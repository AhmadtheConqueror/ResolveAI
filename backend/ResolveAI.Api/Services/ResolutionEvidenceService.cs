using Microsoft.EntityFrameworkCore;
using ResolveAI.Api.Data;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Models.AI;

namespace ResolveAI.Api.Services;

public class ResolutionEvidenceService : IResolutionEvidenceService
{
    private const int MaxAllowedCandidates = 30;
    private const int DefaultMaxCandidates = 25;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "about", "above", "after", "again", "against", "all", "am", "an", "and",
        "any", "are", "as", "at", "be", "because", "been", "before", "being", "below",
        "between", "both", "but", "by", "can", "could", "did", "do", "does", "doing",
        "down", "during", "each", "few", "for", "from", "further", "had", "has", "have",
        "having", "he", "her", "here", "hers", "herself", "him", "himself", "his", "how",
        "i", "if", "in", "into", "is", "it", "its", "itself", "just", "me", "more",
        "most", "my", "myself", "no", "nor", "not", "now", "of", "off", "on", "once",
        "only", "or", "other", "our", "ours", "ourselves", "out", "over", "own", "s",
        "same", "she", "should", "so", "some", "such", "t", "than", "that", "the",
        "their", "theirs", "them", "themselves", "then", "there", "these", "they",
        "this", "those", "through", "to", "too", "under", "until", "up", "very",
        "was", "we", "were", "what", "when", "where", "which", "while", "who", "whom",
        "why", "will", "with", "would", "you", "your", "yours", "yourself", "yourselves"
    };

    private readonly AppDbContext _context;
    private readonly ILogger<ResolutionEvidenceService> _logger;

    public ResolutionEvidenceService(
        AppDbContext context,
        ILogger<ResolutionEvidenceService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ResolutionEvidenceCandidate>> GetCandidatesAsync(
        Incident currentIncident,
        int maxCandidates = DefaultMaxCandidates,
        CancellationToken cancellationToken = default)
    {
        var boundedLimit = Math.Clamp(maxCandidates, 1, MaxAllowedCandidates);

        // Base qualification:
        // 1. Exclude current incident
        // 2. ResolvedAt must not be null (genuine resolution; excludes administrative closures)
        // 3. Resolution must not be empty/whitespace
        var baseQuery = _context.Incidents
            .AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.Priority)
            .Where(i => i.Id != currentIncident.Id
                && i.ResolvedAt != null
                && i.Resolution != null
                && i.Resolution.Trim() != string.Empty);

        var currentTokens = ExtractTokens(
            $"{currentIncident.Title} {currentIncident.Description}"
        );

        // 1. First stage: Bounded candidate retrieval from database
        // Query same category pool (up to 30)
        var sameCategoryCandidates = await baseQuery
            .Where(i => i.CategoryId == currentIncident.CategoryId)
            .OrderByDescending(i => i.ResolvedAt)
            .Take(30)
            .ToListAsync(cancellationToken);

        // Query text-matching pool if significant search terms exist (up to 30)
        var topTerms = currentTokens.Take(5).ToList();
        var textMatchedCandidates = new List<Incident>();
        if (topTerms.Count > 0)
        {
            var firstTerm = topTerms[0];
            var secondTerm = topTerms.Count > 1 ? topTerms[1] : null;

            var textQuery = baseQuery.Where(i =>
                i.Title.ToLower().Contains(firstTerm) ||
                i.Description.ToLower().Contains(firstTerm) ||
                i.Resolution!.ToLower().Contains(firstTerm));

            if (secondTerm != null)
            {
                textQuery = baseQuery.Where(i =>
                    i.Title.ToLower().Contains(firstTerm) ||
                    i.Title.ToLower().Contains(secondTerm) ||
                    i.Description.ToLower().Contains(firstTerm) ||
                    i.Description.ToLower().Contains(secondTerm) ||
                    i.Resolution!.ToLower().Contains(firstTerm) ||
                    i.Resolution!.ToLower().Contains(secondTerm));
            }

            textMatchedCandidates = await textQuery
                .OrderByDescending(i => i.ResolvedAt)
                .Take(30)
                .ToListAsync(cancellationToken);
        }

        // Query recent resolved fallback (up to 15)
        var recentCandidates = await baseQuery
            .OrderByDescending(i => i.ResolvedAt)
            .Take(15)
            .ToListAsync(cancellationToken);

        // Combine bounded pools distinctly
        var combinedCandidates = new Dictionary<Guid, Incident>();
        foreach (var inc in sameCategoryCandidates)
        {
            combinedCandidates[inc.Id] = inc;
        }
        foreach (var inc in textMatchedCandidates)
        {
            combinedCandidates[inc.Id] = inc;
        }
        foreach (var inc in recentCandidates)
        {
            combinedCandidates[inc.Id] = inc;
        }

        if (combinedCandidates.Count == 0)
        {
            _logger.LogInformation(
                "No eligible historical resolved incident candidates found for incident {IncidentNumber}.",
                currentIncident.IncidentNumber);
            return Array.Empty<ResolutionEvidenceCandidate>();
        }

        // 2. Second stage: In-memory deterministic scoring over the bounded pool
        var scoredCandidates = new List<ResolutionEvidenceCandidate>();

        foreach (var candidate in combinedCandidates.Values)
        {
            var score = CalculateCandidateScore(
                currentIncident,
                currentTokens,
                candidate
            );

            // Candidate must have some relevance (token match or category match)
            if (score <= 0)
            {
                continue;
            }

            scoredCandidates.Add(new ResolutionEvidenceCandidate
            {
                IncidentId = candidate.Id,
                IncidentNumber = candidate.IncidentNumber,
                Title = candidate.Title,
                DescriptionExcerpt = Truncate(candidate.Description, 300),
                Category = candidate.Category?.Name ?? "General",
                Priority = candidate.Priority?.Name ?? "Medium",
                ResolutionExcerpt = Truncate(candidate.Resolution ?? string.Empty, 600),
                ResolvedAt = candidate.ResolvedAt,
                CandidateScore = Math.Round(score, 2)
            });
        }

        // Order deterministically by score descending, then resolved date descending
        var finalCandidates = scoredCandidates
            .OrderByDescending(c => c.CandidateScore)
            .ThenByDescending(c => c.ResolvedAt)
            .Take(boundedLimit)
            .ToList();

        _logger.LogInformation(
            "Retrieved {Count} resolution evidence candidates (bounded max {Max}) for incident {IncidentNumber}.",
            finalCandidates.Count,
            boundedLimit,
            currentIncident.IncidentNumber);

        return finalCandidates;
    }

    public static double CalculateCandidateScore(
        Incident currentIncident,
        HashSet<string> currentTokens,
        Incident candidate)
    {
        double score = 0;

        // Category match bonus: +12 points (useful relevance bonus, but not overwhelming)
        if (candidate.CategoryId == currentIncident.CategoryId)
        {
            score += 12.0;
        }

        if (currentTokens.Count == 0)
        {
            return score;
        }

        var candidateTitleTokens = ExtractTokens(candidate.Title);
        var candidateDescTokens = ExtractTokens(candidate.Description);
        var candidateResTokens = ExtractTokens(candidate.Resolution ?? string.Empty);

        // Title token matches (+10 points each)
        var titleMatches = currentTokens.Intersect(candidateTitleTokens).Count();
        score += titleMatches * 10.0;

        // Resolution token matches (+6 points each)
        var resMatches = currentTokens.Intersect(candidateResTokens).Count();
        score += resMatches * 6.0;

        // Description token matches (+4 points each)
        var descMatches = currentTokens.Intersect(candidateDescTokens).Count();
        score += descMatches * 4.0;

        // Recency tie-breaker bonus (up to 5 points for incidents resolved within the last year)
        if (candidate.ResolvedAt.HasValue)
        {
            var ageDays = Math.Max(0, (DateTime.UtcNow - candidate.ResolvedAt.Value).TotalDays);
            var recencyBonus = Math.Max(0, 5.0 - (ageDays / 73.0)); // 5.0 at day 0 down to 0 at day 365
            score += recencyBonus;
        }

        return score;
    }

    public static HashSet<string> ExtractTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = text.Split(
            new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '-', '_', '/', '\\', '(', ')', '[', ']', '{', '}', '"', '\'', '`' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var word in words)
        {
            var cleanWord = word.ToLowerInvariant();
            if (cleanWord.Length >= 3 && !StopWords.Contains(cleanWord) && !int.TryParse(cleanWord, out _))
            {
                tokens.Add(cleanWord);
            }
        }

        return tokens;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..maxLength] + "…";
    }
}
