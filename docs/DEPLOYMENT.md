# First Production Deployment

The first deployment uses manually created Render PostgreSQL, a Render Docker web service, and a Vercel Vite project. Do not enable automatic migrations on API startup.

## 1. Create Render PostgreSQL

Create a PostgreSQL database in Render. Keep the database and web service in the same region. Save the internal host, database name, user, password, and the external connection details for the one-time migration and Admin setup. Restrict external database access to the operator's address while it is needed.

Use a normal Npgsql key/value connection string for the API:

```text
Host=<internal-host>;Port=5432;Database=<database>;Username=<user>;Password=<password>;SSL Mode=Require
```

Use the external host when connecting from your workstation. Do not paste either connection string into source control or Vercel.

## 2. Create Render Docker Web Service

Connect the repository in Render and create a **Docker** web service manually. Use the repository root as the build context and `backend/ResolveAI.Api/Dockerfile` as the Dockerfile path. The image publishes the .NET 10 API in Release mode, runs the ASP.NET Core runtime image in Production, and binds to `0.0.0.0:${PORT:-10000}`. Set the health check path to `/health`.

Set these Render environment variables before the first deploy:

```text
ConnectionStrings__DefaultConnection=<Npgsql key/value connection string using the internal host>
Jwt__Key=<secure random value of at least 32 characters>
Jwt__Issuer=ResolveAI.Api
Jwt__Audience=ResolveAI.Web
Features__AllowPublicRegistration=false
AI__Provider=Gemini
AI__Model=gemini-2.5-flash
AI__ApiKey=<Google AI API key>
ExternalNotifications__EmailEnabled=false
ExternalNotifications__OverrideRecipient=
Resend__ApiKey=<Resend API key, when email is enabled>
Resend__FromEmail=<verified sender address, when email is enabled>
```

The image sets `ASPNETCORE_ENVIRONMENT=Production`. Render supplies `PORT`; the fallback is `10000`. Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`.

### Forwarded headers

The API processes one `X-Forwarded-For` and one `X-Forwarded-Proto` value only when the immediate proxy IP is trusted. The loopback proxy defaults remain in place. For Render, set `ForwardedHeaders__KnownProxies` to a comma-separated list of the **immediate Render proxy IP addresses** observed for this service or confirmed by Render support. Include the address format seen by Kestrel (IPv4 or IPv4-mapped IPv6), and update the list when Render changes the proxy address. Do not use public client IPs here. Until the actual proxy is listed, forwarded headers are ignored and `RemoteIpAddress` / `Request.Scheme` reflect the container connection. In particular, login rate limits may group requests by the proxy address. Do not open this trust list to all peers: the API relies on the proxy as the only path to its container port, and the list limits who can change client IP and scheme. [ASP.NET Core guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) explains the trust restriction and right-to-left processing.

## 3. Apply Migrations Deliberately

After PostgreSQL exists and before user traffic, generate an idempotent EF Core SQL script from the checked-out release, review it, and apply it to the **external** Render database endpoint from an authorized workstation. The local .NET SDK and `dotnet-ef` tool must be installed. Set `ASPNETCORE_ENVIRONMENT=Production` and provide `Jwt__Key`, `Jwt__Issuer`, and `Jwt__Audience` in the operator's environment because EF tooling constructs the API host. Use the external connection details supplied by Render for `psql`; keep credentials outside shell history and source control.

```powershell
dotnet ef migrations script --idempotent --project backend/ResolveAI.Api/ResolveAI.Api.csproj --startup-project backend/ResolveAI.Api/ResolveAI.Api.csproj --output resolveai-migrations.sql
```

Review `resolveai-migrations.sql` before running it with `psql` against the intended database. Remove the generated script after use; do not commit it. Confirm `__EFMigrationsHistory` contains the expected latest migration. Neither normal API startup nor the Docker image applies migrations.

## 4. Create The Initial Admin

There is no Admin bootstrap command. For the first account only, temporarily set `Features__AllowPublicRegistration=true` on Render and redeploy. Register the intended Admin email through `POST /api/auth/register` with `firstName`, `lastName`, `email`, and a strong `password`; registration initially creates an **Employee**. In the database, promote exactly that account:

```sql
UPDATE "Users"
SET "RoleId" = (SELECT "Id" FROM "Roles" WHERE "Name" = 'Admin')
WHERE "Email" = '<admin-email>' AND "RoleId" = (SELECT "Id" FROM "Roles" WHERE "Name" = 'Employee');
```

Check that exactly one row changed. Immediately set `Features__AllowPublicRegistration=false`, redeploy, and verify registration returns `403`. Verify the Admin can sign in. Keep the temporary registration window short and monitor for unexpected accounts; remove any unauthorized registrations.

## 5. Verify The API

Check `https://<render-host>/health` returns `200` and `https://<render-host>/health/ready` returns `200` after migrations. If readiness fails, inspect the database settings and Render logs. Verify TLS requests have the expected scheme and distinct client IPs in server diagnostics before relying on IP-based rate limits.

## 6. Deploy The Vercel Frontend

Create a Vercel project manually with root directory `frontend/resolveai-web` and the Vite framework. `vercel.json` builds `dist` and rewrites SPA routes to `index.html`. Set the Production build variable:

```text
VITE_API_URL=https://<render-host>
```

This URL is public in the browser bundle. Never put JWT, database, Gemini, or Resend secrets in any `VITE_*` variable. API calls go directly to Render; Vercel does not proxy them.

## 7. Update Frontend Origins

After Vercel assigns the production hostname, set these Render variables to its exact HTTPS origin (no path or trailing slash) and redeploy the API:

```text
Frontend__BaseUrl=https://<vercel-host>
Frontend__AllowedOrigins=https://<vercel-host>
```

For multiple production origins, separate `Frontend__AllowedOrigins` values with commas. Confirm browser requests from the Vercel site succeed and unlisted origins are rejected.

## 8. Verify Integrations And Smoke Test

As an Admin or Manager, trigger a Gemini analysis and confirm a valid result. For Resend, first verify the sending domain, `Resend__ApiKey`, and `Resend__FromEmail`; then set `ExternalNotifications__EmailEnabled=true` and redeploy. Trigger a qualifying notification and inspect `GET /api/admin/external-notifications` for delivery status. Keep email disabled until its settings are valid.

Test sign-in, role-gated navigation, incident creation and assignment, `/dashboard`, `/incidents`, `/incidents/<id>`, `/my-work`, `/analytics`, and `/users` by directly refreshing each route. Recheck `/health` and `/health/ready`, browser network errors, and Render logs. Use an appropriate account for each protected route.

## Local Verification

```powershell
dotnet run --project backend/ResolveAI.Api.Tests
dotnet build -c Release backend/ResolveAI.Api/ResolveAI.Api.csproj
```

In `frontend/resolveai-web`, run `npm test`, `npm run lint`, and `npm run build`.
