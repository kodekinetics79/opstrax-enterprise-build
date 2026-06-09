FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Zayra.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish Zayra.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./
# PORT is injected at runtime by Railway; 8080 is the Railway default
EXPOSE 8080
ENTRYPOINT ["dotnet", "Zayra.Api.dll"]
