
namespace TOPqwWeb_Core.Models.Chatbot;

public class ChatbotConversationState
{
    public ChatbotIntent? CurrentIntent { get; set; }

    public List<string> RecentMessages { get; set; } = [];

    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
}
