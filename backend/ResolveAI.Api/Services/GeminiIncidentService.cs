using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ResolveAI.Api.Entities;

namespace ResolveAI.Api.Services;

public class GeminiIncidentService : IAIIncidentService
{
    private const string ProviderName = "Gemini";

    private static readonly string[] AllowedCategories =
    {
        "Hardware",
        "Software",
        "Network",
        "Access",
        "Other"
    };

    private static readonly string[] AllowedPriorities =
    {
        "Low",
        "Medium",
        "High",
        "Critical"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiIncidentService> _logger;

    public GeminiIncidentService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiIncidentService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<AIIncidentAnalysisResult> AnalyzeIncidentAsync(
        Incident incident,
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
                "AI analysis configuration is incomplete.",
                isConfigurationError: true
            );
        }

        if (!string.Equals(
                provider,
                ProviderName,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new AIIncidentAnalysisException(
                "Configured AI provider is not supported.",
                isConfigurationError: true
            );
        }

        var prompt = IncidentAIPromptBuilder.Build(incident);
        var modelId = model.StartsWith(
                "models/",
                StringComparison.OrdinalIgnoreCase
            )
            ? model["models/".Length..]
            : model;

        var requestUri =
            $"/v1beta/models/{Uri.EscapeDataString(modelId)}:generateContent";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri
        )
        {
            Content = JsonContent.Create(BuildGeminiRequest(prompt))
        };

        request.Headers.TryAddWithoutValidation(
            "x-goog-api-key",
            apiKey
        );

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                await LogProviderFailureAsync(
                    response,
                    model,
                    cancellationToken
                );

