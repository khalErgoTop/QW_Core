
namespace TOPqwWeb_Core.Models.Chatbot;

public class ChatbotResponse
{
    public string Message { get; set; } = string.Empty;

    public bool NeedsClarification { get; set; }
    public bool HasMoreResults { get; set; }

    public List<ChatbotFacilityResult> Facilities { get; set; } = [];

    public bool HasResults => Facilities.Count > 0;
}
