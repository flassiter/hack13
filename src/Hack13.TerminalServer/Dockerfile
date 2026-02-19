FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/RpaPoc.MockServer/RpaPoc.MockServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
COPY configs ./configs
COPY test-data ./test-data
EXPOSE 5250
ENTRYPOINT ["dotnet", "RpaPoc.MockServer.dll", "/app", "5250"]
