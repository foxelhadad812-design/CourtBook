using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class LoginModel : PageModel
{
    private readonly ApiClient _api;
    public LoginModel(ApiClient api) => _api = api;

    [BindProperty] public LoginRequest Input { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var response = await _api.Client.PostAsJsonAsync("/api/auth/login", Input);
        if (response.IsSuccessStatusCode)
        {
            var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (result != null)
            {
                HttpContext.Session.SetString("JwtToken", result.Token);
                HttpContext.Session.SetString("UserRole", result.Role);
                // In a real app we'd decode JWT for Name. For now we just query or set a dummy
                HttpContext.Session.SetString("UserName", Input.Email);
                
                if (result.Role == "Owner") return RedirectToPage("/Dashboard/Index");
                return RedirectToPage("/Venues/Index");
            }
        }
        
        ErrorMessage = "Invalid email or password.";
        return Page();
    }
}
