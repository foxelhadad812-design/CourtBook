using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class RegisterModel : PageModel
{
    private readonly ApiClient _api;
    public RegisterModel(ApiClient api) => _api = api;

    [BindProperty] public RegisterRequest Input { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var response = await _api.Client.PostAsJsonAsync("/api/auth/register", Input);
        if (response.IsSuccessStatusCode)
        {
            return RedirectToPage("/Login");
        }
        
        ErrorMessage = await response.Content.ReadAsStringAsync();
        return Page();
    }
}
