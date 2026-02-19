FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Hack13.Api/Hack13.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
COPY configs ./configs
EXPOSE 5000
ENTRYPOINT ["dotnet", "Hack13.Api.dll"]
