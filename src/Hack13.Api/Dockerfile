FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/RpaPoc.Api/RpaPoc.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
COPY configs ./configs
EXPOSE 5000
ENTRYPOINT ["dotnet", "RpaPoc.Api.dll"]
