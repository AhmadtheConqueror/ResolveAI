# ResolveAI Operations & Production Hardening Guide

This document outlines operational requirements, deployment procedures, database management, security architecture, and operational expectations for ResolveAI.

---

## 1. System Architecture & Components

ResolveAI consists of two primary applications:
1. **Backend API**: ASP.NET Core (.NET 10) REST API backed by PostgreSQL and Entity Framework Core.
2. **Frontend Web**: Single-page application built with React 19, TypeScript, and Vite.

### Core Service Ports
- **Backend API**: `http://localhost:5151`
- **Frontend Web**: `http://localhost:5173`

---

## 2. Environment Variables & Configuration

### Backend (`appsettings.json` / Environment Variables)

Configuration keys can be provided via environment variables in production (using `__` or `:` as separator) or via .NET User Secrets in development.

| Configuration Key | Purpose | Required in Production | Example / Default |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string | Yes | `Host=postgres;Port=5432;Database=resolveai;Username=resolveai_app;Password=REDACTED` |
| `Jwt:Key` | Symmetric HMAC-SHA256 signing key (min 32 chars / 256 bits) | Yes | Secure random string (never committed) |
| `Jwt:Issuer` | JWT token issuer | Yes | `ResolveAI.Api` |
| `Jwt:Audience` | JWT token audience | Yes | `ResolveAI.Web` |
| `AI:Provider` | AI provider for incident analysis and resolution assistance | Yes (if AI enabled) | `Gemini` |
| `AI:Model` | AI model for incident analysis and resolution assistance | Yes (if AI enabled) | `gemini-2.5-flash` |
| `AI:ApiKey` | Google AI API key for Gemini-backed AI features | Yes (if AI enabled) | API key string (never committed) |
| `Features:AllowPublicRegistration` | Disables/enables self-registration | No (default `false`) | `false` |
| `Frontend:AllowedOrigins` | Allowed CORS origins for frontend client | Yes | `http://localhost:5173` or `https://resolveai.yourdomain.com` |
| `Frontend:BaseUrl` | Frontend client base URL for email action links | Yes | `http://localhost:5173` |
| `ExternalNotifications:EmailEnabled` | Master toggle for external email dispatching | No (default `false`) | `false` (in dev), `true` (in prod) |
| `ExternalNotifications:OverrideRecipient` | Safe development recipient override | No | `delivered@resend.dev` (never in prod) |
| `Resend:ApiKey` | Resend REST API authorization key | Yes (if email enabled) | `re_...` (never committed) |
| `Resend:FromEmail` | Verified sending email address | Yes (if email enabled) | `notifications@resolveai.dev` |
| `Resend:FromName` | Sender display name | No | `ResolveAI Notifications` |
| `Resend:ApiUrl` | Resend REST API endpoint | No | `https://api.resend.com/emails` |

> [!IMPORTANT]
> **Production Startup Validation**:
> In non-development environments, the API enforces fast-fail validation:
> - `Jwt:Key` must be present and contain at least 32 characters (256 bits).
> - `Jwt:Issuer` and `Jwt:Audience` must be non-empty.
> Failure to satisfy these halts the process immediately with an `InvalidOperationException` without printing secrets to logs.

### Frontend (`.env` / Environment Variables)

| Variable | Purpose | Default |
|---|---|---|
| `VITE_API_URL` | Base URL of the ResolveAI backend API | `http://localhost:5151` |

---

## 3. Database Management & Migrations

### Applying Migrations
Database migrations are managed through Entity Framework Core:

```powershell
# From backend/ResolveAI.Api
dotnet ef database update
```

### Migration Chain History Note
- Migration `20260916145517_AddNotificationIncidentSnapshot` was initially generated empty.
- Migration `20260917102511_AddIncidentAuditTrail` subsequently picked up and created the `IncidentNumber` and `IncidentTitle` columns on the `Notifications` table.
- Migration `20260917123800_AddExternalEmailNotifications` created the `ExternalNotificationDeliveries` table and added `EmailNotificationsEnabled` to `Users`.
- **Rule**: Never retroactively modify or delete applied migrations in source control. The current model snapshot and migration chain are fully aligned.

---

## 4. External Email Notifications (Resend)

ResolveAI provides transactional email notifications built on top of the in-app notification system.

