namespace TOPqwWeb_Core.Models.Chatbot
{
    public class ChatbotIntent
    {
        public string Intent { get; set; } = "unknown";

        public string ConversationAction { get; set; } = "new_search";

        public List<string> ClearCriteria { get; set; } = [];

        public string? FacilityType { get; set; }

        public string? Category { get; set; }

        public string? Location { get; set; }

        public string? PostalCode { get; set; }

        public List<string> SearchTerms { get; set; } = [];

        public bool NeedsClarification { get; set; }

        public string? ClarificationQuestion { get; set; }
        public int? DistanceKm { get; set; }
    }
}
