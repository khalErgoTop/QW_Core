
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TOPqwWeb_Core.Models.Chatbot;
using TOPqwWeb_Core.Services.Chatbot;

namespace TOPqwWeb_Core.Infrastructure.AI;

public class OllamaQuestionUnderstandingService : IQuestionUnderstandingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OllamaQuestionUnderstandingService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OllamaQuestionUnderstandingService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OllamaQuestionUnderstandingService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ChatbotIntent> UnderstandAsync(
    string question,
    ChatbotConversationState? conversationState,
    CancellationToken cancellationToken)
    {
        var model = _configuration["Ollama:Model"] ?? "phi3:mini";

        // Keep the prompt short because inference runs on CPU.
        const string systemPrompt = """
            Du bist ein deutscher Suchassistent für soziale Einrichtungen.

            Analysiere die Benutzerfrage und gib ausschließlich gültiges JSON
            mit genau diesen Eigenschaften zurück:

            {
              "intent": "find_facilities",
              "conversationAction": "new_search",
              "clearCriteria": [],
              "facilityType": null,
              "category": null,
              "location": null,
              "postalCode": null,
              "distanceKm": null,
              "searchTerms": [],
              "needsClarification": false,
              "clarificationQuestion": null
            }

            Regeln:
            - find_facilities: Suche nach sozialen Einrichtungen oder Angeboten.
            - unsupported_request: Konkrete Stellenanzeigen oder offene Arbeitsplätze.
            - unknown: Frage unverständlich oder zu unklar.
            - facilityType: Gesuchter Einrichtungstyp, z.B. Werkstatt oder Wohnheim.
            - category: Fachbereich, z.B. Arbeit und Beschäftigung, Wohnen, Beratung.
            - location: Nur ausdrücklich genannte Stadt.
            - postalCode: Nur ausdrücklich genannte deutsche fünfstellige PLZ.
            - distanceKm: Nur ausdrücklich gewünschte Entfernung.
            - searchTerms: Maximal 5 kurze relevante deutsche Suchbegriffe.
            - Erfinde keine Standorte, Angebote oder Datenbankinformationen.
            - Erzeuge niemals SQL oder Datenbank-IDs.
            - Bei unklarer Frage: needsClarification = true und eine kurze
              clarificationQuestion auf Deutsch.
            - Nicht genannte Angaben müssen null sein.
            - Berücksichtige den bisherigen Gesprächskontext.
            - Eine kurze Nachricht kann eine Ergänzung oder Änderung der vorherigen Suche sein.
            - Übernimm bisherige Suchkriterien, wenn der Benutzer sie nicht ausdrücklich ändert.
            - Ändere nur die Kriterien, die der Benutzer tatsächlich ändern möchte.
            - Wenn der Benutzer eine völlig neue Suche beginnt, verwende nicht die alten Suchkriterien.
            - Erfinde keine fehlenden Angaben.
            - conversationAction:
              - new_search: Der Benutzer beginnt eine vollständig neue Suche.
              - refine_search: Der Benutzer ergänzt oder ändert eine bestehende Suche.
            - Bei einer kurzen Folgefrage wie "nur in Altona" oder "im Umkreis von 10 km"
              verwende refine_search.
            - Bei einer ausdrücklich neuen Suche oder "von vorne anfangen"
              verwende new_search.
            - Bei refine_search: Übernimm alle bisherigen Kriterien, die nicht geändert wurden.
            - Bei new_search: Ignoriere sämtliche bisherigen Suchkriterien.
            - clearCriteria: Liste der Kriterien, die der Benutzer ausdrücklich entfernen möchte.
            - Erlaubte Werte für clearCriteria:
              facilityType, category, location, postalCode, distanceKm, searchTerms.
            - Wenn keine Kriterien entfernt werden sollen, gib eine leere Liste zurück.
            """;

        var conversationContext = conversationState is null
        ? "Es gibt noch keinen bisherigen Gesprächskontext."
        : JsonSerializer.Serialize(new
        {
            previousCriteria = conversationState.CurrentIntent,
            recentMessages = conversationState.RecentMessages.TakeLast(6)
        }, JsonOptions);

        var request = new
        {
            model,
            stream = false,
            format = "json",
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = $"""
                        Bisheriger Gesprächskontext:
                        {conversationContext}

                        Aktuelle Benutzernachricht:
                        {question}
                        """
                }
            },
            options = new
            {
                temperature = 0.1,
                num_predict = 180
            }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/chat",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var ollamaResponse = await response.Content
                .ReadFromJsonAsync<OllamaChatResponse>(
                    JsonOptions,
                    cancellationToken);

            if (ollamaResponse is not null)
            {
                var totalSeconds = ollamaResponse.TotalDuration / 1_000_000_000d;
                var loadSeconds = ollamaResponse.LoadDuration / 1_000_000_000d;
                var promptSeconds = ollamaResponse.PromptEvalDuration / 1_000_000_000d;
                var generationSeconds = ollamaResponse.EvalDuration / 1_000_000_000d;

                var tokensPerSecond = ollamaResponse.EvalDuration > 0
                    ? ollamaResponse.EvalCount / generationSeconds
                    : 0;

                _logger.LogInformation(
                    """
                    ===== OLLAMA PERFORMANCE =====
                    Total duration: {TotalSeconds:F2} seconds
                    Model loading: {LoadSeconds:F2} seconds
                    Prompt processing: {PromptSeconds:F2} seconds ({PromptTokens} tokens)
                    Response generation: {GenerationSeconds:F2} seconds ({GeneratedTokens} tokens)
                    Generation speed: {TokensPerSecond:F2} tokens/second
                    ==============================
                    """,
                    totalSeconds,
                    loadSeconds,
                    promptSeconds,
                    ollamaResponse.PromptEvalCount,
                    generationSeconds,
                    ollamaResponse.EvalCount,
                    tokensPerSecond);
            }

            var content = ollamaResponse?.Message?.Content;

            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning("Ollama returned an empty response.");
                return CreateErrorIntent();
            }

            var intent = JsonSerializer.Deserialize<ChatbotIntent>(
                content,
                JsonOptions);

            if (intent is null)
            {
                _logger.LogWarning("Ollama returned invalid chatbot JSON.");
                return CreateErrorIntent();
            }

            var validatedIntent = ValidateIntent(intent);

            _logger.LogInformation(
                """
                ===== CHATBOT INTENT =====
                Intent: {Intent}
                FacilityType: {FacilityType}
                Category: {Category}
                Location: {Location}
                PostalCode: {PostalCode}
                DistanceKm: {DistanceKm}
                SearchTerms: {SearchTerms}
                NeedsClarification: {NeedsClarification}
                ==========================
                """,
                validatedIntent.Intent,
                validatedIntent.FacilityType,
                validatedIntent.Category,
                validatedIntent.Location,
                validatedIntent.PostalCode,
                validatedIntent.DistanceKm,
                string.Join(", ", validatedIntent.SearchTerms),
                validatedIntent.NeedsClarification);

            return validatedIntent;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // The user or application explicitly cancelled the request.
            throw;
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                or TaskCanceledException
                or JsonException)
        {
            _logger.LogError(
                exception,
                "Could not understand the question using Ollama.");

            return CreateErrorIntent();
        }
    }

    private static ChatbotIntent ValidateIntent(ChatbotIntent intent)
    {
        var allowedIntents = new[]
        {
            "find_facilities",
            "unsupported_request",
            "unknown"
        };

        var allowedConversationActions = new[]
        {
            "new_search",
            "refine_search"
        };

        if (!allowedConversationActions.Contains(
                intent.ConversationAction,
                StringComparer.OrdinalIgnoreCase))
        {
            intent.ConversationAction = "new_search";
        }

        var allowedClearCriteria = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
            {
                "facilityType",
                "category",
                "location",
                "postalCode",
                "distanceKm",
                "searchTerms"
            };

        intent.ClearCriteria = (intent.ClearCriteria ?? [])
            .Where(allowedClearCriteria.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!allowedIntents.Contains(
                intent.Intent,
                StringComparer.OrdinalIgnoreCase))
        {
            return CreateErrorIntent();
        }

        if (!string.IsNullOrWhiteSpace(intent.PostalCode) &&
            !Regex.IsMatch(intent.PostalCode, @"^\d{5}$"))
        {
            intent.PostalCode = null;
        }

        if (intent.DistanceKm is not null &&
            !new[] { 1, 2, 5, 10, 15, 25, 50 }
                .Contains(intent.DistanceKm.Value))
        {
            intent.DistanceKm = null;
        }

        intent.Location = Clean(intent.Location, 100);
        intent.Category = Clean(intent.Category, 150);
        intent.FacilityType = Clean(intent.FacilityType, 150);
        intent.ClarificationQuestion = Clean(
            intent.ClarificationQuestion, 500);

        intent.SearchTerms = (intent.SearchTerms ?? [])
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Where(term => term.Length <= 100)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (string.Equals(
                intent.Intent,
                "find_facilities",
                StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(intent.Category) &&
            string.IsNullOrWhiteSpace(intent.FacilityType) &&
            intent.SearchTerms.Count == 0)
        {
            return CreateErrorIntent();
        }

        return intent;
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    private static ChatbotIntent CreateErrorIntent()
    {
        return new ChatbotIntent
        {
            Intent = "unknown",
            NeedsClarification = true,
            ClarificationQuestion =
                "Der Einrichtungsassistent ist momentan nicht erreichbar. Bitte versuchen Sie es später erneut."
        };
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }

        [JsonPropertyName("total_duration")]
        public long TotalDuration { get; set; }

        [JsonPropertyName("load_duration")]
        public long LoadDuration { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public long PromptEvalCount { get; set; }

        [JsonPropertyName("prompt_eval_duration")]
        public long PromptEvalDuration { get; set; }

        [JsonPropertyName("eval_count")]
        public long EvalCount { get; set; }

        [JsonPropertyName("eval_duration")]
        public long EvalDuration { get; set; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}
