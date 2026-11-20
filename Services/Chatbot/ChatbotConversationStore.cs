
using System.Text.Json;
using TOPqwWeb_Core.Infrastructure.Session;
using TOPqwWeb_Core.Models.Chatbot;

namespace TOPqwWeb_Core.Services.Chatbot;

public class ChatbotConversationStore
{
    private const int ConversationTimeoutMinutes = 20;

    private readonly IHttpContextAccessor _httpContextAccessor;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public ChatbotConversationStore(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ChatbotConversationState GetState()
    {
        var session = GetSession();

        var json = session.GetString(
            SessionKeys.ChatbotConversationState);

        if (!string.IsNullOrWhiteSpace(json))
        {
            var state = JsonSerializer.Deserialize<ChatbotConversationState>(
                json, JsonOptions);

            if (state != null &&
                DateTime.UtcNow - state.LastActivityUtc <
                TimeSpan.FromMinutes(ConversationTimeoutMinutes))
            {
                return state;
            }
        }

        return new ChatbotConversationState();
    }

    public void SaveState(ChatbotConversationState state)
    {
        state.LastActivityUtc = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(state, JsonOptions);

        GetSession().SetString(
            SessionKeys.ChatbotConversationState, json);
    }

    public void Reset()
    {
        GetSession().Remove(SessionKeys.ChatbotConversationState);
    }

    private ISession GetSession()
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "No active HTTP context is available.");

        return context.Session;
    }
}
