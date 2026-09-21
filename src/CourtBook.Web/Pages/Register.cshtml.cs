using CourtBook.Web.Models;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CourtBook.Web.Pages;

public class RegisterModel : PageModel
{
    private readonly ApiClient _api;
    private readonly ITextLocalizer _loc;

    public RegisterModel(ApiClient api, ITextLocalizer loc)
    {
        _api = api;
        _loc = loc;
    }

    [BindProperty] public RegisterRequest Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Input.AcceptTerms)
        {
            ErrorMessage = _loc["Auth.Register.TermsMustAccept"];
            return Page();
        }

        if (string.Equals(Input.Role, "Client", StringComparison.OrdinalIgnoreCase) && !Input.DateOfBirth.HasValue)
        {
            ErrorMessage = _loc.IsArabic 
                ? "تاريخ الميلاد مطلوب لحسابات اللاعبين للمطابقة العمرية وضمان السلامة."
                : "Date of birth is required for player accounts to ensure age-appropriate games.";
            return Page();
        }

        Input.TermsVersion = "1.0";
        var response = await _api.Client.PostAsJsonAsync("/api/auth/register", Input);
        if (response.IsSuccessStatusCode)
        {
            TempData["SuccessMessage"] = _loc.IsArabic 
                ? "أهلاً بك في بلاي سبوت! تم إنشاء حسابك بنجاح، يمكنك تسجيل الدخول الآن."
                : "Welcome to PlaySpot! Your account was created successfully. Please log in.";
            return RedirectToPage("/Login", new { returnUrl = ReturnUrl });
        }
        
        var errorContent = await response.Content.ReadAsStringAsync();
        ErrorMessage = !string.IsNullOrWhiteSpace(errorContent) ? errorContent.Trim('"') : "Registration failed. Please check your inputs.";
        return Page();
    }
}
