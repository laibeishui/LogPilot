# LogPilot 多阶段构建
# 构建： docker build -f deploy/Dockerfile -t logpilot:local .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nuget.config ./
COPY global.json ./
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY LogPilot.slnx ./
COPY src/ ./src/

RUN dotnet restore src/LogPilot.Web/LogPilot.Web.csproj
RUN dotnet publish src/LogPilot.Web/LogPilot.Web.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN mkdir -p /app/data \
    && chown -R app:app /app

USER app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
ENV Storage__Sqlite__DataSource=/app/data/logpilot.db
ENV Storage__VictoriaLogs__BaseUrl=http://victorialogs:9428

EXPOSE 8080

ENTRYPOINT ["dotnet", "LogPilot.Web.dll"]
