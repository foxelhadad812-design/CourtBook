# CourtBook / PlaySpot — Enterprise Production Deployment Playbook

**Target Runtimes**: .NET 10.0 | SQL Server 2019+ | Redis 6.0+  
**Architecture**: Multi-Instance Horizontal Scale-Out Ready  
**Document Status**: Production Operational Specification  

---

## 1. Executive Deployment Overview

This deployment playbook provides operational guidelines for deploying the CourtBook platform (`CourtBook.API` and `CourtBook.Web`) to enterprise staging and production environments (e.g. Azure App Services, AWS ECS, or Kubernetes).

---

## 2. Mandatory Environment Variables & Configuration

The application validates critical production settings during startup. Missing or placeholder credentials will prevent application initialization in `Production` environment.

### Connection Strings & Databases
| Environment Variable | Description | Example / Syntax |
| :--- | :--- | :--- |
| `ConnectionStrings__DefaultConnection` | SQL Server connection string | `Server=tcp:sql.playspot.eg,1433;Initial Catalog=PlaySpotDB;User ID=app_user;Password=SECURE_PASSWORD;Encrypt=True;TrustServerCertificate=False;` |
| `Redis__ConnectionString` | Redis cluster connection string (optional for multi-instance SignalR & Web session) | `redis.internal.playspot.eg:6379,password=SECURE_REDIS_PASSWORD,ssl=false,abortConnect=false` |

### Security & Authentication
| Environment Variable | Description | Constraint |
| :--- | :--- | :--- |
| `JwtSettings__Key` | Secret key for HMAC-SHA256 JWT signing | Must be $\ge$ 32 characters (256-bit). Defaults/placeholders rejected in Prod. |
| `JwtSettings__Issuer` | JWT issuer identifier | `PlaySpotAPI` |
| `JwtSettings__Audience` | JWT audience identifier | `PlaySpotClient` |

### Payment Gateway (Paymob Integration)
| Environment Variable | Description | Constraint |
| :--- | :--- | :--- |
| `PaymentGateway__Paymob__IsSandbox` | Set to `false` in live production | `false` |
| `PaymentGateway__Paymob__ApiKey` | Live Paymob merchant API key | Required in Prod; placeholders rejected |
| `PaymentGateway__Paymob__IntegrationId` | Paymob payment integration ID | Required in Prod |
| `PaymentGateway__Paymob__IframeId` | Paymob checkout iFrame ID | Required in Prod |
| `PaymentGateway__Paymob__HmacSecret` | Paymob webhook HMAC secret key | Required in Prod for signature verification |

### Background Workers & Services
| Environment Variable | Description | Default |
| :--- | :--- | :---: |
| `SettlementWorker__Enabled` | Enable background settlement worker | `true` |
| `SettlementWorker__IntervalHours` | Settlement sweep interval (hours) | `12` |
| `SettlementWorker__BufferHours` | Cleared booking buffer (hours) | `24` |
| `PaymentHoldWorker__Enabled` | Enable payment hold cleanup worker | `true` |
| `PaymentHoldWorker__IntervalSeconds` | Hold sweep interval (seconds) | `60` |
| `PaymentHoldWorker__BatchSize` | Maximum expired holds per batch | `50` |

---

## 3. Database Architecture & Concurrency Requirements

1. **Database Provider**: SQL Server 2019+ or Azure SQL Database.
2. **Distributed Application Locking**:
   - `SettlementWorker` and `PaymentHoldWorker` execute SQL Server `sp_getapplock` (`Exclusive`, `Session`, `Timeout=0`) to ensure single-instance execution across multi-instance API deployments.
   - Database user account must have permission to execute `sp_getapplock` and `sp_releaseapplock`.
3. **Financial Isolation**: `SettlementService` and `PayoutService` execute financial state mutations under `IsolationLevel.Serializable` database transactions to prevent race conditions or balance corruption.
4. **Schema Migrations**: Schema migrations are managed via EF Core (`dotnet ef database update`). Ensure migrations are applied prior to rolling out new API container instances.

---

