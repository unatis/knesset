# Render не поддерживает .NET нативно — деплой идёт через Docker.
# Образы Debian-based (не Alpine): в них есть ICU, без которой не работают
# культуры he/ar/ru и сравнение строк в PostgreSQL-запросах.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только csproj — слой restore переиспользуется, пока не менялись зависимости.
COPY Kneset.slnx ./
COPY src/Kneset.Core/Kneset.Core.csproj src/Kneset.Core/
COPY src/Kneset.Infrastructure/Kneset.Infrastructure.csproj src/Kneset.Infrastructure/
COPY src/Kneset.Web/Kneset.Web.csproj src/Kneset.Web/
RUN dotnet restore src/Kneset.Web/Kneset.Web.csproj

COPY . .
RUN dotnet publish src/Kneset.Web/Kneset.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

# Render передаёт порт в переменной PORT и ждёт, что сервис слушает 0.0.0.0.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet Kneset.Web.dll"]
