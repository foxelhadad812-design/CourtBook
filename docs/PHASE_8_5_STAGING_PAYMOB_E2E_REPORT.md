# Phase 8.5 — Staging & Paymob Sandbox E2E Report

## 1. Executive Summary

Phase 8.5 focused strictly on auditing, hardening, and verifying the existing PlaySpot / CourtBook codebase for staging readiness, ensuring end-to-end payment lifecycle integrity, and evaluating live Paymob Sandbox capabilities.

No speculative product features were introduced. All actions were directed at verifying staging configuration invariants, sealing payment terminal state machines, preventing late webhook resurrection of failed/expired payments, confirming double-entry transaction ledgers, testing SignalR isolation, and establishing a baseline for staging deployment.

### Key Outcomes:
- **Baseline Integrity**: Build verified at 0 warnings, 0 errors, 191 / 191 automated tests passing across 28 test suites.
- **Paymob Sandbox E2E Status**: Explicitly declared as **`BLOCKED — credentials unavailable`**. In accordance with strict zero-mockery guidelines, real external HTTP calls to Paymob were not fabricated because valid merchant sandbox credentials (`PaymentGateway__Paymob__ApiKey`, etc.) are not present in the local execution environment.
- **Terminal State Security Fix**: Fixed a critical security loophole in `PaymentService.ProcessWebhookPaymentCompletedAsync` and `VerifyReturnAsync` where payments already in terminal state `PaymentStatus.Failed` could previously have been moved back to `Completed` by a late or rogue webhook delivery.
- **Staging Hardening**: Enforced that `Staging` environments—like `Production`—strictly reject `localhost`/`LocalDB` connection strings, reject known placeholder JWT secrets, enforce `CookieSecurePolicy.Always`, and require explicit `ApiSettings:BaseUrl`.

---

## 2. Baseline

- **Initial Commit**: `e6e5e8a2f9fb537e0e66e37334b7bbb0f4d4d448`
- **Branch**: `main`
- **Build Status**: Succeeded (`dotnet build --warnaserror`)
- **Warnings**: 0
- **Errors**: 0
- **Initial Tests**: 177 passing (0 failed, 0 skipped)
- **Current Tests**: 191 passing (14 new tests added in Phase 8.5)

---

## 3. Staging Configuration

