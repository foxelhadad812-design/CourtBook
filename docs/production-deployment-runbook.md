# CourtBook (PlaySpot) — Production Deployment Runbook

**Document Version:** 1.0.0  
**Target Release Baseline:** Commit `9360418cafc8364c429c0924d30dadedae3231ab`  
**Classification:** Operational Runbook & Production Deployment Guide  
**Status:** Approved for Production Deployment Planning  

---

## 1. Production Environment Prerequisites

Before deploying the CourtBook platform into staging or production, ensure the target hosting environment satisfies the following infrastructure requirements:

1. **Runtime:** .NET 10.0 ASP.NET Core Runtime (or Linux/Windows container base image `mcr.microsoft.com/dotnet/aspnet:10.0`).
2. **Database:** Microsoft SQL Server 2019+ or Azure SQL Database with TCP/IP enabled and `sp_getapplock` stored procedure permissions granted.
3. **Cache & Messaging:** Redis 6.2+ / Redis Cluster (required if running multiple API replicas with SignalR).
4. **Ingress / Reverse Proxy:** TLS-terminating reverse proxy (Nginx, Traefik, AWS ALB, Azure Application Gateway, Cloudflare) configured to forward standard proxy headers and support WebSockets.
5. **DNS & Networking:** Dedicated domain with valid TLS/SSL certificates (e.g., `api.playspot.app` and `playspot.app`).
6. **Payment Provider Account:** Valid and active live merchant account with Paymob (accepting cards and mobile wallets).

---

## 2. Required Environment Variables

All sensitive credentials and environment-specific settings must be provided via environment variables (or container secrets / Azure Key Vault / AWS Secrets Manager).

> [!CAUTION]
> **ABSOLUTELY NO REAL SECRETS IN CODE OR REPOSITORIES.**  
> The table below uses explicit placeholders. Never hardcode live secrets into configuration files.

| Variable Name | Required | Placeholder Format | Description |
| :--- | :---: | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | **Yes** | `Production` | Activates strict production security assertions. |
| `ConnectionStrings__DefaultConnection` | **Yes** | `<SQL_CONNECTION_STRING>` | SQL Server connection string with credentials. |
| `JwtSettings__Key` | **Yes** | `<JWT_SECRET>` | 256-bit (minimum 32 characters) random secret key. |
| `JwtSettings__Issuer` | **Yes** | `https://api.playspot.app` | Valid issuer URI for JWT token validation. |
| `JwtSettings__Audience` | **Yes** | `https://playspot.app` | Valid audience URI for JWT token validation. |
| `PaymentGateway__Paymob__ApiKey` | **Yes** | `<PAYMOB_API_KEY>` | Live Paymob merchant API key. |
| `PaymentGateway__Paymob__IntegrationId` | **Yes** | `<PAYMOB_INTEGRATION_ID>` | Live Paymob card payment integration ID. |
| `PaymentGateway__Paymob__IframeId` | **Yes** | `<PAYMOB_IFRAME_ID>` | Live Paymob payment iframe ID. |
| `PaymentGateway__Paymob__HmacSecret` | **Yes** | `<PAYMOB_HMAC_SECRET>` | Live Paymob webhook HMAC-SHA512 secret. |
| `HealthChecks__DetailsApiKey` | **Yes** | `<HEALTH_DETAILS_API_KEY>` | High-entropy secret key protecting `/health/details`. |
| `Redis__ConnectionString` | *Multi-Instance* | `<REDIS_CONNECTION_STRING>` | Redis connection string for SignalR backplane. |
| `ForwardedHeaders__KnownProxies__0` | *Optional* | `10.0.0.1` | Static IP of trusted upstream reverse proxy. |
| `ForwardedHeaders__KnownNetworks__0` | *Recommended* | `10.0.0.0/16` | CIDR block of trusted internal Kubernetes / VPC network. |
| `OpenApi__EnableInProduction` | *Optional* | `false` | Defaults to `false`. Set `true` only if public OpenAPI is required. |
| `AllowedOrigins__0` | **Yes** | `https://playspot.app` | Allowed CORS origins for browser web requests. |

---

## 3. SQL Server Configuration

