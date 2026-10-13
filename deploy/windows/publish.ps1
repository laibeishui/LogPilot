# 发布 Windows 可运行目录（framework-dependent，需安装 .NET 8 ASP.NET Runtime）
# 用法：在仓库根目录执行
#   powershell -ExecutionPolicy Bypass -File deploy\windows\publish.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $Root "LogPilot.slnx"))) {
    $Root = (Get-Location).Path
}

$Out = Join-Path $Root "artifacts\win-x64"
New-Item -ItemType Directory -Force -Path $Out | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $Out "data") | Out-Null

Write-Host "Publishing to $Out ..."
dotnet publish (Join-Path $Root "src\LogPilot.Web\LogPilot.Web.csproj") `
    -c Release `
    -o $Out `
    /p:UseAppHost=true

$EnvExample = @"
# 复制为 .env 或在系统中设置环境变量后启动
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
Storage__Sqlite__DataSource=./data/logpilot.db
Storage__VictoriaLogs__BaseUrl=http://127.0.0.1:9428
"@
Set-Content -Path (Join-Path $Out "env.example") -Value $EnvExample -Encoding UTF8

Write-Host "Done. Next:"
Write-Host "  1) 启动 VictoriaLogs: docker compose -f deploy/compose.vl-only.yml up -d"
Write-Host "  2) 运行:       deploy\windows\run.ps1"
