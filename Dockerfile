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

# Публикация идёт с восстановлением, хотя слой restore выше уже прогрел кэш
# пакетов. Флага --no-restore здесь раньше не было зря: восстанавливался один
# Kneset.Web.csproj, а публиковался полный исходник, и на этом стыке статика,
# приходящая из пакетов, терялась молча — в образе не оказывалось каталога
# wwwroot/_framework со скриптом Blazor. Кэш никуда не делся, лишняя минута
# сборки дешевле неработающего сайта.
RUN dotnet publish src/Kneset.Web/Kneset.Web.csproj -c Release -o /app

# Без blazor.web.js страница отрисовывается на сервере и выглядит целой, но
# цепь SignalR не поднимается: ни один фильтр, ни одна кнопка не отвечают.
# Отказ при этом тихий — на сборке всё зелено, 404 виден только в консоли
# браузера. Поэтому проверяем здесь: пусть лучше упадёт сборка.
RUN set -e; \
    if [ ! -f /app/wwwroot/_framework/blazor.web.js ] \
       || [ ! -f /app/Kneset.Web.staticwebassets.endpoints.json ]; then \
        echo "СБОРКА ОСТАНОВЛЕНА: публикация не положила статику Blazor." >&2; \
        echo "Ожидались wwwroot/_framework/blazor.web.js и манифест endpoints.json." >&2; \
        ls -la /app /app/wwwroot >&2 || true; \
        exit 1; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_ENVIRONMENT=Production

# По умолчанию хост следит за appsettings.json через inotify, чтобы подхватывать
# правки на лету. В контейнере файлы не меняются за время его жизни, а лимит
# inotify-наблюдателей на хосте общий и на маленьких инстансах быстро исчерпан —
# приложение падало при старте с «user limit (128) on the number of inotify
# instances has been reached».
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false

EXPOSE 10000

# Render передаёт порт в переменной PORT и ждёт, что сервис слушает 0.0.0.0.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet Kneset.Web.dll"]
