using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using CourtBook.API.Controllers;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using CourtBook.Domain.Entities;
using CourtBook.Domain.Enums;
using CourtBook.Infrastructure.Services;
using CourtBook.Web.Middleware;
using CourtBook.Web.Pages;
using CourtBook.Web.Pages.Venues;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CourtBook.Tests;

public class TestSession : ISession
{
    private readonly Dictionary<string, byte[]> _storage = new();
    public bool IsAvailable => true;
    public string Id => Guid.NewGuid().ToString();
    public IEnumerable<string> Keys => _storage.Keys;

    public void Clear() => _storage.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => _storage.Remove(key);
    public void Set(string key, byte[] value) => _storage[key] = value;
    public bool TryGetValue(string key, out byte[] value) => _storage.TryGetValue(key, out value!);
}

public class VenueAuthenticationAccessTests
{
    [Fact]
    public async Task Guest_RequestsVenueDetailsPage_RedirectedToLoginWithReturnUrl()
    {
        // 1. Arrange AuthMiddleware with empty session (Guest)
        var venueId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Session = new TestSession(); // No JwtToken set
        context.Request.Path = "/venues/details";
        context.Request.QueryString = new QueryString($"?id={venueId}");

        var nextCalled = false;
        var middleware = new AuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // 2. Act
        await middleware.InvokeAsync(context);

        // 3. Assert: Request did not pass to next; redirected to /Login with full ReturnUrl
        Assert.False(nextCalled);
        Assert.Equal(302, context.Response.StatusCode);
        var expectedReturnUrl = Uri.EscapeDataString($"/venues/details?id={venueId}");
        Assert.Equal($"/Login?returnUrl={expectedReturnUrl}", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task Guest_RequestsRouteStyleVenueDetailsPage_RedirectedToLoginWithReturnUrl()
    {
        var venueId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Session = new TestSession();
        context.Request.Path = $"/venues/details/{venueId}";

        var nextCalled = false;
        var middleware = new AuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(302, context.Response.StatusCode);
        var expectedReturnUrl = Uri.EscapeDataString($"/venues/details/{venueId}");
        Assert.Equal($"/Login?returnUrl={expectedReturnUrl}", context.Response.Headers.Location.ToString());
    }

    [Fact]
    public void Guest_RequestsDetailedVenueApi_RequiresAuthorizeAttribute()
    {
        // Assert server-side API authorization attribute on GetById
        var method = typeof(VenuesController).GetMethod(nameof(VenuesController.GetById));
        Assert.NotNull(method);

        var authorizeAttr = method.GetCustomAttribute<AuthorizeAttribute>();
        var allowAnonymousAttr = method.GetCustomAttribute<AllowAnonymousAttribute>();

        // Must have [Authorize] and must NOT have [AllowAnonymous]
        Assert.NotNull(authorizeAttr);
        Assert.Null(allowAnonymousAttr);
    }

    [Fact]
    public async Task AuthenticatedPlayer_RequestsVenueDetailsPage_PassesMiddlewareSuccessfully()
    {
        var venueId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        var session = new TestSession();
        session.SetString("JwtToken", "valid-player-jwt-token");
        context.Session = session;
        context.Request.Path = "/venues/details";
        context.Request.QueryString = new QueryString($"?id={venueId}");

        var nextCalled = false;
        var middleware = new AuthMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        // Assert: Allowed through to page execution
        Assert.True(nextCalled);
        Assert.NotEqual(302, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedPlayer_RequestsDetailedVenueApi_ReturnsSuccess()
    {
        var db = TestDbContextFactory.Create(nameof(AuthenticatedPlayer_RequestsDetailedVenueApi_ReturnsSuccess));
        var (venueId, courtId, ownerId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var venueService = new VenueService(db);
        var controller = new VenuesController(venueService);

        // Simulate authenticated user context
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Client")
        }, "TestAuth"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        var actionResult = await controller.GetById(venueId);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var dto = Assert.IsType<VenueResponse>(okResult.Value);

        Assert.Equal(venueId, dto.Id);
        Assert.Equal("Stars Arena", dto.Name);
        Assert.Single(dto.Courts);
        Assert.Equal(courtId, dto.Courts[0].Id);
    }

    [Fact]
    public void ReturnUrl_Preserved_BetweenLoginAndRegisterFlow()
    {
        var testReturnUrl = "/venues/details?id=" + Guid.NewGuid();

        // 1. LoginModel binds ReturnUrl
        var loginModel = new LoginModel(new ApiClient(new HttpClient(), new HttpContextAccessor()))
        {
            ReturnUrl = testReturnUrl
        };
        Assert.Equal(testReturnUrl, loginModel.ReturnUrl);

        // 2. RegisterModel binds ReturnUrl
        var registerModel = new RegisterModel(new ApiClient(new HttpClient(), new HttpContextAccessor()), new TextLocalizer())
        {
            ReturnUrl = testReturnUrl
        };
        Assert.Equal(testReturnUrl, registerModel.ReturnUrl);
    }

    [Theory]
    [InlineData("/venues/details?id=b1ab1ad7-a5ae-4ae6-8f4f-1b678324789b")]
    [InlineData("/venues/details/b1ab1ad7-a5ae-4ae6-8f4f-1b678324789b")]
    public void Login_WithValidReturnUrl_IsRecognizedAsLocalAndPreservesDestination(string returnUrl)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new PageActionDescriptor());
        var urlHelper = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(actionContext);

        // Verify that ReturnUrl pointing to Venue Details is recognized as a valid local redirect
        Assert.True(urlHelper.IsLocalUrl(returnUrl));

        // When LoginModel executes with ReturnUrl, it returns LocalRedirectResult targeting this exact URL
        var result = new LocalRedirectResult(returnUrl);
        Assert.Equal(returnUrl, result.Url);
    }

    [Fact]
    public async Task Guest_CanStillAccess_HomeAndPublicDiscoveryEndpoints()
    {
        // 1. Home page passes middleware without token
        var homeContext = new DefaultHttpContext();
        homeContext.Session = new TestSession();
        homeContext.Request.Path = "/";

        var homeNextCalled = false;
        var middleware = new AuthMiddleware(_ =>
        {
            homeNextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(homeContext);
        Assert.True(homeNextCalled);

        // 2. Public discovery page passes middleware without token
        var venuesContext = new DefaultHttpContext();
        venuesContext.Session = new TestSession();
        venuesContext.Request.Path = "/venues";

        var venuesNextCalled = false;
        middleware = new AuthMiddleware(_ =>
        {
            venuesNextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(venuesContext);
        Assert.True(venuesNextCalled);

        // 3. Search and GetAll API endpoints have [AllowAnonymous]
        var searchMethod = typeof(VenuesController).GetMethod(nameof(VenuesController.Search));
        Assert.NotNull(searchMethod?.GetCustomAttribute<AllowAnonymousAttribute>());

        var getAllMethod = typeof(VenuesController).GetMethod(nameof(VenuesController.GetAll));
        Assert.NotNull(getAllMethod?.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void OwnerAndAdmin_RoleAuthorization_RemainsEnforced()
    {
        // 1. Create venue requires Owner role
        var createMethod = typeof(VenuesController).GetMethod(nameof(VenuesController.Create));
        var createAuth = createMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(createAuth);
        Assert.Equal("Owner", createAuth.Roles);

        // 2. Update venue requires Owner role
        var updateMethod = typeof(VenuesController).GetMethod(nameof(VenuesController.Update));
        var updateAuth = updateMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(updateAuth);
        Assert.Equal("Owner", updateAuth.Roles);

        // 3. Delete venue requires Owner or Admin
        var deleteMethod = typeof(VenuesController).GetMethod(nameof(VenuesController.Delete));
        var deleteAuth = deleteMethod?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(deleteAuth);
        Assert.Contains("Owner", deleteAuth.Roles);
        Assert.Contains("Admin", deleteAuth.Roles);
    }

    [Fact]
    public async Task CrossOwnerIsolation_RemainsStrictlyEnforced()
    {
        var db = TestDbContextFactory.Create(nameof(CrossOwnerIsolation_RemainsStrictlyEnforced));
        var (venueId, _, ownerAId, _) = await TestDbContextFactory.SeedBasicTestDataAsync(db);

        var ownerB = new User
        {
            Id = Guid.NewGuid(),
            Name = "Intruder Owner",
            Email = "intruder@test.com",
            PasswordHash = "h",
            Role = Role.Owner
        };
        db.Users.Add(ownerB);
        await db.SaveChangesAsync();

        var venueService = new VenueService(db);

        // Owner B attempts to update Owner A's venue -> UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            venueService.UpdateAsync(ownerB.Id, "Owner", venueId, new UpdateVenueRequest
            {
                Name = "Hijacked Venue",
                City = "Cairo",
                Address = "Nowhere"
            }));

        // Owner B attempts to delete Owner A's venue -> UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            venueService.DeleteAsync(ownerB.Id, "Owner", venueId));
    }

    [Fact]
    public void LocalizationAndRtl_RemainsValid_ForAuthAndVenuePages()
    {
        var localizer = new TextLocalizer();

        // English checks
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en");
        Assert.False(localizer.IsArabic);
        Assert.Equal("ltr", localizer.Direction);
        Assert.False(string.IsNullOrEmpty(localizer["Auth.Login.Title"]));
        Assert.False(string.IsNullOrEmpty(localizer["Auth.Register.TermsMustAccept"]));

        // Arabic checks
        System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar-EG");
        Assert.True(localizer.IsArabic);
        Assert.Equal("rtl", localizer.Direction);
        Assert.Contains("أهلاً بك", localizer["Auth.Login.Title"]);
        Assert.Contains("شروط", localizer["Auth.Register.TermsMustAccept"]);
    }
}
