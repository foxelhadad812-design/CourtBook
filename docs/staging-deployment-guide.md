# CourtBook (PlaySpot) — Staging Deployment & Validation Guide

**Document Version:** 1.0.0  
**Target Release Baseline:** Commit `24981c83a9d37604cebfddb763edcb2cd182ec48`  
**Classification:** Staging Deployment Manual & Operational Guide  
**Status:** Approved for Staging Execution Planning  

---

## 1. Executive Summary & Staging Objectives

This guide establishes the standardized procedure for deploying the CourtBook (PlaySpot) platform into a **Staging environment**.

The primary objectives of the Staging environment are:
1. Validate real external integrations (Paymob Sandbox/Staging gateway, cloud-hosted SQL Server, Redis cache).
2. Validate reverse-proxy TLS termination and SignalR WebSocket longevity under real network conditions.
3. Perform end-to-end smoke testing of complete user booking, payment confirmation, and owner settlement workflows without risking production data or real financial funds.

---

## 2. Staging Architecture & Topology

The standard Staging topology mirrors the production architecture in an isolated tier:

```
[ Internet / Clients ]
         │ (HTTPS / WSS)
         ▼
[ Reverse Proxy / Ingress ] (Nginx / ALB / Traefik - TLS Termination)
         │
    ┌────┴──────────────────────────────┐
    │ (Forwarded Headers)               │
    ▼                                   ▼
[ CourtBook.API ] (Port 5000)      [ CourtBook.Web ] (Port 5100)
    │             │                     │
    │             └───────┬─────────────┘
    ▼                     ▼
[ Staging SQL Server ] [ Staging Redis ]
(sp_getapplock)        (SignalR Backplane & Session)
```

---

## 3. Staging Prerequisites & Readiness Classification

To avoid confusion, staging components are strictly categorized into three classifications:

### 3.1 Codebase-Ready Items (100% Complete & Tested)
- Application compilation (.NET 10, 0 warnings, 0 errors).
- Automated test coverage (343 / 343 tests passing).
- Database migration chain (12 existing EF Core migrations).
- Multi-stage container definitions (`Dockerfile.api`, `Dockerfile.web`).
- Container orchestration manifest (`docker-compose.staging.yml`).
- Migration execution scripts (`scripts/apply-staging-migrations.sh`, `.ps1`).
- Reverse proxy configuration template (`nginx/staging.conf`).
- Startup security assertions for Staging (LocalDB/localhost rejection, JWT placeholder check).

### 3.2 Environment-Dependent Items (Requires Staging Infrastructure Provisioning)
- Staging SQL Server instance (cloud or containerized) accessible over TCP 1433.
- Staging Redis server / cluster (optional for single-instance, required for multi-node).
- Ingress controller / Reverse proxy with valid staging TLS certificates.
- Dedicated staging DNS records (e.g. `staging.playspot.internal` and `staging-api.playspot.internal`).

### 3.3 Human Credentials Required (Must be Injected via Secrets)
- Staging SQL Server credentials.
- Staging JWT signing key ($\ge$ 32 random characters).
- Paymob Sandbox merchant account credentials (`ApiKey`, `IntegrationId`, `IframeId`, `HmacSecret`).
- Staging Health diagnostic key (`HealthChecks__DetailsApiKey`).

---

## 4. Required Environment Variables

All settings must be injected via container environment variables or a secure `.env.staging` file (derived from `.env.staging.template`).

> [!CAUTION]
> **NO REAL SECRETS IN CODE OR REPOSITORIES.**  
> Always use unmistakable placeholders in configuration templates. Never commit `.env.staging` to source control.

