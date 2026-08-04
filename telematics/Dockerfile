FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:3c0edbfe1549dd93fb789dc96299a40df865ad7bffefcaf38e8c05940686d641 AS build
WORKDIR /src

COPY telematics/src/Opstrax.Telematics.Contracts/Opstrax.Telematics.Contracts.csproj telematics/src/Opstrax.Telematics.Contracts/
COPY telematics/src/Opstrax.Telematics.Protocols.Gt06/Opstrax.Telematics.Protocols.Gt06.csproj telematics/src/Opstrax.Telematics.Protocols.Gt06/
COPY telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj telematics/src/Opstrax.Telematics.Gateway/
RUN dotnet restore telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj

COPY telematics/src/ telematics/src/
RUN dotnet publish telematics/src/Opstrax.Telematics.Gateway/Opstrax.Telematics.Gateway.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0@sha256:4d0117b335477ad6c35cfa70c75424e72bbf05645a5d77c1f68a74fdd057676a AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV DOTNET_ENVIRONMENT=Production \
    Gateway__ListenAddress=0.0.0.0 \
    Gateway__ListenPort=5023
EXPOSE 5023/tcp
USER $APP_UID
ENTRYPOINT ["dotnet", "Opstrax.Telematics.Gateway.dll"]