### 3.1 Database Requirements & Constraints
- **Production Assertion:** When `ASPNETCORE_ENVIRONMENT` is set to `Production` or `Staging`, the application validates `ConnectionStrings:DefaultConnection`. It **immediately throws an unrecoverable exception** if the connection string references `(localdb)`, `localhost`, or `127.0.0.1`.
- **Permissions:** The database application user must have:
  - `CONNECT`, `SELECT`, `INSERT`, `UPDATE`, `DELETE` on all application schema tables.
  - `EXECUTE` permissions on `sp_getapplock` and `sp_releaseapplock` (system procedures in SQL Server).

### 3.2 High Availability & Locking Behavior
- **Application-Level Distributed Locking (`sp_getapplock`):**
  - Used by `SettlementWorker` (hourly batch settlement runs).
  - Used by `PaymentHoldWorker` (periodic expired hold releases).
  - Used by `PaymentService` (`CourtBook:Webhook:{provider}:{idempotencyKey}` for webhook processing).
- **Session-Level Exclusivity:** All application locks request `@LockOwner = 'Session'` and `@LockMode = 'Exclusive'`. In a multi-instance web cluster, this ensures that only one worker or webhook processor handles a given operation at any moment.

### 3.3 Backup & Recovery Expectations
- **Financial Subsystem Invariant:** Because CourtBook maintains an immutable double-entry ledger (`TransactionLedger`), the database should utilize the **Full Recovery Model**.
- **Backup Schedule:**
  - Full backups daily.
  - Differential backups every 6 hours.
  - Transaction log backups every 10–15 minutes (enabling Point-In-Time recovery).

> [!WARNING]
> **REQUIRES REAL ENVIRONMENT VALIDATION:**  
> Production SQL Server clustering, Always-On Availability Groups, failover behavior, and real network latency under high concurrent load must be validated in the production/staging cloud environment.

---

## 4. JWT Authentication Configuration

### 4.1 Security Specifications
- **Key Length:** Must be at least 256 bits (32 UTF-8 bytes).
- **Production Assertion:** In `Production` and `Staging`, the application asserts that `JwtSettings:Key` does not match any known development or placeholder keys (e.g. `CourtBook_Super_Secret_Key_For_Jwt_Authentication_2024!`). If detected, the application terminates on startup with `InvalidOperationException`.
- **Lifetime & Clock Skew:**
  - Access tokens have an expiration of 60 minutes.
  - Clock skew is constrained to 30 seconds (`ClockSkew = TimeSpan.FromSeconds(30)`).
- **SignalR Hub Authentication:**
  - The JWT token is securely transmitted via `access_token` query parameter specifically for `/hubs/*` routes, handled by `JwtBearerEvents.OnMessageReceived`.

---

## 5. Paymob Payment Gateway Configuration

### 5.1 Required Configuration Keys
Provide the live production Paymob credentials using environment variables:
```bash
PaymentGateway__Paymob__ApiKey="<PAYMOB_API_KEY>"
PaymentGateway__Paymob__IntegrationId="<PAYMOB_INTEGRATION_ID>"
PaymentGateway__Paymob__IframeId="<PAYMOB_IFRAME_ID>"
PaymentGateway__Paymob__HmacSecret="<PAYMOB_HMAC_SECRET>"
```

### 5.2 Startup Assertion
In `Production`, `Program.cs` validates that none of these values are missing, empty, or set to placeholder strings starting with `OVERRIDE_VIA_ENV_VAR_`. If unconfigured, the application refuses to start.

### 5.3 Webhook Idempotency & Concurrency Safety
The Paymob webhook handler (`PaymentService.ProcessWebhookPaymentCompletedAsync`) implements 3 layers of protection:
1. In-process `SemaphoreSlim` keyed by `{provider}:{idempotencyKey}`.
2. SQL Server `sp_getapplock` distributed session lock across all cluster instances.
3. Database transactional execution with unique composite index `IX_IdempotencyLog_Provider_TransactionId` and `DbUpdateException` race suppression.

> [!WARNING]
> **REQUIRES REAL ENVIRONMENT VALIDATION:**  
> Live Paymob callback delivery, actual bank-switch response latencies, and production webhook deliveries must be validated against Paymob's live gateway environment with a real merchant account.