### Architectural Principles
1. **In-App Remains Source of Truth**: In-app notifications are persisted and functional independently of external email delivery.
2. **Failure Isolation**: Email delivery staging or provider failures **NEVER** fail or rollback business transactions (incident creation, assignment, comments, status changes, resolution, SLA processing, or audit history).
3. **Provider Abstraction**: ResolveAI business logic depends on `IEmailSender`. Resend is the first implementation, communicating over HTTPS using `HttpClient` (`POST https://api.resend.com/emails`).
4. **Asynchronous Dispatch**: High-value notifications stage an `ExternalNotificationDelivery` row with `Status = Pending`. A background worker (`ExternalNotificationDeliveryWorker`) claims and sends deliveries asynchronously.

### Email Eligibility Matrix
Only high-value, actionable notifications trigger email deliveries:
- **Employee / Reporter**: Incident assigned/reassigned, reply from Technician/Manager/Admin, incident resolved, incident closed.
- **Technician**: Incident assigned/reassigned to them, reply from Reporter, SLA At Risk on assigned incident, SLA Breached on assigned incident.
- **Manager / Admin Fallback**: New incident requiring triage, reporter reply on unassigned incident, SLA At Risk, SLA Breached.
- **Non-Email Events**: Status updates (e.g. `InProgress`, `WaitingForUser`), priority adjustments, and internal technician banter remain in-app only.

### Retry Policy & Idempotency
- Deliveries use stable, deterministic idempotency keys: `email/{notificationId}/{userId}`.
- Resend `Idempotency-Key` headers are transmitted on all calls, preventing duplicate sends on network retries.
- Retry Schedule for transient failures (HTTP 429, 5xx, timeouts):
  - Attempt 1: Immediate/next poll
  - Attempt 2: +1 minute
  - Attempt 3: +5 minutes
  - Attempt 4: +30 minutes
  - After 4 attempts or permanent errors (4xx validation/unauthorized): marked `PermanentlyFailed`.

### Safe Development & Test Controls
- `ExternalNotifications:EmailEnabled` defaults to `false`. No external sending occurs unless explicitly enabled.
- `ExternalNotifications:OverrideRecipient` (e.g. `delivered@resend.dev`): When running in `Development`, all outbound emails are redirected to this address, preventing accidental customer emails during testing.
- Manual smoke test command:
  ```powershell
  dotnet run --no-restore --project backend/ResolveAI.Api.Tests -- --smoke-test
  ```

### Admin Diagnostics API
Admins can monitor email delivery status via `GET /api/admin/external-notifications`:
- Returns recent delivery status, attempt counts, timestamps, provider message IDs, and error summaries.
- Strictly excludes API keys, authorization headers, or provider secrets.

---

## 5. Backup & Disaster Recovery

ResolveAI deliberately delegates backup automation to the database hosting layer (e.g. AWS RDS, Azure Database for PostgreSQL, Google Cloud SQL, or automated cron jobs).

### Manual PostgreSQL Backup (`pg_dump`)
To take a full compressed binary dump of the ResolveAI database:

```bash
pg_dump -h <host> -p 5432 -U <username> -d resolveai -F c -b -v -f "resolveai_backup_$(date +%Y%m%d_%H%M%S).dump"
```

### Manual PostgreSQL Restore (`pg_restore`)
To restore the backup into a target database:

```bash
pg_restore -h <host> -p 5432 -U <username> -d resolveai -v --clean --if-exists "resolveai_backup_<timestamp>.dump"
```

*Note: For plain SQL dumps (`-F p`), restore using `psql`:*
```bash
psql -h <host> -p 5432 -U <username> -d resolveai -f "resolveai_backup.sql"
```

---

## 6. Health Probes & Monitoring

ResolveAI exposes standard health check endpoints:

### 1. Liveness Probe: `GET /health`
- **Purpose**: Verifies that the ASP.NET Core process is running and responding to HTTP requests.
- **Dependency**: Process only.
- **Response**: `200 OK` (`Healthy`).

### 2. Readiness Probe: `GET /health/ready`
- **Purpose**: Verifies that the API can connect to and query PostgreSQL (`AppDbContext`).
- **Dependency**: PostgreSQL database.
- **Response**: `200 OK` (`Healthy`) if database connection is confirmed; `503 Service Unavailable` if database is down.

> [!NOTE]
> **Third-Party Dependency Isolation**:
> External services (Google Gemini AI and Resend Email) are intentionally excluded from the readiness probe. A third-party outage, rate-limit, or unconfigured key must not mark ResolveAI as down.

