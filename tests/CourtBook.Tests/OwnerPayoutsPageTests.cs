using System.Net;
using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;
using CourtBook.Web.Pages.Owner;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Moq;
using Xunit;

namespace CourtBook.Tests;

public class OwnerPayoutsPageTests
{
    private class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> HandlerFunc { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(HandlerFunc(request));
        }
    }

    private (PayoutsModel model, MockHttpMessageHandler handler, TestSession session) CreateModel(string? role = "Owner")
    {
        var session = new TestSession();
        if (role != null)
        {
            session.SetString("UserRole", role);
        }

        var httpContext = new DefaultHttpContext { Session = session };
        var modelState = new ModelStateDictionary();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor(), modelState);
        var modelMetadataProvider = new EmptyModelMetadataProvider();
        var viewData = new ViewDataDictionary(modelMetadataProvider, modelState);
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        var pageContext = new PageContext(actionContext)
        {
            ViewData = viewData
        };

        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var apiClient = new ApiClient(httpClient);

        var pageModel = new PayoutsModel(apiClient)
        {
            PageContext = pageContext,
            TempData = tempData
        };

        return (pageModel, handler, session);
    }

    [Fact]
    public async Task OnGetAsync_RedirectsToLogin_WhenUnauthenticated()
    {
        var (model, _, _) = CreateModel(role: null);

        var result = await model.OnGetAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Login", redirect.PageName);
        Assert.Equal("/Owner/Payouts", redirect.RouteValues?["returnUrl"]);
    }

    [Fact]
    public async Task OnGetAsync_SetsForbidden_WhenUserIsNotOwner()
    {
        var (model, _, _) = CreateModel(role: "Client");

        var result = await model.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.True(model.IsForbidden);
    }

    [Fact]
    public async Task OnGetAsync_LoadsBalanceAndPayoutMethodsAndPagedPayouts_WhenOwner()
    {
        var (model, handler, _) = CreateModel(role: "Owner");

        var ownerId = Guid.NewGuid();
        var testBalance = new OwnerBalanceDto
        {
            OwnerId = ownerId,
            PendingBalance = 1500m,
            AvailableBalance = 2500m,
            InFlightBalance = 500m,
            TotalPaidOut = 10000m,
            OutstandingDeficit = 0m
        };

        var testMethods = new List<PayoutMethodDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OwnerId = ownerId,
                Type = "InstaPay",
                AccountHolderName = "Ahmed Ali",
                MaskedInstaPayAddress = "owner@instapay",
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            }
        };

        var testPayouts = PagedResult<PayoutRequestDto>.From(
            new List<PayoutRequestDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    OwnerId = ownerId,
                    PayoutMethodId = testMethods[0].Id,
                    PayoutMethodType = "InstaPay",
                    DestinationSummary = "InstaPay - owner@instapay",
                    Amount = 1000m,
                    Status = "Submitted",
                    SubmittedAt = DateTime.UtcNow
                }
            },
            totalCount: 1,
            page: 1,
            pageSize: 10
        );

        handler.HandlerFunc = req =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/owner/balance")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(testBalance)
                };
            }
            if (req.RequestUri?.AbsolutePath == "/api/owner/payout-methods")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(testMethods)
                };
            }
            if (req.RequestUri?.AbsolutePath == "/api/owner/payouts")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(testPayouts)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(model.IsForbidden);
        Assert.NotNull(model.Balance);
        Assert.Equal(2500m, model.Balance.AvailableBalance);
        Assert.Single(model.PayoutMethods);
        Assert.Equal("owner@instapay", model.PayoutMethods[0].MaskedInstaPayAddress);
        Assert.Single(model.PagedPayouts.Items);
        Assert.Equal(1000m, model.PagedPayouts.Items[0].Amount);
    }

    [Fact]
    public async Task OnPostAddMethodAsync_RedirectsWithSuccess_WhenApiSucceeds()
    {
        var (model, handler, _) = CreateModel(role: "Owner");

        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == "/api/owner/payout-methods")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var request = new CreatePayoutMethodRequest
        {
            Type = "BankTransfer",
            Iban = "EG12345678901234567890123456789",
            AccountHolderName = "Ahmed Ali",
            BankName = "National Bank of Egypt"
        };

        var result = await model.OnPostAddMethodAsync(request);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Payout destination method registered successfully.", model.SuccessMessage);
    }

    [Fact]
    public async Task OnPostAddMethodAsync_SetsErrorMessage_WhenApiFails()
    {
        var (model, handler, _) = CreateModel(role: "Owner");

        handler.HandlerFunc = req =>
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { error = "Invalid Egyptian IBAN checksum" })
            };
        };

        var request = new CreatePayoutMethodRequest
        {
            Type = "BankTransfer",
            Iban = "EG000000"
        };

        var result = await model.OnPostAddMethodAsync(request);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Invalid Egyptian IBAN checksum", model.ErrorMessage);
    }

    [Fact]
    public async Task OnPostDeleteMethodAsync_CallsDeleteEndpoint_AndSetsMessage()
    {
        var (model, handler, _) = CreateModel(role: "Owner");
        var methodId = Guid.NewGuid();

        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Delete && req.RequestUri?.AbsolutePath == $"/api/owner/payout-methods/{methodId}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnPostDeleteMethodAsync(methodId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Payout method deactivated.", model.SuccessMessage);
    }

    [Fact]
    public async Task OnPostSetDefaultMethodAsync_CallsDefaultEndpoint_AndSetsMessage()
    {
        var (model, handler, _) = CreateModel(role: "Owner");
        var methodId = Guid.NewGuid();

        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == $"/api/owner/payout-methods/{methodId}/default")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnPostSetDefaultMethodAsync(methodId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Default payout method updated.", model.SuccessMessage);
    }

    [Fact]
    public async Task OnPostRequestPayoutAsync_GeneratesIdempotencyKey_AndSubmitsRequest()
    {
        var (model, handler, _) = CreateModel(role: "Owner");
        var methodId = Guid.NewGuid();

        HttpRequestMessage? capturedRequest = null;
        handler.HandlerFunc = req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var payoutReq = new CreatePayoutRequest
        {
            PayoutMethodId = methodId,
            Amount = 750m
        };

        var result = await model.OnPostRequestPayoutAsync(payoutReq);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("EGP 750.00", model.SuccessMessage);
        Assert.False(string.IsNullOrWhiteSpace(payoutReq.IdempotencyKey));
        Assert.NotNull(capturedRequest);
    }

    [Fact]
    public async Task OnPostCancelPayoutAsync_CallsCancelEndpoint_AndSetsMessage()
    {
        var (model, handler, _) = CreateModel(role: "Owner");
        var payoutId = Guid.NewGuid();

        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == $"/api/owner/payouts/{payoutId}/cancel")
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnPostCancelPayoutAsync(payoutId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Payout request cancelled and funds returned to Available Balance.", model.SuccessMessage);
    }
}