---

## 6. Redis Configuration & Multi-Instance SignalR

### 6.1 Redis Backplane for SignalR
- In single-instance development, Redis is optional; SignalR runs using the local in-process message bus.
- In multi-instance or auto-scaling container environments (e.g., Kubernetes, AWS ECS, Azure Container Apps), Redis is **mandatory** to broadcast SignalR events (`NotificationHub` and `GameLobbyHub`) across all nodes.
- Configure via:
  ```bash
  Redis__ConnectionString="<REDIS_CONNECTION_STRING>,ssl=true,abortConnect=false"
  ```

### 6.2 Fallback & Fault Tolerance
- If `Redis:ConnectionString` is omitted, the application logs an informational message and operates locally.
- In multi-instance setups without Redis, users connected to Node A will not receive real-time notifications dispatched by actions originating on Node B.

> [!WARNING]
> **REQUIRES REAL ENVIRONMENT VALIDATION:**  
> Multi-node SignalR broadcasting across a live Redis cluster / Redis Sentinel setup must be tested in the target staging cluster.

---

## 7. Forwarded Headers & Reverse Proxy Configuration

### 7.1 Security Considerations: Preventing Header Spoofing
- ASP.NET Core `UseForwardedHeaders` middleware is configured at the very beginning of the pipeline.
- **Security Rule:** Forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`) are **never blindly trusted** from arbitrary public IP addresses.
- By default, ASP.NET Core only trusts loopback addresses (`127.0.0.1`, `::1`).
- To trust the reverse proxy or ingress controller, operators must configure trusted proxies or CIDR blocks:
  ```bash
  # Example for Kubernetes VPC cluster:
  ForwardedHeaders__KnownNetworks__0="10.0.0.0/16"
  # Or specific reverse proxy IP:
  ForwardedHeaders__KnownProxies__0="172.16.0.10"
  ```

---

## 8. Reverse Proxy Configurations (Nginx, Traefik, ALB)

### 8.1 Nginx Production Configuration Example

```nginx
upstream courtbook_api {
    server 10.0.1.20:5000;
    server 10.0.1.21:5000;
    keepalive 32;
}