---

## 7. Security Hardening Details

### 1. Per-Request User & Role Validation
Stateless JWT tokens present a known limitation: if an Admin deactivates a user or demotes their role, an existing token remains valid until expiry.
- ResolveAI includes `UserValidationMiddleware` immediately following authentication.
- Every authenticated request queries the database for the user's active state and current role.
- If `user.IsActive == false` or the user was deleted, the request is terminated with `401 Unauthorized`.
- If the user's role in the database differs from the token, the active principal's role claim is replaced with the database truth, preventing privilege escalation.
- *Performance*: Uses a fast indexed primary-key query (`FindAsync` / `SingleOrDefaultAsync`).

### 2. Login Hardening & Anti-Enumeration
- Invalid email, invalid password, or deactivated account all return a uniform response:
  `{"message": "Invalid email or password."}`
- The API never discloses whether an account exists or is disabled.
- Password hashes use ASP.NET Core `IPasswordHasher<AppUser>` (PBKDF2 HMAC-SHA512).

### 3. Rate Limiting
Built-in ASP.NET Core rate limiting protects sensitive endpoints:
- `POST /api/auth/login`: 10 requests / minute per client IP (`login-limiter`).
- `POST /api/incidents/{id}/ai-analysis`: 5 requests / minute per authenticated user (`ai-limiter`).
- Rejections return standard RFC 7807 `429 Too Many Requests` ProblemDetails.

### 4. Security Headers
All responses include standard defense-in-depth headers:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: strict-origin-when-cross-origin`

---

## 8. Automated Testing Strategy

### Backend Tests
Execute via:
```powershell
# From backend/ResolveAI.Api.Tests
dotnet run --no-restore
# or
dotnet build -t:Test --no-restore
```
Covers 7 automated suites (all mocked, zero external dependencies required):
- **IncidentWorkflowTests**: Complete state machine transitions across Employee, Technician, Manager, Admin.
- **SlaServiceTests**: Target calculation for Low, Medium, High, Critical; 75% AtRisk threshold; Breached/Met status; recalculation from original CreatedAt.
- **NotificationServiceTests**: Assignment recipient routing; reporter notifications; SLA deduplication keys; actor exclusion.
- **AuditTrailTests**: Incident creation; status change Old/New values; privacy protection for comment bodies; System actor types; AI approval attribution.
- **UserAdminAndAuthTests**: Sole active Admin protection (demotion and deactivation prevention); second Admin flexibility; inactive account rejection; role claims synchronization.
- **AiIncidentServiceTests**: RBAC on trigger and application; enforcement of stored recommendations over arbitrary client inputs; error mapping.
- **ExternalEmailDeliveryTests**: 19 scenarios covering qualifying vs non-qualifying delivery, worker processing, retry schedules, max attempts, idempotency keys, duplicate prevention, and failure isolation.

### Frontend Tests
Execute via:
```powershell
# From frontend/resolveai-web
npm test
```
Covers:
- **apiErrorHandling.test.ts**: 401 session clearing, 403 forbidden, 429 rate limit mapping, ProblemDetails parsing.
- **navigationAndRbac.test.ts**: Role-based link visibility (Employee, Technician, Manager, Admin).
- **notificationAndAuditState.test.ts**: Notification badge count formatting, empty vs loaded audit states.
- **analyticsCalculations.test.ts**: Zero-division protection and NaN prevention.

---

## 9. Known Architecture Decisions & Deliberate Limitations

1. **Authentication**: Local authentication with ASP.NET PasswordHasher remains the primary identity system. Microsoft Entra SSO is a candidate for future optional authentication provider.
2. **Tokens & Sessions**: Tokens have a 2-hour lifetime. Refresh-token rotation and sliding session cookies are deferred to future enterprise hardening.
3. **External Notifications**: Transactional email via Resend is implemented behind `IEmailSender`. Additional channels (Microsoft Teams, SMS, Webhooks) and email digest features remain deferred.
4. **Caching & Multi-Instance**: No distributed caching (Redis) is used in this phase. Database lookups per authenticated request ensure consistency without cache invalidation complexity.
5. **Worker Coordination**: `SlaNotificationBackgroundService` and `ExternalNotificationDeliveryWorker` run as in-process `IHostedService` instances. In a multi-instance deployment, leader election or a distributed scheduler (e.g., Hangfire/Quartz) would be evaluated.
