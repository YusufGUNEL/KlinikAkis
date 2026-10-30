# syntax=docker/dockerfile:1
# Tek imaj: Angular derlemesi + .NET API + SignalR hub aynı origin'de (SPEC §3, §12).

# 1) Angular
FROM node:24-bookworm-slim AS web
WORKDIR /src/web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci
COPY src/web/ ./
# PrimeUI lisansı: Render ortam değişkenlerini aynı adla build argümanı olarak verir (SPEC §12).
# Anahtar zaten tarayıcıya giden JS'e gömülür; yalnızca bu ara aşamada kalır, son imajın geçmişine girmez.
# Yoksa derleme sürer, sayfada lisans uyarısı görünür.
ARG PRIMEUI_LICENSE
RUN npm run build

# 2) .NET publish
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS api
WORKDIR /src
COPY Directory.Build.props KlinikAkis.slnx ./
COPY src/KlinikAkis.Domain/KlinikAkis.Domain.csproj src/KlinikAkis.Domain/
COPY src/KlinikAkis.Infrastructure/KlinikAkis.Infrastructure.csproj src/KlinikAkis.Infrastructure/
COPY src/KlinikAkis.Api/KlinikAkis.Api.csproj src/KlinikAkis.Api/
RUN dotnet restore src/KlinikAkis.Api/KlinikAkis.Api.csproj
COPY src/KlinikAkis.Domain/ src/KlinikAkis.Domain/
COPY src/KlinikAkis.Infrastructure/ src/KlinikAkis.Infrastructure/
COPY src/KlinikAkis.Api/ src/KlinikAkis.Api/
RUN dotnet publish src/KlinikAkis.Api/KlinikAkis.Api.csproj -c Release -o /app --no-restore

# 3) Çalışma imajı: Ubuntu (ICU + tzdata içerir; Alpine değil — tr-TR ve Europe/Istanbul gerekir, SPEC §10.5)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/web/dist/web/browser ./wwwroot
# Render'ın ters vekili X-Forwarded-For/Proto gönderir; vekil IP'si değişken olduğu için listeler temizlenir.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_ENVIRONMENT=Production
USER app
# Port: Program.cs PORT değişkenini okur (Render varsayılanı 10000); yoksa imajın 8080'i.
EXPOSE 8080
ENTRYPOINT ["dotnet", "KlinikAkis.Api.dll"]
