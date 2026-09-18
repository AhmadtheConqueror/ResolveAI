# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/ResolveAI.Api/ResolveAI.Api.csproj backend/ResolveAI.Api/
RUN dotnet restore backend/ResolveAI.Api/ResolveAI.Api.csproj

COPY backend/ResolveAI.Api/ backend/ResolveAI.Api/
RUN dotnet publish backend/ResolveAI.Api/ResolveAI.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0

EXPOSE 10000

COPY --from=build /app/publish .

ENTRYPOINT ["sh", "-c", "exec dotnet ResolveAI.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
