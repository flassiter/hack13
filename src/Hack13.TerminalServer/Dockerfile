FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
COPY certs/ClaytonRootCA.pem /usr/local/share/ca-certificates/ClaytonRootCA.crt
RUN update-ca-certificates
WORKDIR /src
COPY . .
RUN dotnet publish src/Hack13.TerminalServer/Hack13.TerminalServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY certs/ClaytonRootCA.pem /usr/local/share/ca-certificates/ClaytonRootCA.crt
RUN update-ca-certificates
WORKDIR /app
COPY --from=build /app/publish .
COPY configs ./configs
COPY test-data ./test-data
EXPOSE 5250
ENTRYPOINT ["dotnet", "Hack13.TerminalServer.dll", "/app", "5250"]
