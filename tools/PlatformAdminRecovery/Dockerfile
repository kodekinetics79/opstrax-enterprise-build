FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:3c0edbfe1549dd93fb789dc96299a40df865ad7bffefcaf38e8c05940686d641 AS build
WORKDIR /src
COPY backend-dotnet/Opstrax.Api.csproj backend-dotnet/
COPY tools/PlatformAdminRecovery/PlatformAdminRecovery.csproj tools/PlatformAdminRecovery/
RUN dotnet restore tools/PlatformAdminRecovery/PlatformAdminRecovery.csproj
COPY backend-dotnet/ backend-dotnet/
COPY tools/PlatformAdminRecovery/ tools/PlatformAdminRecovery/
COPY database/init/001_schema.sql database/init/001_schema.sql
RUN dotnet publish tools/PlatformAdminRecovery/PlatformAdminRecovery.csproj -c Release -o /app/recovery --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0@sha256:4d0117b335477ad6c35cfa70c75424e72bbf05645a5d77c1f68a74fdd057676a AS final
WORKDIR /app
COPY --from=build /app/recovery .
USER $APP_UID
ENTRYPOINT ["dotnet", "PlatformAdminRecovery.dll"]
