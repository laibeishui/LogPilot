# 前台运行已发布的 LogPilot（Windows）
# 用法：
#   1) 先 publish.ps1
#   2) 确保 VictoriaLogs 在 9428 可访问（compose.vl-only 或本机二进制）
#   3) powershell -ExecutionPolicy Bypass -File deploy\windows\run.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$AppDir = Join-Path $Root "artifacts\win-x64"
$Dll = Join-Path $AppDir "LogPilot.Web.dll"

if (-not (Test-Path $Dll)) {
    Write-Error "未找到 $Dll ，请先运行 deploy\windows\publish.ps1"
}

New-Item -ItemType Directory -Force -Path (Join-Path $AppDir "data") | Out-Null
Set-Location $AppDir

if (-not $env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT = "Production" }
if (-not $env:ASPNETCORE_URLS) { $env:ASPNETCORE_URLS = "http://0.0.0.0:8080" }
if (-not $env:Storage__Sqlite__DataSource) { $env:Storage__Sqlite__DataSource = "./data/logpilot.db" }
if (-not $env:Storage__VictoriaLogs__BaseUrl) { $env:Storage__VictoriaLogs__BaseUrl = "http://127.0.0.1:9428" }

Write-Host "LogPilot listening on $($env:ASPNETCORE_URLS)"
Write-Host "VictoriaLogs -> $($env:Storage__VictoriaLogs__BaseUrl)"
dotnet .\LogPilot.Web.dll