The staging configuration files ([`CourtBook.API/appsettings.Staging.json`](file:///C:/Users/Mohamed/source/repos/CourtBook/src/CourtBook.API/appsettings.Staging.json) and [`CourtBook.Web/appsettings.Staging.json`](file:///C:/Users/Mohamed/source/repos/CourtBook/src/CourtBook.Web/appsettings.Staging.json)) were audited and hardened against production security rules:

| Configuration Area | Staging Setting | Enforcement / Safeguard |
| :--- | :--- | :--- |
| **Database Connection** | `Server=tcp:staging-sql.playspot.internal,1433;Initial Catalog=PlaySpotStagingDB;...` | Startup validation in `API/Program.cs` throws fatal error if `(localdb)`, `localhost`, or `127.0.0.1` is detected in Staging. |
| **JWT Key** | `OVERRIDE_VIA_ENV_VAR_OR_SECRET_MIN_32_CHARS` | Startup validation throws `InvalidOperationException` if the key is < 32 characters or matches known development/staging placeholders. |
| **CORS** | `https://staging.playspot.eg` | Strict origin matching with credentials allowed for SignalR WebSockets and cookies. |
| **Session Cookies** | `CookieSecurePolicy.Always` | Hardened in `Web/Program.cs` so Staging HTTPS always transmits secure cookies. |
| **API Base URL** | `https://staging-api.playspot.eg` | Hardened in `Web/Program.cs` to require explicit configuration; falls back to fatal error if missing in Staging. |
| **Distributed Cache** | `IDistributedCache` abstraction | Ready for optional Redis toggle via `Redis:Enabled` and `Redis:ConnectionString`. |
| **PaymentHoldWorker** | Enabled, Interval: 30s, BatchSize: 50 | Actively cleans expired payment holds on staging. |

---

## 4. Database / Migrations

- **Latest Migration**: `20260922124104_AddPaymentHoldIndex`
- **Pending Model Changes**: None (`dotnet ef migrations has-pending-model-changes` reports zero pending changes).
- **Core Tables & Entities**:
  - `Payments`: Contains primary key, unique index on `BookingId`, index on `ProviderOrderId`, and composite index on `(Status, ExpiresAt)`.
  - `TransactionLedger`: Double-entry audit table recording gross, commission, and net amounts; strictly immutable.
  - `IdempotencyLogs`: Enforces uniqueness on `(Provider, ProviderTransactionId)` preventing duplicate webhook executions.
  - `Notifications`: Stores persistent notification records with read state and action URLs.
  - `TermsDocuments` & `TermsAcceptances`: Legal compliance tracking for players and owners.
- **Index Optimization Verified**: Composite index `IX_Payments_Status_ExpiresAt` enables $O(\log n)$ index seeks for background hold sweeping.

---

## 5. Paymob Configuration

The following configuration variables are required for Paymob integration:

```json
"PaymentGateway": {
  "OnlineHoldMinutes": 10,
  "CommissionRate": "0.05",
  "Paymob": {
    "IsSandbox": "true",
    "ApiKey": "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__ApiKey",
    "IntegrationId": "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__IntegrationId",
    "IframeId": "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__IframeId",
    "HmacSecret": "OVERRIDE_VIA_ENV_VAR_PaymentGateway__Paymob__HmacSecret"
  }
}
```

- **Authentication Method**: Paymob Auth Token via API Key.
- **Order Registration**: Server-to-server POST to Paymob Ecommerce Orders.
- **Payment Key Request**: Generates iframe token scoped to amount, currency (`EGP`), and billing details.
- **HMAC Validation**: Cryptographically validated on inbound webhooks via HMAC-SHA512.
- **Documentation**: Detailed instructions, setup steps, and Egyptian test card numbers are documented in [`docs/PAYMOB_SANDBOX_SETUP.md`](file:///C:/Users/Mohamed/source/repos/CourtBook/docs/PAYMOB_SANDBOX_SETUP.md).

---

## 6. Paymob E2E Result

### Result: **`BLOCKED — credentials unavailable`**

> **Formal Declaration**:
> In accordance with strict engineering integrity and Rule 14 ("No Fake Validation"), live Paymob Sandbox End-to-End transactions were **NOT** executed because valid merchant sandbox credentials (`PaymentGateway__Paymob__ApiKey`, `PaymentGateway__Paymob__HmacSecret`, etc.) are not present in the local runtime environment.
>
> The Paymob integration has been verified statically and through automated unit/integration tests with cryptographic mocks. Live network calls to Paymob Accept endpoints are ready to execute as soon as genuine sandbox credentials are provided.

---

## 7. Payment Lifecycle Verification

The payment state machine was tested and verified across all valid and invalid transitions:

```
[Booking Initiated] 
       │
       ▼
  (Pending) ──[Initiate Online Payment]──► (Processing, ExpiresAt = +10m)
                                                     │
               ┌─────────────────────────────────────┴─────────────────────────────────────┐
               ▼ (Valid Webhook / VerifyReturn)                                            ▼ (Hold Timeout / Payment Failure)
          (Completed)                                                                  (Failed)
               │                                                                           │
         [Process Refund]                                                        [Slot Released to Pool]
               │
       ┌───────┴────────┐
       ▼                ▼
  (Refunded)   (PartiallyRefunded)
```

- **Pending $\rightarrow$ Processing**: Establishes 10-minute hold window.
- **Processing $\rightarrow$ Completed**: Validated only upon verified webhook or server-to-server gateway query. Records double-entry ledger.
- **Processing $\rightarrow$ Failed**: Executed by `PaymentHoldWorker` or gateway rejection. Cancels booking, populates cancellation timestamp and reason, releases court slot.
- **Completed $\rightarrow$ Refunded / PartiallyRefunded**: Deducts cancellation fee, reverses commission and owner net in ledger, sends notification.
- **Forbidden Transitions**: Moving from `Refunded`, `PartiallyRefunded`, `Cancelled`, or `Failed` back to `Completed` is strictly rejected.

---

## 8. Webhook Security

- **HMAC-SHA512 Verification**: Paymob webhook payloads are hashed using the shared HMAC secret and compared against the `X-Hmac-Signature` header using `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.
- **Unsigned Requests**: Rejected with HTTP 401 Unauthorized.
- **Malformed / Empty Requests**: Rejected with HTTP 400 Bad Request.
- **Terminal State Lockout**: Verified that webhooks cannot alter payments already in `Failed`, `Cancelled`, `Refunded`, or `PartiallyRefunded` states.

---

## 9. Idempotency

- **Database Guard**: `IdempotencyLog` table maintains a unique constraint on `(Provider, ProviderTransactionId)`.
- **Duplicate Webhook Delivery**: When the same transaction ID is received twice:
  - First invocation: Processes payment, writes ledger, records idempotency log.
  - Second invocation: Detects existing transaction ID, logs warning, returns HTTP 200 without executing duplicate business logic or creating duplicate ledger entries.
- **Hold Worker Sweep**: Generates system idempotency logs (`Provider = "System"`, `Action = "HoldExpired"`).

---

## 10. Hold Expiration

- **Service**: `PaymentHoldWorker` (`BackgroundService`).
- **Interval**: 60s (Production), 30s (Staging), 15s (Development).
- **Sweep Logic**: Finds payments with `Status == Processing` and `ExpiresAt <= DateTime.UtcNow`.
- **Actions Executed**:
  - Updates payment status to `Failed`.
  - Updates booking payment status to `Failed` and status to `Cancelled`.
  - Sets `CancelledAt` timestamp and detailed cancellation reason.
  - Inserts `IdempotencyLog` entry.
  - Dispatches user expiration notification via `INotificationService`.
  - Releases court slot for subsequent reservations.

---

## 11. Refunds

- **Role Authorization**: Refund endpoint (`POST /api/payments/booking/{id}/refund`) is restricted to `Admin` users.
- **Cancellation Policy Compliance**:
  - Automatically respects venue cancellation policies and fees.
  - Calculates `eligibleRefundAmount = Max(0, Amount - CancellationFee)`.
- **Zero-Value Gateway Call Prevention**:
  - If cancellation fee is 100% (eligible refund = 0), gateway refund API is NOT called.
  - The retained fee is recorded in `TransactionLedger` as `LedgerEntryType.CancellationFee`.
- **Gateway Refund Execution**:
  - For online payments with eligible refund > 0, calls `_gateway.RefundAsync`.
  - For `PayAtFacility`, reverses ledger locally without gateway round-trip.
- **Status Assignment**:
  - Full refund $\rightarrow$ `PaymentStatus.Refunded`.
  - Partial refund (cancellation fee retained) $\rightarrow$ `PaymentStatus.PartiallyRefunded`.

---

## 12. SignalR

- **Hub Route**: `/hubs/notifications`
- **Authentication**: `[Authorize]` attribute; token passed via query string `?access_token=...` during WebSocket handshake and parsed via `JwtBearerEvents.OnMessageReceived` in `CourtBook.API/Program.cs`.
- **User Isolation**:
  - Clients join group `user:{userId}` derived strictly from validated JWT claims (`Context.UserIdentifier` / `sub`).
  - No client-facing methods allow arbitrary group joining.
  - User A cannot receive notifications destined for User B.
- **Client Features**:
  - Automatic reconnection backoff `[0s, 2s, 5s, 10s, 30s]`.
  - Live unread badge count updates on the bell icon.
  - Real-time dropdown item prepending.
  - Pop-up toast alerts honoring light/dark mode and RTL Arabic layout.

---

## 13. Health / Readiness

- **`/health` (Liveness)**: Unauthenticated probe returning HTTP 200 `Healthy` when the process is alive.
- **`/health/ready` (Readiness)**: Validates that SQL Server database connectivity is operational and schema is responsive via `AddDbContextCheck<AppDbContext>()`.

---

## 14. Authorization / Security

- **Role-Based Access Control**:
  - Admin Portal (`/admin`, `/Admin/Transactions`): Enforces `Admin` role at middleware and controller levels.
  - Owner Dashboard (`/owner`, `/Owner/FinancialReport`): Enforces `Owner` or `Admin` role.
  - Player/Client: Access to booking creation and personal notifications.
- **BOLA / IDOR Protection**:
  - `GET /api/payments/verify`: Validates that `booking.UserId == currentUserId` or user is `Admin`.
  - `GET /api/payments/booking/{id}`: Enforces booking ownership before returning payment details.
  - `GET /api/payments/owner/report`: Derives owner ID directly from authenticated user claims, not query parameters.
- **Input Validation**: FluentValidation validators active on all application request DTOs with ProblemDetails format (HTTP 422).
- **Rate Limiting**: Fixed window rate limiting applied to authentication (5 req/min) and general API endpoints (120 req/min).

---

## 15. Tests

A total of **191 automated tests** are passing (100% green).

### Test Breakdown:
- **Phase 8.5 Staging & Lifecycle Tests**: 14 tests in [`Phase85StagingAndLifecycleVerificationTests.cs`](file:///C:/Users/Mohamed/source/repos/CourtBook/tests/CourtBook.Tests/Phase85StagingAndLifecycleVerificationTests.cs)
- **Phase 8 Production & Realtime Tests**: 7 tests in [`Phase8ProductionAndRealtimeTests.cs`](file:///C:/Users/Mohamed/source/repos/CourtBook/tests/CourtBook.Tests/Phase8ProductionAndRealtimeTests.cs)
- **Phase 7.1 Security & Integrity Tests**: 21 tests in [`PaymentSecurityAndIntegrityTests.cs`](file:///C:/Users/Mohamed/source/repos/CourtBook/tests/CourtBook.Tests/PaymentSecurityAndIntegrityTests.cs)
- **Phase 7 Payment Gateway Tests**: 10 tests in [`PaymentGatewayTests.cs`](file:///C:/Users/Mohamed/source/repos/CourtBook/tests/CourtBook.Tests/PaymentGatewayTests.cs)
- **Production Configuration Tests**: 6 tests in [`ProductionConfigurationTests.cs`](file:///C:/Users/Mohamed/source/repos/CourtBook/tests/CourtBook.Tests/ProductionConfigurationTests.cs)
- **Core Domain & Feature Tests**: 133 tests across availability, booking concurrency, reviews, age-aware matching, admin portal, and timezone handling.

---

## 16. Issues Found

### Issue 1: Terminal State Loophole in Webhook & Return Verification
- **Severity**: HIGH
- **Description**: If a payment was already marked `PaymentStatus.Failed` (e.g., by the background hold worker after 10 minutes), a delayed webhook payload or browser return verification could have transitioned the payment to `Completed`.
- **Root Cause**: Line 255 of `PaymentService.cs` checked for `Refunded`, `PartiallyRefunded`, and `Cancelled`, but omitted `Failed`.
- **Fix**: Added `payment.Status == PaymentStatus.Failed` to the invalid transitions guard in `ProcessWebhookPaymentCompletedAsync` and added a pre-gateway terminal state guard in `VerifyReturnAsync`.
- **Verification**: Verified via 8 new parameterized unit tests in `Phase85StagingAndLifecycleVerificationTests.cs`.

### Issue 2: Staging Environment Allowed LocalDB & Placeholder Secrets
- **Severity**: MEDIUM
- **Description**: Startup validation in `API/Program.cs` and `Web/Program.cs` only checked `builder.Environment.IsProduction()`, allowing a staging instance to accidentally launch with local connection strings or placeholder JWT keys.
- **Root Cause**: Checks were scoped exclusively to `IsProduction()`.
- **Fix**: Expanded checks to `builder.Environment.IsProduction() || builder.Environment.IsStaging()`, added staging placeholder keys to the rejection list, and enforced `CookieSecurePolicy.Always` in Staging.
- **Verification**: Verified via `ProductionConfigurationTests` and `Phase85StagingAndLifecycleVerificationTests`.

---

## 17. Remaining Limitations

1. **Paymob Sandbox Live E2E**: Blocked until live merchant sandbox credentials are configured in environment variables.
2. **External Staging Database**: Deployment to a remote cloud SQL Server instance requires infrastructure provisioning and CI/CD secret injection.
3. **Redis Distributed Cache**: Memory cache is active by default; enabling distributed clustering requires deploying a Redis instance and configuring `Redis:Enabled = true`.

---

## 18. Production Deployment Checklist

- [ ] Set `ASPNETCORE_ENVIRONMENT` = `Production` (or `Staging`).
- [ ] Configure `ConnectionStrings__DefaultConnection` with production SQL Server connection string (with `Encrypt=True;TrustServerCertificate=False`).
- [ ] Configure `JwtSettings__Key` with a cryptographically secure 256-bit secret string (min 32 characters).
- [ ] Set `ADMIN_INITIAL_PASSWORD` and `ADMIN_EMAIL` in environment to control bootstrap admin credentials.
- [ ] Inject Paymob credentials via environment:
  - `PaymentGateway__Paymob__ApiKey`
  - `PaymentGateway__Paymob__IntegrationId`
  - `PaymentGateway__Paymob__IframeId`
  - `PaymentGateway__Paymob__HmacSecret`
- [ ] Configure Paymob Webhook URL in Paymob Dashboard to `https://<DOMAIN>/api/payments/webhook`.
- [ ] Ensure Redis connection string is set if running multiple web server instances.
- [ ] Verify SSL/TLS certificates are active for both API and Web domains.

---

## 19. Phase 9 Readiness

The system is in a pristine, hardened state. All database models, payment state machines, background sweepers, and real-time communication channels are locked down and thoroughly tested.

The codebase is fully primed to proceed to **Phase 9: Mobile API & Native Client Optimization / Marketplace Social & Matchmaking** once staging deployment and live Paymob credentials are confirmed.