                throw new AIIncidentAnalysisException(
                    "Gemini returned an unsuccessful response."
                );
            }

            var responseBody = await response.Content.ReadAsStringAsync(
                cancellationToken
            );

            var structuredJson = ExtractCandidateText(responseBody);
            var payload = JsonSerializer.Deserialize<GeminiAnalysisPayload>(
                structuredJson,
                JsonOptions
            );

            return ValidatePayload(payload, model);
        }
        catch (AIIncidentAnalysisException)
        {
            throw;
        }
        catch (OperationCanceledException) when
            (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Gemini incident analysis timed out. Model={Model}.",
                model
            );

            throw new AIIncidentAnalysisException(
                "Gemini incident analysis timed out."
            );
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Gemini incident analysis HTTP request failed. Model={Model}.",
                model
            );

            throw new AIIncidentAnalysisException(
                "Gemini incident analysis request failed.",
                innerException: exception
            );
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(
                exception,
                "Gemini returned invalid incident analysis JSON. Model={Model}.",
                model
            );

            throw new AIIncidentAnalysisException(
                "Gemini returned invalid incident analysis JSON.",
                innerException: exception
            );
        }
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
                maxOutputTokens = 900,
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
                ["categoryRecommendation"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = AllowedCategories
                },
                ["priorityRecommendation"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = AllowedPriorities
                },
                ["urgency"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = AllowedPriorities
                },
                ["confidence"] = new Dictionary<string, object?>
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["maximum"] = 1
                },
                ["possibleCause"] = new Dictionary<string, object?>
                {
                    ["type"] = "string"
                },
                ["reasoningSummary"] = new Dictionary<string, object?>
                {
                    ["type"] = "string"
                },
                ["suggestedActions"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = 6,
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string"
                    }
                }
            },
            ["required"] = new[]
            {
                "categoryRecommendation",
                "priorityRecommendation",
                "urgency",
                "confidence",
                "possibleCause",
                "reasoningSummary",
                "suggestedActions"
            },
            ["propertyOrdering"] = new[]
            {
                "categoryRecommendation",
                "priorityRecommendation",
                "urgency",
                "confidence",
                "possibleCause",
                "reasoningSummary",
                "suggestedActions"
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
                "Gemini returned no incident analysis candidates."
            );
        }

        var firstCandidate = candidates[0];

        if (!firstCandidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned an unexpected incident analysis response."
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
            "Gemini returned an empty incident analysis response."
        );
    }

    private static AIIncidentAnalysisResult ValidatePayload(
        GeminiAnalysisPayload? payload,
        string model)
    {
        if (payload is null)
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned an empty incident analysis result."
            );
        }

        var category = NormalizeAllowedValue(
            payload.CategoryRecommendation,
            AllowedCategories,
            "category"
        );
        var priority = NormalizeAllowedValue(
            payload.PriorityRecommendation,
            AllowedPriorities,
            "priority"
        );
        var urgency = NormalizeAllowedValue(
            payload.Urgency,
            AllowedPriorities,
            "urgency"
        );

        if (!payload.Confidence.HasValue ||
            double.IsNaN(payload.Confidence.Value) ||
            payload.Confidence.Value < 0 ||
            payload.Confidence.Value > 1)
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned an invalid confidence score."
            );
        }

        var possibleCause = ValidateText(
            payload.PossibleCause,
            "possible cause",
            maxLength: 1000
        );
        var reasoningSummary = ValidateText(
            payload.ReasoningSummary,
            "reasoning summary",
            maxLength: 1200
        );
        var suggestedActions = ValidateSuggestedActions(
            payload.SuggestedActions
        );

        return new AIIncidentAnalysisResult
        {
            Provider = ProviderName,
            Model = model,
            CategoryRecommendation = category,
            PriorityRecommendation = priority,
            Urgency = urgency,
            Confidence = Math.Round(payload.Confidence.Value, 2),
            PossibleCause = possibleCause,
            ReasoningSummary = reasoningSummary,
            SuggestedActions = suggestedActions,
            PromptVersion = IncidentAIPromptBuilder.PromptVersion
        };
    }

    private static string NormalizeAllowedValue(
        string? value,
        IReadOnlyList<string> allowedValues,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AIIncidentAnalysisException(
                $"Gemini returned an empty {fieldName}."
            );
        }

        var normalizedValue = allowedValues.FirstOrDefault(
            allowedValue => string.Equals(
                allowedValue,
                value.Trim(),
                StringComparison.OrdinalIgnoreCase
            )
        );

        if (normalizedValue is null)
        {
            throw new AIIncidentAnalysisException(
                $"Gemini returned an unsupported {fieldName}."
            );
        }

        return normalizedValue;
    }

    private static string ValidateText(
        string? value,
        string fieldName,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new AIIncidentAnalysisException(
                $"Gemini returned an empty {fieldName}."
            );
        }

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > maxLength)
        {
            throw new AIIncidentAnalysisException(
                $"Gemini returned a {fieldName} that is too long."
            );
        }

        return trimmedValue;
    }

    private static IReadOnlyList<string> ValidateSuggestedActions(
        IReadOnlyList<string>? suggestedActions)
    {
        if (suggestedActions is null || suggestedActions.Count == 0)
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned no suggested actions."
            );
        }

        var actions = suggestedActions
            .Select(action => action.Trim())
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .ToList();

        if (actions.Count == 0)
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned no usable suggested actions."
            );
        }

        if (actions.Count > 6 ||
            actions.Any(action => action.Length > 300))
        {
            throw new AIIncidentAnalysisException(
                "Gemini returned suggested actions outside allowed limits."
            );
        }

        return actions;
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
                "Gemini incident analysis was rate limited. Model={Model}.",
                model
            );
            return;
        }

        string? errorBody = null;

        try
        {
            var raw = await response.Content.ReadAsStringAsync(
                cancellationToken
            );

            if (!string.IsNullOrWhiteSpace(raw))
            {
                // Truncate to avoid log flooding; never log secrets
                errorBody = raw.Length <= 600 ? raw : raw[..600] + "…";
            }
        }
        catch
        {
            // Best-effort only
        }

        _logger.LogWarning(
            "Gemini incident analysis returned HTTP {StatusCode}. " +
            "Model={Model}. ResponseBody={Body}",
            (int)statusCode,
            model,
            errorBody ?? "(empty)"
        );
    }

    private sealed class GeminiAnalysisPayload
    {
        public string? CategoryRecommendation { get; set; }

        public string? PriorityRecommendation { get; set; }

        public string? Urgency { get; set; }

        public double? Confidence { get; set; }

        public string? PossibleCause { get; set; }

        public string? ReasoningSummary { get; set; }

        public List<string>? SuggestedActions { get; set; }
    }
}