| Environment Variable | Required | Placeholder Format | Description |
| :--- | :---: | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | **Yes** | `Staging` | Activates staging security assertions. |
| `ConnectionStrings__DefaultConnection` | **Yes** | `<STAGING_SQL_CONNECTION_STRING>` | Target SQL Server connection string. |
| `JwtSettings__Key` | **Yes** | `<STAGING_JWT_SECRET>` | Random 256-bit secret key ($\ge$ 32 chars). |
| `JwtSettings__Issuer` | **Yes** | `https://<STAGING_API_HOSTNAME>` | JWT issuer URI. |
| `JwtSettings__Audience` | **Yes** | `https://<STAGING_WEB_HOSTNAME>` | JWT audience URI. |
| `PaymentGateway__Paymob__IsSandbox` | **Yes** | `true` | Enables Paymob sandbox mode. |
| `PaymentGateway__Paymob__ApiKey` | **Yes** | `<STAGING_PAYMOB_API_KEY>` | Paymob sandbox API key. |
| `PaymentGateway__Paymob__IntegrationId` | **Yes** | `<STAGING_PAYMOB_INTEGRATION_ID>` | Paymob card integration ID. |
| `PaymentGateway__Paymob__IframeId` | **Yes** | `<STAGING_PAYMOB_IFRAME_ID>` | Paymob checkout iframe ID. |
| `PaymentGateway__Paymob__HmacSecret` | **Yes** | `<STAGING_PAYMOB_HMAC_SECRET>` | Webhook HMAC-SHA512 secret. |
| `HealthChecks__DetailsApiKey` | **Yes** | `<STAGING_HEALTH_DETAILS_API_KEY>` | Shared secret protecting `/health/details`. |
| `Redis__ConnectionString` | *Multi-Instance* | `<STAGING_REDIS_CONNECTION_STRING>` | Redis connection for SignalR & session cache. |
| `ForwardedHeaders__KnownNetworks__0` | *Recommended* | `10.0.0.0/16` | Trusted VPC CIDR for reverse proxy headers. |
| `ApiSettings__BaseUrl` | **Yes (Web)** | `https://<STAGING_API_HOSTNAME>` | API endpoint consumed by Web frontend. |
| `AllowedOrigins__0` | **Yes (API)** | `https://<STAGING_WEB_HOSTNAME>` | CORS allowed origin for Web frontend. |

---

## 5. Database Deployment & Migration Safety

### 5.1 Strict Migration Safety Policy
- **Zero New Migrations:** Staging deployment applies the **existing 12 migrations** in the repository. No new migrations will be generated during staging.
- **Zero Schema Redesign:** The database model is frozen.
- **Zero Destructive Operations:** Do NOT run `database drop` or table truncations.

### 5.2 Existing Migration Chain
The repository contains 12 verified migrations targeting `AppDbContext`:
1. `20260920160641_InitialCreate`
2. `20260920222408_ExpandDomainModels`
3. `20260921140057_AddVenueApprovalAndTerms`
4. `20260921175519_AddPlayerDobAndGameAgeLimits`
5. `20260921225715_AddCancellationFeeAndGameCourtIndex`
6. `20260922113648_AddPaymentGatewayAndLedger`
7. `20260922124104_AddPaymentHoldIndex`
8. `20260922141236_AddRefreshTokenSystem`
9. `20260922144948_AddRefreshTokenConcurrencyStamp`
10. `20260922154805_AddAdvancedMatchmakingAndLobby`
11. `20260922170959_AddCommunityAndMarketplaceLayer`
12. `20260922190943_AddFinancialPayoutAndSettlement`

### 5.3 Executing Migrations on Staging
Use the provided safe migration scripts:

**Linux / macOS:**
```bash
chmod +x scripts/apply-staging-migrations.sh
./scripts/apply-staging-migrations.sh "<STAGING_SQL_CONNECTION_STRING>"
```

**Windows PowerShell:**
```powershell
./scripts/apply-staging-migrations.ps1 -ConnectionString "<STAGING_SQL_CONNECTION_STRING>"
```

**Direct EF Core CLI:**
```bash
dotnet ef database update \
  --project src/CourtBook.Infrastructure \
  --startup-project src/CourtBook.API \
  --connection "<STAGING_SQL_CONNECTION_STRING>"
```

---

## 6. Deployment Modes (Single-Instance vs. Multi-Instance)

### Mode 1: Single-Instance Staging (Minimal Footprint)
- Run 1 API container and 1 Web container.
- Redis is **optional**:
  - SignalR defaults cleanly to the in-process message hub.
  - Web session cache defaults cleanly to `AddDistributedMemoryCache()`.
- Ideal for initial smoke-testing and QA feature validation.

### Mode 2: Multi-Instance Staging (Scale-Out Simulation)
- Run 2+ API containers and 2+ Web containers behind Nginx/ALB.
- Redis is **mandatory**:
  - Configured via `Redis__ConnectionString`.
  - SignalR uses `AddStackExchangeRedis` for cross-node WebSocket messaging.
  - Web uses `AddStackExchangeRedisCache` for shared session state.
