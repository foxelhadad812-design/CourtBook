# PlaySpot / CourtBook — Paymob Sandbox Setup & Configuration Guide

This guide details how to configure and execute real-world sandbox testing with **Paymob Egypt** on PlaySpot.

---

## 1. Environment Configuration

To test against Paymob's live sandbox environment, configure the following environment variables (or user secrets in development):

| Setting Key | Environment Variable Name | Example / Description |
| :--- | :--- | :--- |
| `PaymentGateway:Paymob:IsSandbox` | `PaymentGateway__Paymob__IsSandbox` | `"true"` |
| `PaymentGateway:Paymob:ApiKey` | `PaymentGateway__Paymob__ApiKey` | `ZXlKaGJHY2lPaUpJVXpVeE1pSX...` (Paymob Dashboard -> Settings -> Account Info -> API Key) |
| `PaymentGateway:Paymob:IntegrationId` | `PaymentGateway__Paymob__IntegrationId` | `482910` (Paymob Dashboard -> Developers -> Payment Integrations -> Card Integration ID) |
| `PaymentGateway:Paymob:IframeId` | `PaymentGateway__Paymob__IframeId` | `839102` (Paymob Dashboard -> Developers -> Iframes -> Standard Checkout Iframe ID) |
| `PaymentGateway:Paymob:HmacSecret` | `PaymentGateway__Paymob__HmacSecret` | `9B8C7D6E5F4A3B2C...` (Paymob Dashboard -> Settings -> Account Info -> HMAC Secret) |

> **Security Invariant**: Never commit live API keys, integration IDs, or HMAC secrets into Git. Always inject them via environment variables or secret vaults in CI/CD and production.

---

## 2. Setting Up Your Paymob Sandbox Account

1. Register or log into [Paymob Accept Portal](https://accept.paymob.com/portal2/en/login).
2. Ensure your account is toggled to **Test / Sandbox Mode**.
3. Under **Settings -> Account Info**:
   - Copy your **API Key**.
   - Copy your **HMAC Secret** (used for cryptographic webhook validation).
4. Under **Developers -> Payment Integrations**:
   - Create an Online Card Integration (Select Currency: **EGP**).
   - In **Transaction Processed Callback**:
     - Set URL to: `https://<YOUR_API_DOMAIN>/api/payments/webhook` (or your Ngrok tunnel URL for local testing).
   - In **Transaction Response Callback**:
     - Set URL to: `https://<YOUR_WEB_DOMAIN>/Payments/Success`
   - Copy the generated **Integration ID**.
5. Under **Developers -> Iframes**:
   - Create or select an existing checkout iframe.
   - Copy the **Iframe ID**.

---

## 3. Test Card Credentials (Paymob Sandbox)

Paymob provides official test credentials to simulate various transaction outcomes:

| Test Case | Card Number | Expiry | CVV | 3D Secure OTP | Expected Outcome |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Successful Payment** | `4111 1111 1111 1111` | Any future date (e.g. `12/28`) | `123` | `1111` or any OTP | **Success** (Status: `Completed`, Ledger Created) |
| **3DS Challenge** | `5123 4567 8901 2346` | Any future date | `123` | `1111` | Redirects to OTP challenge then succeeds |
| **Insufficient Funds** | `4000 0000 0000 0002` | Any future date | `123` | N/A | **Declined** (Status: `Failed`) |
| **Declined / Invalid Card** | `4000 0000 0000 0001` | Any future date | `123` | N/A | **Declined** (Status: `Failed`) |

---

## 4. End-to-End Sandbox Verification Flow

1. **Start PlaySpot API & Web**:
   ```bash
   # Terminal 1 - API
   dotnet run --project src/CourtBook.API

   # Terminal 2 - Web
   dotnet run --project src/CourtBook.Web
   ```

2. **Expose Webhook (Local Testing Only)**:
   When running locally, Paymob's servers cannot reach `localhost`. Use a secure tunnel such as Ngrok or Cloudflare Tunnel:
   ```bash
   ngrok http 5257
   ```
   Update your Paymob integration's **Transaction Processed Callback** to `https://<your-ngrok-subdomain>.ngrok-free.app/api/payments/webhook`.

3. **Reserve & Pay**:
   - Navigate to `http://localhost:5100/Venues` and book a court.
   - On `/Payments/Pay?bookingId=<ID>`, select **Online Card Payment**.
   - You will be redirected to the Paymob secure checkout iframe.
   - Enter test card `4111 1111 1111 1111` with future expiry and CVV `123`.
   - Submit payment.
   - Paymob redirects back to `/Payments/Success`.
   - The server verifies transaction reference with Paymob and receives the webhook.
   - Payment status updates to `Completed`, booking is confirmed, double-entry ledger entries are recorded, and real-time SignalR notification is dispatched.

---

## 5. Paymob Sandbox Verification Status Note

If valid Paymob sandbox credentials (`PaymentGateway__Paymob__ApiKey`, etc.) are not configured in your current environment:
- The system defaults to simulated/mocked gateway verification for automated testing.
- Live Paymob network communication is cleanly bypassed until real sandbox credentials are provided.