server {
    listen 80;
    server_name api.playspot.app;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name api.playspot.app;

    ssl_certificate /etc/letsencrypt/live/api.playspot.app/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.playspot.app/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;

    # Standard reverse proxy headers
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    # Forward traffic to ASP.NET Core API
    location / {
        proxy_pass http://courtbook_api;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
    }

    # SignalR WebSockets forwarding
    location /hubs/ {
        proxy_pass http://courtbook_api;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_cache_bypass $http_upgrade;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
}
```

---

## 9. Health Checks & Kubernetes Probes

The application provides three tiered health endpoints:

```
+-----------------------------------------------------------------------------+
| /health         --> Public Liveness Probe (process responsiveness)          |
| /health/ready   --> Public Readiness Probe (database & critical components) |
| /health/details --> Protected Diagnostics (requires X-Health-Key or loopback)|
+-----------------------------------------------------------------------------+
```

### 9.1 Endpoint Mappings

| Endpoint | Probe Type | Purpose | Access Control |
| :--- | :--- | :--- | :--- |
| `/health` | **Liveness Probe** | Confirms process is running and responding. | Public (No auth required) |
| `/health/ready` | **Readiness Probe** | Confirms DB connection and services ready for traffic. | Public (No auth required) |
| `/health/details` | **Diagnostics** | Structured JSON diagnostic report with component metrics. | **Restricted** (`X-Health-Key` or Loopback) |

### 9.2 Kubernetes Pod Health Probe Configuration

> [!CAUTION]
> **DO NOT USE `/health/details` AS A KUBERNETES LIVENESS PROBE.**  
> In Production and Staging, `/health/details` returns HTTP 403 Forbidden to unauthorized requests. If used as a container probe without proper credentials, Kubernetes will falsely flag containers as dead and enter an infinite crash/restart loop.

**Correct Kubernetes Pod Specification:**

```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 5000
  initialDelaySeconds: 10
  periodSeconds: 15
  timeoutSeconds: 5
  failureThreshold: 3

readinessProbe:
  httpGet:
    path: /health/ready
    port: 5000
  initialDelaySeconds: 15
  periodSeconds: 10
  timeoutSeconds: 5
  failureThreshold: 2
```

### 9.3 Accessing Protected `/health/details` Diagnostics
In Production or Staging, operators or internal monitoring tools can query `/health/details` using one of two methods:
1. **API Key Header:** `X-Health-Key: <HEALTH_DETAILS_API_KEY>`
2. **Query Parameter:** `https://api.playspot.app/health/details?apiKey=<HEALTH_DETAILS_API_KEY>`
3. **Local Loopback:** Calling from `localhost` / `127.0.0.1` inside the container/pod requires no key.

The diagnostic payload provides:
- Overall status (`Healthy`, `Degraded`, `Unhealthy`).
- Execution duration per check.
- `SettlementHealthCheck` diagnostic metadata (verifying invariant stability).
- **Security Guarantee:** Zero secrets, connection strings, JWT keys, or PII are ever exposed in this output.

---

## 10. Deployment Order & Execution Steps

Follow this strict step-by-step sequence when deploying a new release:

```
Step 1: Database Migration Check & Backup
   └── Perform full database backup
   └── Run EF Core migrations (if any are pending)

Step 2: External Dependencies Readiness
   └── Verify SQL Server connectivity
   └── Verify Redis cluster connectivity (if multi-instance)

Step 3: Environment Configuration Injection
   └── Set ASPNETCORE_ENVIRONMENT=Production
   └── Inject all required environment variables into Secret Store

Step 4: Deploy Application Container(s)
   └── Launch new container replica(s)
   └── Allow startup assertions to validate credentials

Step 5: Automated Readiness Verification
   └── Ingress routes traffic only when /health/ready returns HTTP 200

Step 6: Ingress & Reverse Proxy Routing
   └── Switch traffic to new release (Blue/Green or Rolling update)

Step 7: Post-Deployment Smoke Test
   └── Query /health/details with X-Health-Key to inspect system telemetry
```

---

## 11. Rollback Procedure

If unexpected errors occur post-deployment:
1. **Container Rollback:** Immediately route traffic back to the previous stable container image tag (e.g. `playspot-api:previous`).
2. **Reverse Proxy Drain:** Gracefully disconnect SignalR clients; clients will automatically attempt reconnection with backoff.
3. **Database Considerations:** Because Phase 10 introduces zero database schema changes or migrations, rolling back the application container requires **no database schema down-migrations**.
4. **Idempotency Preservation:** Do NOT truncate or modify the `IdempotencyLogs` or `TransactionLedger` tables during rollback, as they guarantee payment consistency across versions.

---

## 12. Staging Validation Checklist

Before initiating production go-live, execute this verification checklist in the staging environment:

- [ ] `ASPNETCORE_ENVIRONMENT` is set to `Staging`.
- [ ] Application starts successfully with non-local database connection string.
- [ ] Application rejects startup if placeholder JWT secrets are used.
- [ ] `/health` returns HTTP 200 `Healthy`.
- [ ] `/health/ready` returns HTTP 200 `Healthy` with database checks passing.
- [ ] `/health/details` returns HTTP 403 Forbidden without API key, and HTTP 200 with `X-Health-Key`.
- [ ] SignalR connections successfully establish over WebSockets via reverse proxy.
- [ ] Real-time notification toast appears and reconnections auto-recover when network toggles.
- [ ] Dark/Light theme toggle persists preference across browser sessions without FOUC.
- [ ] Password change successfully terminates all active refresh token device sessions.

---

## 13. External Integrations Requiring Real Environment Validation

The following items cannot be fully validated in local or isolated CI environments and **REQUIRE REAL ENVIRONMENT VALIDATION**:

1. **Live Paymob Payment Gateway:** Live card payments, 3D-Secure bank redirection, and production webhook delivery.
2. **Production SQL Server:** Multi-node availability groups, failover behavior, and `sp_getapplock` performance under load.
3. **Production Redis Cluster:** SignalR hub backplane message propagation across separated VM/container hosts.
4. **Ingress TLS & WebSockets:** Real reverse proxy TLS termination, WSS (Secure WebSocket) connection stability, and connection keep-alive timeout tuning.