- SQL Server `sp_getapplock` coordinates background workers (`SettlementWorker`, `PaymentHoldWorker`) so only one node executes jobs per cycle.

---

## 7. Health Probe Mapping & Observability

```
+-------------------------------------------------------------------------------+
| Endpoint        | Probe Type       | Access Control                           |
+-----------------+------------------+------------------------------------------+
| /health         | Liveness Probe   | Public (HTTP 200 Healthy)                |
| /health/ready   | Readiness Probe  | Public (HTTP 200 Healthy with DB checks) |
| /health/details | Diagnostic Probe | Restricted (X-Health-Key or Loopback)    |
+-------------------------------------------------------------------------------+
```

> [!CAUTION]
> **CRITICAL PROBE CONFIGURATION RULE:**  
> **NEVER** configure `/health/details` as a container liveness probe.  
> In Staging, unauthorized requests to `/health/details` return **HTTP 403 Forbidden**. If configured as a liveness probe, Kubernetes or Docker will continually kill healthy containers.

**Correct Kubernetes Staging Deployment Spec:**
```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 5000
  initialDelaySeconds: 10
  periodSeconds: 15
readinessProbe:
  httpGet:
    path: /health/ready
    port: 5000
  initialDelaySeconds: 15
  periodSeconds: 10
```

---

## 8. Staging Smoke-Test Checklist

Once deployed to Staging, execute this systematic verification checklist:

### A. Infrastructure & Connectivity
- [ ] Application starts successfully with `ASPNETCORE_ENVIRONMENT=Staging`.
- [ ] Startup assertion verifies database connection is non-local.
- [ ] Startup assertion rejects placeholder JWT keys.
- [ ] `/health` returns HTTP 200 `Healthy`.
- [ ] `/health/ready` returns HTTP 200 with database check passed.
- [ ] `/health/details` returns HTTP 403 without API key.
- [ ] `/health/details` returns HTTP 200 with `X-Health-Key: <STAGING_HEALTH_DETAILS_API_KEY>`.

### B. Security & Authentication
- [ ] User registration and login succeed with JWT tokens.
- [ ] Single-use refresh token rotation works correctly.
- [ ] Changing user password immediately terminates all active refresh token device sessions.
- [ ] Rate limiting rejects excessive requests with HTTP 429.

### C. Booking & Real-Time Flow
- [ ] Venue and court catalogs load with dynamic hourly pricing.
- [ ] Available time slots calculate correctly without double-booking.
- [ ] Unpaid booking hold expires after 10 minutes and is cleaned up by `PaymentHoldWorker`.
- [ ] SignalR connects via WebSocket over WSS (`/hubs/notifications`).
- [ ] Reconnection toast appears and dismisses cleanly upon network toggle.

### D. Paymob Payment Flow (Sandbox)
- [ ] Booking initiates Paymob payment key generation.
- [ ] Checkout iframe loads Paymob test payment card screen.
- [ ] Test card transaction completes and redirects to booking confirmation.
- [ ] Webhook callback is received and processed with constant-time HMAC-SHA512 verification.
- [ ] Duplicate webhook delivery is suppressed cleanly by idempotency engine.

### E. Financial Integrity
- [ ] `TransactionLedger` logs payment entry with gross, commission, and net amounts.
- [ ] `OwnerBalance` credits pending balance.
- [ ] `SettlementWorker` processes eligible payments and transitions funds to available balance.
- [ ] Financial health check verifies `AvailableBalance >= 0`.

---

## 9. Real-Environment Validation Boundary

The repository and its artifacts are 100% prepared for staging deployment. However, the following items **CANNOT BE VALIDATED LOCALLY** and strictly require a live cloud/staging environment:

> [!WARNING]
> **REQUIRES REAL STAGING ENVIRONMENT VALIDATION:**
> 1. **Live Paymob Sandbox Integration:** Validating real HTTP requests to `accept.paymob.com` and receiving real webhook callbacks requires live sandbox credentials and a publicly reachable webhook URL.
> 2. **Production SQL Server Cluster:** Validating `sp_getapplock` performance, failover behavior, and multi-node transaction latency requires a real SQL Server instance.
> 3. **Production Redis Cluster:** Validating cross-node SignalR message broadcasting requires at least two separate container nodes connected to a real Redis server.
> 4. **Reverse Proxy & TLS Termination:** Validating WSS secure WebSocket connections and proxy header forwarding requires a live reverse proxy with signed TLS certificates.
