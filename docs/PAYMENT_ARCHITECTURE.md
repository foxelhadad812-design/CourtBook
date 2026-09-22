# PlaySpot / CourtBook — Payment Architecture & Financial Transactions

## 1. Overview
Phase 7 introduces the enterprise-grade financial core for PlaySpot, enabling seamless multi-method payments, rigorous double-spend and replay protection, and double-entry transaction ledgers with platform commission splitting.

```
+------------------------------------------------------------------------------------+
|                                    PLAYSPOT WEB                                    |
|   /Payments/Pay   <-->   /Payments/Success   <-->   /Payments/Failed / Cancelled   |
+------------------------------------------------------------------------------------+
                                      |
                                      v
+------------------------------------------------------------------------------------+
|                                   PLAYSPOT API                                     |
|  POST /api/payments/initiate  |  POST /api/payments/webhook  |  GET /verify       |
+------------------------------------------------------------------------------------+
        |                                      |                                |
        v                                      v                                v
+----------------------+             +--------------------+            +-------------------+
|  IPaymentService     |             | Webhook Engine     |            | Return Verifier   |
|  - Server-calculated |             | - HMAC-SHA512 auth |            | - Never trusts    |
|    price integrity   |             | - IdempotencyLog   |            |   browser alone   |
|  - 10-min hold       |             | - Atomic Ledger    |            | - Re-queries gw   |
+----------------------+             +--------------------+            +-------------------+
        |                                      |                                |
        +-----------------------+--------------+--------------------------------+
                                |
                                v
                +-------------------------------+
                |    IPaymentGatewayService     |
                |  (PaymobGatewayService / etc) |
                +-------------------------------+
```

---

## 2. Core Architectural Pillars

### A. Server-Side Price & Amount Integrity
- Payable amounts are calculated exclusively server-side from authoritative court pricing rules, operating schedules, and durations.
- Client-supplied prices are strictly ignored and never accepted.

### B. Gateway Abstraction (`IPaymentGatewayService`)
- Application code depends strictly on `IPaymentGatewayService` and vendor-neutral DTOs.
- `PaymobGatewayService` implements Paymob (Egypt's leading payment aggregator) supporting Credit/Debit cards, Digital Wallets, and Fawry.
- Provider secrets are loaded via environment variables (`PaymentGateway:Paymob:*`) and never hardcoded in source control.

### C. Webhook Engine & Replay Protection
- `POST /api/payments/webhook` runs anonymously (external gateway callbacks cannot supply user JWTs).
- HMAC-SHA512 signature validation ensures incoming payloads originate authentically from the provider.
- `IdempotencyLog` table enforces unique constraints on `(Provider, ProviderTransactionId)`. Duplicate webhook deliveries are safely acknowledged with HTTP 200 without executing duplicate business logic.

### D. Booking Lifecycle & 10-Minute Hold
- Initiating an online payment transitions the payment status to `Processing` with a strict 10-minute hold expiration (`ExpiresAt`).
- Bookings cannot be double-paid: once marked `Completed`, re-initiation is strictly blocked.
- Cancelled bookings cannot be paid.

### E. Double-Entry Financial Ledger (`TransactionLedger`)
- Every financial event creates an immutable `TransactionLedger` record:
  - `Payment`: Player gross debit, platform commission credit, owner net credit.
  - `Refund`: Player credit, platform commission reversal, owner net reversal.
  - `CancellationFee`: Player penalty allocation.
- Ledger records are append-only and never deleted or updated.

### F. Multi-Tier Financial Isolation & Reporting
- **Owner Portal (`/Owner/FinancialReport`)**: Filterable date-range report strictly isolated to the authenticated owner's facilities. Shows Gross Revenue, Platform Commission, Net Revenue, and Refunds.
- **Admin Portal (`/Admin/Transactions`)**: Global transaction log with pagination, filterable by date and transaction type.

---

## 3. Supported Payment Methods

| Method | Provider | Flow | Instant Settlement |
|---|---|---|---|
| `PayAtFacility` | Cash / Internal | Direct Confirmation; pending physical settlement | No |
| `CreditCard` | Paymob Accept | 3D-Secure Iframe / Hosted Checkout | Yes (on webhook) |
| `DebitCard` | Paymob Accept | 3D-Secure Iframe / Hosted Checkout | Yes (on webhook) |
| `DigitalWallet` | Paymob Accept | Mobile Wallet redirect (Vodafone Cash, Orange, etc.) | Yes (on webhook) |
| `Fawry` | Paymob Accept / Fawry | Reference code generated for physical kiosk payout | On payment notification |

---

## 4. Notifications & User Experience
- Real-time notifications dispatched upon payment confirmation (`PaymentReceipt`), gateway failures (`PaymentFailed`), and customer refunds (`PaymentRefunded`).
- Owners receive instant alerts (`PaymentReceived`) detailing the net amount credited to their account.
