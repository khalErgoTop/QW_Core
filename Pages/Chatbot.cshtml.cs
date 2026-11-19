
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using TOPqwWeb_Core.Models.Chatbot;
using TOPqwWeb_Core.Services.Chatbot;

namespace TOPqwWeb_Core.Pages;

public class ChatbotModel : PageModel
{
    private readonly ChatbotService _chatbotService;

    public ChatbotModel(ChatbotService chatbotService)
    {
        _chatbotService = chatbotService;
    }

    [BindProperty]
    [Required(ErrorMessage = "Bitte geben Sie eine Frage ein.")]
    [StringLength(1000, ErrorMessage = "Die Frage darf maximal 1000 Zeichen lang sein.")]
    public string Question { get; set; } = string.Empty;

    public ChatbotResponse? Response { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        Response = await _chatbotService.ProcessAsync(
            Question,
            cancellationToken);

        return Page();
    }
}
