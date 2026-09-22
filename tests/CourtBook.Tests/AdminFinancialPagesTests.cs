using System.Net;
using System.Net.Http.Json;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Web.Pages.Admin;
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

public class AdminFinancialPagesTests
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

    private (TModel model, MockHttpMessageHandler handler, TestSession session) CreateModel<TModel>(
        Func<ApiClient, TModel> factory,
        string? role = "Admin",
        Guid? userId = null) where TModel : PageModel
    {
        var session = new TestSession();
        if (role != null)
        {
            session.SetString("UserRole", role);
        }
        if (userId.HasValue)
        {
            session.SetString("UserId", userId.Value.ToString());
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

        var model = factory(apiClient);
        model.PageContext = pageContext;
        model.TempData = tempData;

        return (model, handler, session);
    }

    // ==========================================
    // 1. SETTLEMENTS PAGE TESTS
    // ==========================================

    [Fact]
    public async Task Settlements_OnGetAsync_RedirectsToLogin_WhenUnauthenticated()
    {
        var (model, _, _) = CreateModel(api => new SettlementsModel(api), role: null);

        var result = await model.OnGetAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Login", redirect.PageName);
        Assert.Equal("/Admin/Settlements", redirect.RouteValues?["returnUrl"]);
    }

    [Fact]
    public async Task Settlements_OnGetAsync_RedirectsToAccessDenied_WhenNotAdmin()
    {
        var (model, _, _) = CreateModel(api => new SettlementsModel(api), role: "Owner");

        var result = await model.OnGetAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/AccessDenied", redirect.PageName);
    }

    [Fact]
    public async Task Settlements_OnGetAsync_LoadsSummaryAndBatches_WhenAdmin()
    {
        var (model, handler, _) = CreateModel(api => new SettlementsModel(api), role: "Admin");

        var testSummary = new SettlementSummaryDto
        {
            TotalSettledAmount = 50000m,
            TotalSettledBatches = 5,
            TotalSettledItems = 120,
            LastSettlementDate = DateTime.UtcNow
        };

        var testBatches = PagedResult<SettlementBatchDto>.From(
            new List<SettlementBatchDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    BatchReference = "SET-BATCH-001",
                    TotalGross = 1000m,
                    TotalCommission = 50m,
                    TotalNet = 950m,
                    ItemCount = 2,
                    Status = "Completed",
                    CreatedAt = DateTime.UtcNow
                }
            },
            totalCount: 1,
            page: 1,
            pageSize: 15
        );

        handler.HandlerFunc = req =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/admin/settlements/summary")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(testSummary) };

            if (req.RequestUri?.AbsolutePath == "/api/admin/settlements")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(testBatches) };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Summary);
        Assert.Equal(50000m, model.Summary.TotalSettledAmount);
        Assert.Single(model.PagedBatches.Items);
        Assert.Equal("SET-BATCH-001", model.PagedBatches.Items[0].BatchReference);
    }

    [Fact]
    public async Task Settlements_OnGetAsync_LoadsInspectedBatch_WhenInspectBatchIdProvided()
    {
        var batchId = Guid.NewGuid();
        var (model, handler, _) = CreateModel(api => new SettlementsModel(api), role: "Admin");
        model.InspectBatchId = batchId;

        var inspectedBatch = new SettlementBatchDto
        {
            Id = batchId,
            BatchReference = "SET-INSPECT-01",
            TotalNet = 1900m,
            Items = new List<SettlementItemDto>
            {
                new() { Id = Guid.NewGuid(), BookingReference = "BK-101", NetAmount = 950m, OwnerName = "Owner A" },
                new() { Id = Guid.NewGuid(), BookingReference = "BK-102", NetAmount = 950m, OwnerName = "Owner B" }
            }
        };

        handler.HandlerFunc = req =>
        {
            if (req.RequestUri?.AbsolutePath == $"/api/admin/settlements/{batchId}")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(inspectedBatch) };

            if (req.RequestUri?.AbsolutePath == "/api/admin/settlements/summary")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new SettlementSummaryDto()) };

            if (req.RequestUri?.AbsolutePath == "/api/admin/settlements")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(PagedResult<SettlementBatchDto>.Empty()) };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.InspectedBatch);
        Assert.Equal("SET-INSPECT-01", model.InspectedBatch.BatchReference);
        Assert.Equal(2, model.InspectedBatch.Items.Count);
    }

    [Fact]
    public async Task Settlements_OnPostTriggerAsync_CallsRunEndpoint_AndSetsSuccessMessage()
    {
        var (model, handler, _) = CreateModel(api => new SettlementsModel(api), role: "Admin");

        var createdBatch = new SettlementBatchDto
        {
            Id = Guid.NewGuid(),
            BatchReference = "SET-AUTO-123",
            ItemCount = 4,
            TotalNet = 3800m
        };

        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == "/api/admin/settlements/run")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(createdBatch) };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnPostTriggerAsync(bufferHours: 12);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("SET-AUTO-123", model.SuccessMessage);
        Assert.Contains("3800.00", model.SuccessMessage);
    }

    // ==========================================
    // 2. PAYOUTS PAGE TESTS
    // ==========================================

    [Fact]
    public async Task Payouts_OnGetAsync_RedirectsToLogin_WhenUnauthenticated()
    {
        var (model, _, _) = CreateModel(api => new PayoutsModel(api), role: null);

        var result = await model.OnGetAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Login", redirect.PageName);
        Assert.Equal("/Admin/Payouts", redirect.RouteValues?["returnUrl"]);
    }

    [Fact]
    public async Task Payouts_OnGetAsync_LoadsSummaryAndPagedPayouts_WhenAdmin()
    {
        var adminId = Guid.NewGuid();
        var (model, handler, _) = CreateModel(api => new PayoutsModel(api), role: "Admin", userId: adminId);

        var testSummary = new AdminPayoutSummaryDto
        {
            PendingReviewCount = 3,
            TotalPendingReviewAmount = 7500m,
            ApprovedCount = 1,
            TotalApprovedAmount = 2500m,
            PaidCount = 10,
            TotalPaidAmount = 25000m
        };

        var testPayouts = PagedResult<PayoutRequestDto>.From(
            new List<PayoutRequestDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    PayoutReference = "PO-2026-001",
                    OwnerId = Guid.NewGuid(),
                    OwnerName = "Venue Owner 1",
                    Amount = 2500m,
                    NetAmount = 2500m,
                    Status = "Submitted",
                    SubmittedAt = DateTime.UtcNow
                }
            },
            totalCount: 1,
            page: 1,
            pageSize: 15
        );

        handler.HandlerFunc = req =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/admin/payouts/summary")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(testSummary) };

            if (req.RequestUri?.AbsolutePath == "/api/admin/payouts")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(testPayouts) };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(adminId, model.CurrentAdminId);
        Assert.NotNull(model.Summary);
        Assert.Equal(3, model.Summary.PendingReviewCount);
        Assert.Single(model.PagedPayouts.Items);
    }

    [Fact]
    public async Task Payouts_OnPostApproveAsync_BlocksSelfApproval_WhenOwnerMatchesAdmin()
    {
        var adminId = Guid.NewGuid();
        var (model, _, _) = CreateModel(api => new PayoutsModel(api), role: "Admin", userId: adminId);

        var payoutId = Guid.NewGuid();

        // Admin attempts to approve payout where OwnerId == adminId (Self-Approval)
        var result = await model.OnPostApproveAsync(payoutId, ownerId: adminId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Contains("Anti-self-dealing protection", model.ErrorMessage);
    }

    [Fact]
    public async Task Payouts_OnPostApproveAsync_CallsApproveEndpoint_WhenLegitimate()
    {
        var adminId = Guid.NewGuid();
        var legitimateOwnerId = Guid.NewGuid();
        var (model, handler, _) = CreateModel(api => new PayoutsModel(api), role: "Admin", userId: adminId);

        var payoutId = Guid.NewGuid();

        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == $"/api/admin/payouts/{payoutId}/approve")
                return new HttpResponseMessage(HttpStatusCode.OK);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnPostApproveAsync(payoutId, ownerId: legitimateOwnerId);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Payout request approved successfully. It is now queued for manual disbursement.", model.SuccessMessage);
    }

    [Fact]
    public async Task Payouts_OnPostRejectAsync_RequiresReason_AndCallsRejectEndpoint()
    {
        var (model, handler, _) = CreateModel(api => new PayoutsModel(api), role: "Admin");
        var payoutId = Guid.NewGuid();

        // 1. Validation check for empty reason
        var resEmpty = await model.OnPostRejectAsync(payoutId, new RejectPayoutRequest { Reason = "" });
        Assert.IsType<RedirectToPageResult>(resEmpty);
        Assert.Equal("Rejection reason is required.", model.ErrorMessage);

        // 2. Successful rejection with reason
        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == $"/api/admin/payouts/{payoutId}/reject")
                return new HttpResponseMessage(HttpStatusCode.OK);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var resValid = await model.OnPostRejectAsync(payoutId, new RejectPayoutRequest { Reason = "Invalid bank account" });
        Assert.IsType<RedirectToPageResult>(resValid);
        Assert.Contains("rejected", model.SuccessMessage);
    }

    [Fact]
    public async Task Payouts_OnPostMarkPaidAsync_RequiresExternalReference_AndCallsMarkPaidEndpoint()
    {
        var (model, handler, _) = CreateModel(api => new PayoutsModel(api), role: "Admin");
        var payoutId = Guid.NewGuid();

        // 1. Validation check for missing external reference
        var resMissing = await model.OnPostMarkPaidAsync(payoutId, new MarkPayoutPaidRequest { ExternalTransactionReference = "" });
        Assert.IsType<RedirectToPageResult>(resMissing);
        Assert.Contains("mandatory", model.ErrorMessage);

        // 2. Successful mark-paid
        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == $"/api/admin/payouts/{payoutId}/mark-paid")
                return new HttpResponseMessage(HttpStatusCode.OK);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var resValid = await model.OnPostMarkPaidAsync(payoutId, new MarkPayoutPaidRequest
        {
            ExternalTransactionReference = "NBE-TX-9988123",
            DisbursementNote = "Wire sent"
        });

        Assert.IsType<RedirectToPageResult>(resValid);
        Assert.Contains("NBE-TX-9988123", model.SuccessMessage);
    }

    // ==========================================
    // 3. RECOVERY PAGE TESTS
    // ==========================================

    [Fact]
    public async Task Recovery_OnGetAsync_RedirectsToLogin_WhenUnauthenticated()
    {
        var (model, _, _) = CreateModel(api => new RecoveryModel(api), role: null);

        var result = await model.OnGetAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Login", redirect.PageName);
        Assert.Equal("/Admin/Recovery", redirect.RouteValues?["returnUrl"]);
    }

    [Fact]
    public async Task Recovery_OnGetAsync_LoadsSummaryAndPagedObligations_WhenAdmin()
    {
        var (model, handler, _) = CreateModel(api => new RecoveryModel(api), role: "Admin");

        var testSummary = new RecoverySummaryDto
        {
            TotalActiveDeficit = 1500m,
            ActiveObligationsCount = 2,
            TotalRecoveredAmount = 3000m,
            TotalWrittenOffAmount = 500m
        };

        var testObligations = PagedResult<RecoveryObligationDto>.From(
            new List<RecoveryObligationDto>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ObligationReference = "REC-OBL-001",
                    OwnerName = "Owner With Deficit",
                    BookingReference = "BK-DEF-01",
                    TotalDeficitAmount = 1000m,
                    RemainingDeficitAmount = 500m,
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                }
            },
            totalCount: 1,
            page: 1,
            pageSize: 15
        );

        handler.HandlerFunc = req =>
        {
            if (req.RequestUri?.AbsolutePath == "/api/admin/financial/recovery-obligations/summary")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(testSummary) };

            if (req.RequestUri?.AbsolutePath == "/api/admin/financial/recovery-obligations")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(testObligations) };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var result = await model.OnGetAsync();

        Assert.IsType<PageResult>(result);
        Assert.NotNull(model.Summary);
        Assert.Equal(1500m, model.Summary.TotalActiveDeficit);
        Assert.Single(model.PagedObligations.Items);
        Assert.Equal(500m, model.PagedObligations.Items[0].RemainingDeficitAmount);
    }

    [Fact]
    public async Task Recovery_OnPostSettleManuallyAsync_RequiresPositiveAmountAndRef_AndCallsEndpoint()
    {
        var (model, handler, _) = CreateModel(api => new RecoveryModel(api), role: "Admin");
        var oblId = Guid.NewGuid();

        // 1. Zero amount validation
        var resZero = await model.OnPostSettleManuallyAsync(oblId, new ManualSettleRecoveryRequest { Amount = 0, ExternalReference = "REC-1" });
        Assert.IsType<RedirectToPageResult>(resZero);
        Assert.Contains("greater than zero", model.ErrorMessage);

        // 2. Empty reference validation
        var resNoRef = await model.OnPostSettleManuallyAsync(oblId, new ManualSettleRecoveryRequest { Amount = 100, ExternalReference = "" });
        Assert.IsType<RedirectToPageResult>(resNoRef);
        Assert.Contains("reference", model.ErrorMessage);

        // 3. Valid settlement
        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == $"/api/admin/financial/recovery-obligations/{oblId}/settle-manually")
                return new HttpResponseMessage(HttpStatusCode.OK);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var resValid = await model.OnPostSettleManuallyAsync(oblId, new ManualSettleRecoveryRequest { Amount = 250m, ExternalReference = "CASH-REC-01" });
        Assert.IsType<RedirectToPageResult>(resValid);
        Assert.Contains("EGP 250.00", model.SuccessMessage);
    }

    [Fact]
    public async Task Recovery_OnPostWriteOffAsync_RequiresReason_AndCallsWriteOffEndpoint()
    {
        var (model, handler, _) = CreateModel(api => new RecoveryModel(api), role: "Admin");
        var oblId = Guid.NewGuid();

        // 1. Missing reason
        var resNoReason = await model.OnPostWriteOffAsync(oblId, new WriteOffRecoveryRequest { Reason = "" });
        Assert.IsType<RedirectToPageResult>(resNoReason);
        Assert.Contains("mandatory", model.ErrorMessage);

        // 2. Valid write-off
        handler.HandlerFunc = req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri?.AbsolutePath == $"/api/admin/financial/recovery-obligations/{oblId}/write-off")
                return new HttpResponseMessage(HttpStatusCode.OK);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        };

        var resValid = await model.OnPostWriteOffAsync(oblId, new WriteOffRecoveryRequest { Reason = "Management board waiver" });
        Assert.IsType<RedirectToPageResult>(resValid);
        Assert.Contains("written off successfully", model.SuccessMessage);
    }
}
