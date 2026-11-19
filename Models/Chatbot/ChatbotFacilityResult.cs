namespace TOPqwWeb_Core.Models.Chatbot;

public class ChatbotFacilityResult
{
    public string Name { get; set; } = string.Empty;

    public string? FacilityType { get; set; }

    public string? Address { get; set; }

    public decimal? DistanceKm { get; set; }

    public string ResultKind { get; set; } = "Einrichtung";
}
