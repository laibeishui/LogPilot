# 使用 Windows 服务包装运行 LogPilot（需要管理员权限）
# 依赖：已安装 .NET 8 ASP.NET Core Runtime；已执行 publish.ps1
# 可选：安装 NSSM 后更稳妥。本脚本优先尝试 sc.exe + 简易启动器。
#
# 推荐生产做法：
#   1) IIS 反向代理到 Kestrel，或
#   2) 使用 nssm install LogPilot "C:\Program Files\dotnet\dotnet.exe" "C:\logpilot\LogPilot.Web.dll"
#
# 本脚本创建任务计划「开机启动」作为轻量替代（无需额外工具）：

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$AppDir = Join-Path $Root "artifacts\win-x64"
$RunPs1 = Join-Path $PSScriptRoot "run.ps1"

if (-not (Test-Path (Join-Path $AppDir "LogPilot.Web.dll"))) {
    Write-Error "请先运行 publish.ps1"
}

$TaskName = "LogPilot"
$Action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$RunPs1`""
$Trigger = New-ScheduledTaskTrigger -AtStartup
$Principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$Settings = New-ScheduledTaskSettingsSet -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)

Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings | Out-Null

Write-Host "已注册计划任务 '$TaskName'（开机启动）。"
Write-Host "立即启动: Start-ScheduledTask -TaskName $TaskName"
Write-Host "卸载:     Unregister-ScheduledTask -TaskName $TaskName -Confirm:`$false"
Write-Host ""
Write-Host "若需正式 Windows Service，建议使用 NSSM："
Write-Host "  nssm install LogPilot `"$([Environment]::GetEnvironmentVariable('ProgramFiles'))\dotnet\dotnet.exe`" `"$AppDir\LogPilot.Web.dll`""
Write-Host "  nssm set LogPilot AppDirectory `"$AppDir`""
Write-Host "  nssm set LogPilot AppEnvironmentExtra ASPNETCORE_URLS=http://0.0.0.0:8080 Storage__VictoriaLogs__BaseUrl=http://127.0.0.1:9428 Storage__Sqlite__DataSource=./data/logpilot.db"
