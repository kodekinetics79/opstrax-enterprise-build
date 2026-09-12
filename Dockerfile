FROM mcr.microsoft.com/dotnet/sdk:8.0.419-bookworm-slim@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS build
WORKDIR /src
COPY backend-dotnet/Opstrax.Api.csproj backend-dotnet/
COPY telematics/src/Opstrax.Telematics.Contracts/Opstrax.Telematics.Contracts.csproj telematics/src/Opstrax.Telematics.Contracts/
COPY telematics/src/Opstrax.Telematics.Protocols.J1939/Opstrax.Telematics.Protocols.J1939.csproj telematics/src/Opstrax.Telematics.Protocols.J1939/
RUN dotnet restore backend-dotnet/Opstrax.Api.csproj
COPY backend-dotnet/ backend-dotnet/
COPY telematics/src/Opstrax.Telematics.Contracts/ telematics/src/Opstrax.Telematics.Contracts/
COPY telematics/src/Opstrax.Telematics.Protocols.J1939/ telematics/src/Opstrax.Telematics.Protocols.J1939/
COPY database/init/001_schema.sql database/init/001_schema.sql
# Render builds this root Dockerfile. Package the complete migration tree so the
# live artifact cannot drift behind the canonical predeploy runner.
COPY database/migrations/ database/migrations/
RUN dotnet publish backend-dotnet/Opstrax.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0.30-bookworm-slim@sha256:b0beb9cc1dee1c1b0749796110d4734292071b814207ad0d4f40611f7db04f7b AS final
# Apply Debian's PCRE2 security fix while retaining the pinned runtime base.
RUN apt-get update \
    && apt-get install -y --no-install-recommends --only-upgrade libpcre2-8-0 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' libpcre2-8-0)" ge '10.42-1+deb12u1' \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/database/migrations ./Migrations
EXPOSE 10000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
USER $APP_UID
ENTRYPOINT ["dotnet", "Opstrax.Api.dll"]
