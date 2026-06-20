FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend-dotnet/Zayra.Api/Zayra.Api.csproj ./
RUN dotnet restore
COPY backend-dotnet/Zayra.Api/ ./
RUN dotnet publish Zayra.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./

# Memory/GC tuning for 512 MB containers (Render free/starter tier).
# GCConserveMemory=9: most aggressive heap trimming after each GC cycle.
# EnableDiagnostics=0: skip diagnostic pipes/sockets (-3 MB baseline).
# GCHeapHardLimit: cap managed heap at 380 MB, leaving headroom for native/stack.
ENV DOTNET_GCConserveMemory=9
ENV DOTNET_EnableDiagnostics=0
ENV DOTNET_GCHeapHardLimit=398458880

EXPOSE 8080
ENTRYPOINT ["dotnet", "Zayra.Api.dll"]
