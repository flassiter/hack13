FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
COPY certs/ClaytonRootCA.pem /usr/local/share/ca-certificates/ClaytonRootCA.crt
RUN update-ca-certificates
WORKDIR /src
COPY . .
RUN dotnet publish src/Hack13.Api/Hack13.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
COPY certs/ClaytonRootCA.pem /usr/local/share/ca-certificates/ClaytonRootCA.crt
RUN update-ca-certificates
RUN apt-get update && apt-get install -y --no-install-recommends chromium && rm -rf /var/lib/apt/lists/*
ENV RPA_PDF_CHROMIUM_PATH=/usr/bin/chromium
WORKDIR /app
COPY --from=build /app/publish .
COPY configs ./configs
EXPOSE 5000
ENTRYPOINT ["dotnet", "Hack13.Api.dll"]
