FROM mcr.microsoft.com/dotnet/sdk:8.0.419-bookworm-slim@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS build
WORKDIR /src
COPY backend-dotnet/Opstrax.Api.csproj backend-dotnet/
COPY telematics/src/Opstrax.Telematics.Protocols.J1939/Opstrax.Telematics.Protocols.J1939.csproj telematics/src/Opstrax.Telematics.Protocols.J1939/
RUN dotnet restore backend-dotnet/Opstrax.Api.csproj
COPY backend-dotnet/ backend-dotnet/
COPY telematics/src/Opstrax.Telematics.Protocols.J1939/ telematics/src/Opstrax.Telematics.Protocols.J1939/
COPY database/init/001_schema.sql database/init/001_schema.sql
# Package the WHOLE migrations directory. The previous per-file COPY allowlist had
# already fallen behind reality (it stopped at stage82, silently dropping
# stage83/84/85 and everything after from the image's /app/Migrations payload).
# A directory COPY cannot drift; .dockerignore excludes nothing under database/.
COPY database/migrations/ database/migrations/
RUN dotnet publish backend-dotnet/Opstrax.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0.30-bookworm-slim@sha256:b0beb9cc1dee1c1b0749796110d4734292071b814207ad0d4f40611f7db04f7b AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/database/migrations ./Migrations
ENV Rls__EnforceTenantContext=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Opstrax.Api.dll"]
