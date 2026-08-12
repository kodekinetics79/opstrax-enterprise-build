FROM mcr.microsoft.com/dotnet/sdk:8.0.419-bookworm-slim@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS build
WORKDIR /src
COPY backend-dotnet/Opstrax.Api.csproj backend-dotnet/
COPY tools/PlatformAdminRecovery/PlatformAdminRecovery.csproj tools/PlatformAdminRecovery/
RUN dotnet restore tools/PlatformAdminRecovery/PlatformAdminRecovery.csproj
COPY backend-dotnet/ backend-dotnet/
COPY tools/PlatformAdminRecovery/ tools/PlatformAdminRecovery/
COPY database/init/001_schema.sql database/init/001_schema.sql
RUN dotnet publish tools/PlatformAdminRecovery/PlatformAdminRecovery.csproj -c Release -o /app/recovery --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0.30-bookworm-slim@sha256:a27922704a14d6d1ce103697aa3efa885902db33651b41a84ad1594fdf5be66e AS final
WORKDIR /app
COPY --from=build /app/recovery .
USER $APP_UID
ENTRYPOINT ["dotnet", "PlatformAdminRecovery.dll"]
