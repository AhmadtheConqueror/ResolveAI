using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResolveAI.Api.Entities;
using ResolveAI.Api.Models.AI;

namespace ResolveAI.Api.Services;

public class GeminiResolutionAssistantService : IAIResolutionAssistantService
{
    private const string ProviderName = "Gemini";
    private const int MaxSimilarIncidents = 5;

    private static readonly string[] AllowedConfidenceLevels =
    {
        "High",
        "Moderate",
        "Low"
    };

    private static readonly string[] AllowedMatchStrengths =
    {
        "High",
        "Moderate",
        "Low"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiResolutionAssistantService> _logger;

    public GeminiResolutionAssistantService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiResolutionAssistantService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AIResolutionAnalysisResult> GenerateResolutionAssistanceAsync(
        Incident incident,
        IReadOnlyList<ResolutionEvidenceCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var provider = _configuration["AI:Provider"]?.Trim();
        var model = _configuration["AI:Model"]?.Trim();
        var apiKey = _configuration["AI:ApiKey"]?.Trim();

        if (string.IsNullOrWhiteSpace(provider) ||
            string.IsNullOrWhiteSpace(model) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AIIncidentAnalysisException(
                "AI resolution assistance configuration is incomplete.",
                isConfigurationError: true
            );
        }

        if (!string.Equals(provider, ProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new AIIncidentAnalysisException(
                "Configured AI provider is not supported.",
                isConfigurationError: true
            );
        }

        var prompt = ResolutionAssistantPromptBuilder.Build(incident, candidates);
        var modelId = model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model["models/".Length..]
            : model;

        var requestUri = $"/v1beta/models/{Uri.EscapeDataString(modelId)}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(BuildGeminiRequest(prompt))
        };

        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                await LogProviderFailureAsync(response, model, cancellationToken);
                throw new AIIncidentAnalysisException(
                    "Gemini returned an unsuccessful response."
                );
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var structuredJson = ExtractCandidateText(responseBody);
            var payload = JsonSerializer.Deserialize<GeminiResolutionPayload>(
                structuredJson,
                JsonOptions
            );

            return ValidateAndSanitizePayload(payload, candidates, model);
        }
        catch (AIIncidentAnalysisException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Gemini resolution assistance timed out. Model={Model}.",
                model
            );
            throw new AIIncidentAnalysisException(
                "Gemini resolution assistance timed out."
            );
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Gemini resolution assistance HTTP request failed. Model={Model}.",
                model
            );
            throw new AIIncidentAnalysisException(
                "Gemini resolution assistance request failed.",
                innerException: exception
            );
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Gemini returned invalid resolution assistance JSON. Model={Model}.",
                model
            );
            throw new AIIncidentAnalysisException(
                "Gemini returned invalid resolution assistance JSON.",
                innerException: exception
            );
        }
    }

    public static AIResolutionAnalysisResult ValidateAndSanitizePayload(
        GeminiResolutionPayload? payload,
        IReadOnlyList<ResolutionEvidenceCandidate> candidates,
        string model)
    {
        if (payload is null)
        {
            throw new AIIncidentAnalysisException("Gemini returned an empty resolution assistant result.");
        }

        var candidateWhitelistById = candidates.ToDictionary(c => c.IncidentId);
        var candidateWhitelistByNumber = candidates.ToDictionary(
            c => c.IncidentNumber.Trim().ToUpperInvariant(),
            c => c);

        var summary = ValidateText(payload.CurrentSummary, "current summary", 2000, "The incident requires technical investigation.");
        var likelyIssue = ValidateText(payload.LikelyIssue, "likely issue", 500, "Investigation needed to isolate root cause.");

        var confidence = NormalizeAllowedValue(
            payload.Confidence,
            AllowedConfidenceLevels,
            "confidence",
            defaultValue: "Low"
        );

        // Validate and sanitize similar incident evidence against whitelist
        var validatedEvidence = new List<SimilarResolvedIncidentEvidence>();
        var seenEvidenceIds = new HashSet<Guid>();

        if (payload.Evidence != null)
        {
            foreach (var item in payload.Evidence)
            {
                if (validatedEvidence.Count >= MaxSimilarIncidents)
                {
                    break;
                }

                ResolutionEvidenceCandidate? matchedCandidate = null;

                if (Guid.TryParse(item.IncidentId, out var parsedId) &&
                    candidateWhitelistById.TryGetValue(parsedId, out var byIdCandidate))
                {
                    matchedCandidate = byIdCandidate;
                }
                else if (!string.IsNullOrWhiteSpace(item.IncidentNumber) &&
                         candidateWhitelistByNumber.TryGetValue(item.IncidentNumber.Trim().ToUpperInvariant(), out var byNumberCandidate))
                {
                    matchedCandidate = byNumberCandidate;
                }

                // Strictly drop any evidence not in candidate whitelist
                if (matchedCandidate is null)
                {
                    continue;
                }

                // Drop duplicate evidence
                if (!seenEvidenceIds.Add(matchedCandidate.IncidentId))
                {
                    continue;
                }

                var matchStrength = NormalizeAllowedValue(
                    item.MatchStrength,
                    AllowedMatchStrengths,
                    "match strength",
                    defaultValue: "Moderate"
                );

                var reasonForMatch = ValidateText(
                    item.ReasonForMatch,
                    "reason for match",
                    600,
                    "Historically resolved with similar operational symptoms."
                );

                validatedEvidence.Add(new SimilarResolvedIncidentEvidence
                {
                    IncidentId = matchedCandidate.IncidentId,
                    IncidentNumber = matchedCandidate.IncidentNumber,
                    Title = matchedCandidate.Title,
                    Category = matchedCandidate.Category,
                    ResolvedAt = matchedCandidate.ResolvedAt,
                    ResolutionExcerpt = matchedCandidate.ResolutionExcerpt,
                    MatchStrength = matchStrength,
                    ReasonForMatch = reasonForMatch
                });
            }
        }

        var validatedEvidenceNumbers = validatedEvidence
            .Select(e => e.IncidentNumber.Trim().ToUpperInvariant())
            .ToHashSet();

        // Validate and sanitize suggested troubleshooting steps
        var validatedSteps = new List<SuggestedResolutionStep>();
        if (payload.SuggestedSteps != null)
        {
            var stepIndex = 1;
            foreach (var rawStep in payload.SuggestedSteps)
            {
                if (validatedSteps.Count >= 8)
                {
                    break;
                }

                var action = rawStep.Action?.Trim();
                if (string.IsNullOrWhiteSpace(action))
                {
                    continue;
                }
                if (action.Length > 400)
                {
                    action = action[..400];
                }

                var reason = rawStep.Reason?.Trim() ?? string.Empty;
                if (reason.Length > 600)
                {
                    reason = reason[..600];
                }

                // Filter evidence references: only retain validated evidence incident numbers
                var stepEvidence = new List<string>();
                if (rawStep.EvidenceIncidentNumbers != null)
                {
                    foreach (var num in rawStep.EvidenceIncidentNumbers)
                    {
                        var cleanNum = num?.Trim();
                        if (!string.IsNullOrWhiteSpace(cleanNum) &&
                            validatedEvidenceNumbers.Contains(cleanNum.ToUpperInvariant()) &&
                            !stepEvidence.Contains(cleanNum, StringComparer.OrdinalIgnoreCase))
                        {
                            stepEvidence.Add(cleanNum);
                        }
                    }
                }

                validatedSteps.Add(new SuggestedResolutionStep
                {
                    StepNumber = stepIndex++,
                    Action = action,
                    Reason = reason,
                    EvidenceIncidentNumbers = stepEvidence
                });
            }
        }

        if (validatedSteps.Count == 0)
        {
            validatedSteps.Add(new SuggestedResolutionStep
            {
                StepNumber = 1,
                Action = "Verify the current incident symptoms and gather diagnostic details.",
                Reason = "Initial triage to isolate the reported problem.",
                EvidenceIncidentNumbers = new List<string>()
            });
        }

        var hasSufficientEvidence = validatedEvidence.Count > 0;

        string caveats;
        if (!hasSufficientEvidence)
        {
            // When there is insufficient historical evidence, reduce confidence and state clearly
            if (confidence == "High")
            {
                confidence = "Low";
            }

            caveats = "General AI guidance — not derived from ResolveAI historical incidents. A Technician must validate all steps before making technical changes.";
        }
        else
        {
            caveats = "These suggestions are based on historically similar ResolveAI incidents. A Technician must validate them against the current environment before taking action.";
        }

        return new AIResolutionAnalysisResult
        {
            Summary = summary,
            LikelyIssue = likelyIssue,
            Confidence = confidence,
            SuggestedSteps = validatedSteps,
            Evidence = validatedEvidence,
            Caveats = caveats,
            HasSufficientEvidence = hasSufficientEvidence,
            CandidateCount = candidates.Count,
            Provider = ProviderName,
            Model = model,
            PromptVersion = ResolutionAssistantPromptBuilder.PromptVersion
        };
    }

    private static object BuildGeminiRequest(string prompt)
    {
        return new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new
                        {
                            text = prompt
                        }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 1400,
                responseMimeType = "application/json",
                responseSchema = BuildResponseSchema()
            }
        };
    }

    private static object BuildResponseSchema()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object?>
            {
                ["currentSummary"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["likelyIssue"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["confidence"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = AllowedConfidenceLevels
                },
                ["suggestedSteps"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["stepNumber"] = new Dictionary<string, object?> { ["type"] = "integer" },
                            ["action"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["reason"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["evidenceIncidentNumbers"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                            }
                        },
                        ["required"] = new[] { "stepNumber", "action", "reason", "evidenceIncidentNumbers" }
                    }
                },
                ["evidence"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["incidentId"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["incidentNumber"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["title"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["category"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["resolvedAt"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["resolutionExcerpt"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["matchStrength"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = AllowedMatchStrengths
                            },
                            ["reasonForMatch"] = new Dictionary<string, object?> { ["type"] = "string" }
                        },
                        ["required"] = new[] { "incidentId", "incidentNumber", "title", "matchStrength", "reasonForMatch" }
                    }
                },
                ["caveats"] = new Dictionary<string, object?> { ["type"] = "string" },
                ["isGeneralGuidanceOnly"] = new Dictionary<string, object?> { ["type"] = "boolean" }
            },
            ["required"] = new[]
            {
                "currentSummary",
                "likelyIssue",
                "confidence",
                "suggestedSteps",
                "evidence",
                "caveats",
                "isGeneralGuidanceOnly"
            }
        };
    }

    private static string ExtractCandidateText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned no resolution assistant candidates."
            );
        }

        var firstCandidate = candidates[0];

        if (!firstCandidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned an unexpected resolution assistant response structure."
            );
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                var value = text.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        throw new AIIncidentAnalysisException(
            "Gemini returned an empty resolution assistant response."
        );
    }

    private static string NormalizeAllowedValue(
        string? value,
        IReadOnlyList<string> allowedValues,
        string fieldName,
        string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var normalized = allowedValues.FirstOrDefault(
            a => string.Equals(a, value.Trim(), StringComparison.OrdinalIgnoreCase)
        );

        return normalized ?? defaultValue;
    }

    private static string ValidateText(
        string? value,
        string fieldName,
        int maxLength,
        string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private async Task LogProviderFailureAsync(
        HttpResponseMessage response,
        string model,
        CancellationToken cancellationToken)
    {
        var statusCode = response.StatusCode;
        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(
                "Gemini resolution assistant was rate limited. Model={Model}.",
                model
            );
            return;
        }

        string? errorBody = null;
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                errorBody = raw.Length <= 600 ? raw : raw[..600] + "…";
            }
        }
        catch
        {
            // Best-effort only; never leak secrets
        }

        _logger.LogWarning(
            "Gemini resolution assistant returned HTTP {StatusCode}. Model={Model}. ResponseBody={Body}",
            (int)statusCode,
            model,
            errorBody ?? "(empty)"
        );
    }

    public sealed class GeminiResolutionPayload
    {
        public string? CurrentSummary { get; set; }
        public string? LikelyIssue { get; set; }
        public string? Confidence { get; set; }
        public List<GeminiSuggestedStepPayload>? SuggestedSteps { get; set; }
        public List<GeminiEvidencePayload>? Evidence { get; set; }
        public string? Caveats { get; set; }
        public bool? IsGeneralGuidanceOnly { get; set; }
    }

    public sealed class GeminiSuggestedStepPayload
    {
        public int? StepNumber { get; set; }
        public string? Action { get; set; }
        public string? Reason { get; set; }
        public List<string>? EvidenceIncidentNumbers { get; set; }
    }

    public sealed class GeminiEvidencePayload
    {
        public string? IncidentId { get; set; }
        public string? IncidentNumber { get; set; }
        public string? Title { get; set; }
        public string? Category { get; set; }
        public string? ResolvedAt { get; set; }
        public string? ResolutionExcerpt { get; set; }
        public string? MatchStrength { get; set; }
        public string? ReasonForMatch { get; set; }
    }
}
