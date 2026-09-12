FROM mcr.microsoft.com/dotnet/sdk:8.0.419-bookworm-slim@sha256:dd09bcce84d9130e7f3e85c83a8ce9709e1d95e45c48c722a0d7923b38d8024c AS build
WORKDIR /src

COPY telematics/src/Opstrax.Telematics.Contracts/Opstrax.Telematics.Contracts.csproj telematics/src/Opstrax.Telematics.Contracts/
COPY telematics/src/Opstrax.Telematics.Protocols.Gt06/Opstrax.Telematics.Protocols.Gt06.csproj telematics/src/Opstrax.Telematics.Protocols.Gt06/
COPY telematics/src/Opstrax.Telematics.Protocols.J1939/Opstrax.Telematics.Protocols.J1939.csproj telematics/src/Opstrax.Telematics.Protocols.J1939/
COPY telematics/src/Opstrax.Telematics.Protocols.PacificTrack/Opstrax.Telematics.Protocols.PacificTrack.csproj telematics/src/Opstrax.Telematics.Protocols.PacificTrack/
COPY telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj telematics/src/Opstrax.Telematics.Gateway/
RUN dotnet restore telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj

COPY telematics/src/ telematics/src/
RUN dotnet publish telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0.30-bookworm-slim@sha256:a27922704a14d6d1ce103697aa3efa885902db33651b41a84ad1594fdf5be66e AS final
# Apply Debian's PCRE2 security fix while retaining the pinned runtime base.
RUN apt-get update \
    && apt-get install -y --no-install-recommends --only-upgrade libpcre2-8-0 \
    && dpkg --compare-versions "$(dpkg-query -W -f='${Version}' libpcre2-8-0)" ge '10.42-1+deb12u1' \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV DOTNET_ENVIRONMENT=Production \
    Gateway__ListenAddress=0.0.0.0 \
    Gateway__ListenPort=5023
EXPOSE 5023/tcp
USER $APP_UID
ENTRYPOINT ["dotnet", "Opstrax.Telematics.Gateway.dll"]
