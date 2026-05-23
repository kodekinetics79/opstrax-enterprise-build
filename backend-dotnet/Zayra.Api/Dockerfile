FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Zayra.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish Zayra.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 5117
ENTRYPOINT ["dotnet", "Zayra.Api.dll"]
