
using TOPqwWeb_Core.Models.Chatbot;

namespace TOPqwWeb_Core.Services.Chatbot;

public interface IQuestionUnderstandingService
{
    Task<ChatbotIntent> UnderstandAsync(
        string question,
        ChatbotConversationState? conversationState,
        CancellationToken cancellationToken);
}
