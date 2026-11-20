
using System.Text.RegularExpressions;
using TOPqwWeb_Core.Legacy.Bridge;
using TOPqwWeb_Core.Models.Chatbot;
using TOPqwWeb_Core.Services.Public;

namespace TOPqwWeb_Core.Services.Chatbot;

public class ChatbotService
{
    private readonly IQuestionUnderstandingService _understandingService;
    private readonly SuchePageService _searchService;

    private readonly ILogger<ChatbotService> _logger;

    private readonly ChatbotConversationStore _conversationStore;

    public ChatbotService(
    IQuestionUnderstandingService understandingService,
    SuchePageService searchService,
    ChatbotConversationStore conversationStore,
    ILogger<ChatbotService> logger)
    {
        _understandingService = understandingService;
        _searchService = searchService;
        _conversationStore = conversationStore;
        _logger = logger;
    }

    public async Task<ChatbotResponse> ProcessAsync(
        string question,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new ChatbotResponse
            {
                Message = "Bitte geben Sie eine Frage ein.",
                NeedsClarification = true
            };
        }

        var conversationState = _conversationStore.GetState();

        var intent = await _understandingService.UnderstandAsync(
            question, conversationState, cancellationToken);

        if (intent.NeedsClarification ||
            string.Equals(intent.Intent, "unknown",
                StringComparison.OrdinalIgnoreCase))
        {
            return new ChatbotResponse
            {
                Message = intent.ClarificationQuestion
                    ?? "Könnten Sie Ihre Frage bitte genauer formulieren?",
                NeedsClarification = true
            };
        }

        if (string.Equals(intent.Intent, "unsupported_request",
            StringComparison.OrdinalIgnoreCase))
        {
            return new ChatbotResponse
            {
                Message = "Unsere Datenbank enthält Informationen über Einrichtungen und deren Angebote, aber keine konkreten Stellenanzeigen oder offenen Arbeitsplätze."
            };
        }

        if (!string.Equals(intent.Intent, "find_facilities",
            StringComparison.OrdinalIgnoreCase))
        {
            return new ChatbotResponse
            {
                Message = "Diese Art von Anfrage wird derzeit nicht unterstützt.",
                NeedsClarification = true
            };
        }

        var postalCode = Regex.IsMatch(
            intent.PostalCode ?? string.Empty, @"^\d{5}$")
            ? intent.PostalCode!
            : string.Empty;

        var city = (intent.Location ?? string.Empty).Trim();

        // A German five-digit postal code must never be treated as a city.
        if (Regex.IsMatch(city, @"^\d{5}$") ||
            string.Equals(city, postalCode, StringComparison.OrdinalIgnoreCase))
        {
            city = string.Empty;
        }
        var supportedDistances = new[] { 1, 2, 5, 10, 15, 25, 50 };

        var distanceKm = postalCode.Length == 5
            ? supportedDistances.Contains(intent.DistanceKm ?? 25)
                ? intent.DistanceKm ?? 25
                : 25
            : 0;

        // Retrieve available facility types and their real database IDs.
        var metadata = await _searchService.SearchAsync(
            new PublicSearchRequest(),
            cancellationToken);

        var matchingTypes = ResolveBroadFacilityTypes(
            intent, metadata.FacilityTypes);

        var matchingType = matchingTypes.Count == 0
            ? ResolveFacilityType(intent, metadata.FacilityTypes)
            : null;

        _logger.LogInformation(
            "CHATBOT TYPE MATCH: {MatchedType}",
            matchingType?.Name ?? "NO MATCH");

        foreach (var type in metadata.FacilityTypes)
        {
            _logger.LogInformation(
                "AVAILABLE FACILITY TYPE: {Name} | Category: {Category} | ID: {Id}",
                type.Name,
                type.Category,
                type.Id);
        }

        var collectedResults = new Dictionary<string, PublicSearchResultResponse>();
        var hasMoreResults = false;

        if (matchingType is not null)
        {
            // A specific facility type was found.
            var result = await _searchService.SearchAsync(
                new PublicSearchRequest
                {
                    FacilityType = matchingType.Id,
                    City = city,
                    PostalCode = postalCode,
                    DistanceKm = distanceKm,
                    WithFreePlaces = false,
                    FacilityFeatures = []
                },
                cancellationToken);

            AddResults(result);
        }
        else if (matchingTypes.Count > 0)
        {
            // A broad concept matches multiple facility types.
            foreach (var type in matchingTypes)
            {
                _logger.LogInformation(
                    "CHATBOT MULTI-TYPE MATCH: {Name} | ID: {Id}",
                    type.Name,
                    type.Id);

                var result = await _searchService.SearchAsync(
                    new PublicSearchRequest
                    {
                        FacilityType = type.Id,
                        City = city,
                        PostalCode = postalCode,
                        DistanceKm = distanceKm,
                        WithFreePlaces = false,
                        FacilityFeatures = []
                    },
                    cancellationToken);

                AddResults(result);
            }
        }
        else
        {
            // No facility type matched. Try individual search terms.
            var genericTerms = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
                {
                    "Behinderung",
                    "Behinderte",
                    "Menschen",
                    "Menschen mit Behinderung",
                    "Einrichtung",
                    "Einrichtungen"
                };

            var searchTerms = (intent.SearchTerms ?? [])
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .Select(term => term.Trim())
                .Where(term => !Regex.IsMatch(term, @"^\d{5}$"))
                .Where(term => !string.Equals(
                    term,
                    city,
                    StringComparison.OrdinalIgnoreCase))
                .Where(term => !genericTerms.Contains(term))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (searchTerms.Count == 0 &&
                !string.IsNullOrWhiteSpace(intent.Category))
            {
                searchTerms.Add(intent.Category);
            }

            foreach (var term in searchTerms)
            {
                var result = await _searchService.SearchAsync(
                    new PublicSearchRequest
                    {
                        SearchValue = term,
                        City = city,
                        PostalCode = postalCode,
                        FacilityType = 0,
                        DistanceKm = distanceKm,
                        WithFreePlaces = false,
                        FacilityFeatures = []
                    },
                    cancellationToken);

                AddResults(result);
            }
        }

