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
| `Gemini:ApiKey` | Google Gemini API Key for AI Incident Analysis | Optional | API key string (AI features disabled/mocked if omitted) |
| `Features:AllowPublicRegistration` | Disables/enables self-registration | No (default `false`) | `false` |
| `Frontend:AllowedOrigins` | Allowed CORS origins for frontend client | Yes | `http://localhost:5173` or `https://resolveai.yourdomain.com` |

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
- **Rule**: Never retroactively modify or delete applied migrations in source control. The current model snapshot and migration chain are fully aligned.

---

## 4. Backup & Disaster Recovery

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

## 5. Health Probes & Monitoring

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
> **AI Dependency Isolation**:
> External AI providers (Google Gemini) are intentionally excluded from the readiness probe. A third-party AI service outage or rate-limit must not mark the incident management system as unhealthy.

---

## 6. Security Hardening Details

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

## 7. Automated Testing Strategy

### Backend Tests
Execute via:
```powershell
# From backend/ResolveAI.Api.Tests
dotnet run --no-build
# or
dotnet build -t:Test --no-restore
```
Covers:
- **IncidentWorkflowTests**: Complete state machine transitions across Employee, Technician, Manager, Admin.
- **SlaServiceTests**: Target calculation for Low, Medium, High, Critical; 75% AtRisk threshold; Breached/Met status; recalculation from original CreatedAt.
- **NotificationServiceTests**: Assignment recipient routing; reporter notifications; SLA deduplication keys; actor exclusion.
- **AuditTrailTests**: Incident creation; status change Old/New values; privacy protection for comment bodies; System actor types; AI approval attribution.
- **UserAdminAndAuthTests**: Sole active Admin protection (demotion and deactivation prevention); second Admin flexibility; inactive account rejection; role claims synchronization.
- **AiIncidentServiceTests**: RBAC on trigger and application; enforcement of stored recommendations over arbitrary client inputs; error mapping.

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

## 8. Known Architecture Decisions & Deliberate Limitations

1. **Authentication**: Local authentication with ASP.NET PasswordHasher remains the primary identity system. Microsoft Entra SSO is a candidate for future optional authentication provider.
2. **Tokens & Sessions**: Tokens have a 2-hour lifetime. Refresh-token rotation and sliding session cookies are deferred to future enterprise hardening.
3. **Notifications**: Notification delivery uses client-side polling. External notification channels (Email, Microsoft Teams, Webhooks) are deferred.
4. **Caching & Multi-Instance**: No distributed caching (Redis) is used in this phase. Database lookups per authenticated request ensure consistency without cache invalidation complexity.
5. **Worker Coordination**: The `SlaNotificationBackgroundService` runs as an in-process `IHostedService`. In a multi-instance deployment, leader election or a distributed scheduler (e.g., Hangfire/Quartz) would be required.
