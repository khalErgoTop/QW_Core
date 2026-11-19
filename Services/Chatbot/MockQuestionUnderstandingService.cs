
using TOPqwWeb_Core.Models.Chatbot;
using System.Text.RegularExpressions;

namespace TOPqwWeb_Core.Services.Chatbot;

public class MockQuestionUnderstandingService : IQuestionUnderstandingService
{
    public Task<ChatbotIntent> UnderstandAsync(
        string question,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var text = question.Trim().ToLowerInvariant();

        // The database does not contain actual job vacancies.
        if (text.Contains("arbeitsangebot") ||
            text.Contains("stellenangebot") ||
            text.Contains("jobangebot") ||
            text.Contains("arbeitsplatz"))
        {
            return Task.FromResult(new ChatbotIntent
            {
                Intent = "unsupported_request"
            });
        }

        var intent = new ChatbotIntent
        {
            Intent = "find_facilities"
        };

        // Extract a German postal code.
        var postalCodeMatch = Regex.Match(text, @"\b\d{5}\b");

        if (postalCodeMatch.Success)
        {
            intent.PostalCode = postalCodeMatch.Value;
            intent.DistanceKm = 25;
        }

        // Extract an explicitly requested distance.
        var distanceMatch = Regex.Match(text, @"\b(\d+)\s*km\b");

        if (distanceMatch.Success &&
            int.TryParse(distanceMatch.Groups[1].Value, out var requestedDistance))
        {
            intent.DistanceKm = requestedDistance;
        }

        // Extract a city following "in", "bei" or "nahe".
        var cityMatch = Regex.Match(
            question,
            @"\b(?:in|bei|nahe)\s+([A-ZÄÖÜ][\p{L}-]*(?:\s+(?:am|an|der|den|dem)\s+[A-ZÄÖÜ][\p{L}-]*)*)");

        if (cityMatch.Success)
        {
            intent.Location = cityMatch.Groups[1].Value.Trim();
        }

        if (text.Contains("arbeit") ||
            text.Contains("beschäftigung") ||
            text.Contains("werkstatt") ||
            text.Contains("werkstätt"))
        {
            intent.Category = "Arbeit und Beschäftigung";
            intent.SearchTerms = ["Arbeit", "Beschäftigung", "Werkstatt"];
        }
        else if (text.Contains("wohnen") ||
                 text.Contains("wohnheim"))
        {
            intent.Category = "Wohnen";
            intent.SearchTerms = ["Wohnen", "Wohnheim"];
        }
        else if (text.Contains("tagespflege"))
        {
            intent.Category = "Tagespflege";
            intent.SearchTerms = ["Tagespflege"];
        }
        else if (text.Contains("beratung"))
        {
            intent.Category = "Beratung";
            intent.SearchTerms = ["Beratung"];
        }
        else
        {
            intent.Intent = "unknown";
            intent.NeedsClarification = true;
            intent.ClarificationQuestion =
                "Welche Art von Einrichtung suchen Sie?";
        }

        return Task.FromResult(intent);
    }
}