        foreach (var result in collectedResults.Values)
        {
            _logger.LogInformation(
                """
                ===== FACILITY RESULT =====
                EIID: {EIID}
                STUID: {STUID}
                Name: {Name}
                FacilityType: {FacilityType}
                Address: {Address}
                ===========================
                """,
                result.EIID,
                result.STUID,
                result.Name,
                result.FacilityType,
                result.Address);
        }

        var facilities = collectedResults.Values
            .Select(result => new ChatbotFacilityResult
            {
                Name = result.Name,
                FacilityType = result.FacilityType,
                Address = result.Address,
                DistanceKm = result.DistanceKm,
                ResultKind = result.ResultKind
            })
            .ToList();

        var message = facilities.Count == 0
            ? "Ich konnte leider keine passenden Einrichtungen finden. Versuchen Sie bitte einen anderen Suchbegriff."
            : $"Ich habe {facilities.Count} passende Einrichtung(en) gefunden.";

        if (hasMoreResults)
        {
            message += " Es gibt weitere Suchergebnisse. Angezeigt werden maximal 50 Ergebnisse pro Suche.";
        }

        return new ChatbotResponse
        {
            Message = message,
            Facilities = facilities,
            HasMoreResults = hasMoreResults
        };

        void AddResults(PublicSearchResponse result)
        {
            hasMoreResults |= result.HasMoreResults;

            foreach (var facility in result.Results)
            {
                var key = $"{facility.EIID}_{facility.STUID}";

                if (!collectedResults.ContainsKey(key))
                {
                    collectedResults.Add(key, facility);
                }
            }
        }
    }

    private static List<PublicSearchTypeResponse> ResolveBroadFacilityTypes(
    ChatbotIntent intent,
    List<PublicSearchTypeResponse> availableTypes)
    {
        var category = Normalize(intent.Category ?? "");
        var facilityType = Normalize(intent.FacilityType ?? "");

        var searchTerms = (intent.SearchTerms ?? [])
            .Select(Normalize)
            .ToList();

        // Housing is a broad concept that can include several services.
        var isHousingRequest =
            category == "wohnen" ||
            facilityType == "wohnheim" ||
            searchTerms.Contains("wohnen") ||
            searchTerms.Contains("wohnheim");

        if (!isHousingRequest)
            return [];

        // Match against actual facility type names, not broad categories.
        // This also catches Wohnraum and wohnbegleitende services.
        return availableTypes
            .Where(type =>
                Normalize(type.Name).Contains("wohn"))
            .ToList();
    }


    private static PublicSearchTypeResponse? ResolveFacilityType(
        ChatbotIntent intent,
        List<PublicSearchTypeResponse> availableTypes)
    {
        var scoredTypes = new List<(PublicSearchTypeResponse Type, int Score)>();

        foreach (var type in availableTypes)
        {
            var searchableText = Normalize(
                $"{type.Name} {type.Category}");

            var score = 0;

            if (!string.IsNullOrWhiteSpace(intent.FacilityType))
            {
                var facilityType = Normalize(intent.FacilityType);

                if (facilityType.Length >= 3 &&
                    searchableText.Contains(facilityType))
                {
                    score += 20;
                }
            }

            if (!string.IsNullOrWhiteSpace(intent.Category))
            {
                var category = Normalize(intent.Category);

                if (category.Length >= 3 &&
                    searchableText.Contains(category))
                {
                    score += 10;
                }
            }

            foreach (var term in intent.SearchTerms ?? [])
            {
                if (string.IsNullOrWhiteSpace(term))
                    continue;

                var normalizedTerm = Normalize(term);

                if (normalizedTerm == Normalize(intent.Location ?? "") ||
                    normalizedTerm == Normalize(intent.PostalCode ?? ""))
                {
                    continue;
                }

                if (normalizedTerm.Length >= 3 &&
                    searchableText.Contains(normalizedTerm))
                {
                    score += 3;
                }
            }

            if (score > 0)
            {
                scoredTypes.Add((type, score));
            }
        }

        var rankedTypes = scoredTypes
            .OrderByDescending(x => x.Score)
            .ToList();

        if (rankedTypes.Count == 0)
            return null;

        var bestMatch = rankedTypes[0];

        // A weak or ambiguous match must not select a database type.
        if (bestMatch.Score < 10)
            return null;

        // Require a meaningful score difference from the second candidate.
        if (rankedTypes.Count > 1 &&
            bestMatch.Score - rankedTypes[1].Score < 5)
        {
            return null;
        }

        return bestMatch.Type;
    }


    private static string Normalize(string value)
    {
        return Regex.Replace(
            value.ToLowerInvariant(),
            @"[^\p{L}\p{N}]+",
            " ").Trim();
    }
}