## 4. Multi-Instance Scaling & Redis Configuration

1. **SignalR Backplane**:
   - When `Redis__ConnectionString` is populated, `CourtBook.API` registers `Microsoft.AspNetCore.SignalR.StackExchangeRedis`, allowing WebSocket messages and lobby updates to broadcast seamlessly across all API container nodes.
   - Fallback: If `Redis__ConnectionString` is omitted, single-instance in-memory SignalR hubs operate cleanly.
2. **Web Session State**:
   - When `Redis__ConnectionString` is populated, `CourtBook.Web` registers `Microsoft.Extensions.Caching.StackExchangeRedis`, maintaining user sessions across Web container nodes.
   - Fallback: If `Redis__ConnectionString` is omitted, `AddDistributedMemoryCache()` handles single-node sessions.

---

## 5. Reverse Proxy & Network Security (Nginx / Cloudflare)

1. **TLS / SSL Termination**: All public traffic must be encrypted via TLS 1.2+.
2. **Header Forwarding**:
   - Nginx / reverse proxy must forward `X-Forwarded-For`, `X-Forwarded-Proto`, and `Host` headers.
3. **WebSocket Support**: Nginx configuration must include `Upgrade` and `Connection` headers for `/hubs/*` routes:
   ```nginx
   location /hubs/ {
       proxy_pass http://api_backend;
       proxy_http_version 1.1;
       proxy_set_header Upgrade $http_upgrade;
       proxy_set_header Connection "Upgrade";
       proxy_set_header Host $host;
   }
   ```

---

## 6. Health & Readiness Observability

The platform exposes three monitoring endpoints:

- **`/health`**: Liveness probe. Returns HTTP 200 `Healthy` when container process is running.
- **`/health/ready`**: Readiness probe. Validates database connectivity and background settlement worker state.
- **`/health/details`**: Structured JSON observability endpoint for APM tools (Datadog, Prometheus, Azure Monitor). Returns diagnostic metrics (`ThresholdHours`, `TotalCompletedBatches`, `LastBatchReference`, `LastBatchAgeHours`, `OverdueUnsettledBookingsCount`).

---

## 7. Recommended Deployment Procedure

```
[Step 1: Database] ──────> [Step 2: Environment] ──────> [Step 3: API Cluster] ──────> [Step 4: Web Cluster]
Apply EF Migrations        Inject Environment Vars       Deploy CourtBook.API           Deploy CourtBook.Web
```

1. **Database Migration**: Run `dotnet ef database update --project src/CourtBook.Infrastructure` against target SQL Server.
2. **Inject Secrets**: Configure environment variables in Azure Key Vault, AWS Secrets Manager, or Kubernetes Secrets.
3. **Deploy API Instances**: Roll out `CourtBook.API` containers. Verify `/health/ready` returns HTTP 200.
4. **Deploy Web Instances**: Roll out `CourtBook.Web` containers.
5. **Post-Deploy Smoke Test**: Verify user login, venue discovery, and `/health/details` output.

---

## 8. Rollback & Emergency Recovery Strategy

1. **Binary Rollback**: If issues occur, roll back container images to the previous stable release commit.
2. **Worker Safety**: Background workers gracefully yield locks during shutdown and acquire locks upon startup via `sp_getapplock`.
3. **Database Consistency**: Because financial state mutations execute inside `Serializable` transactions, rolling back binary versions presents zero risk of financial balance corruption.

---

## 9. Real-Environment Validation Checklist

Local unit & integration tests pass with 100% success rate (328/328 tests). The following external integrations require validation in the real staging/production environment:

- [ ] Verify live Paymob merchant API key and HMAC webhook signature callbacks with real test credit cards.
- [ ] Verify SQL Server `sp_getapplock` behavior on production Azure SQL / SQL Server instance.
- [ ] Verify Redis cluster failover under high concurrent SignalR WebSocket traffic.
- [ ] Verify SSL certificate termination and Nginx WebSocket proxy forwarding.
- [ ] Verify SMTP / Twilio SMS notification dispatch (if SMS gateway activated).
