# HTTP JSON 批量接入示例

```bash
export LOGPILOT_URL=http://127.0.0.1:8080
export TOKEN=lp_你的接入密钥

curl -sS -X POST "${LOGPILOT_URL}/api/v1/ingest/logs" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Content-Type: application/json" \
  -d @sample.json
```

Windows PowerShell：

```powershell
$env:LOGPILOT_URL="http://127.0.0.1:8080"
$env:TOKEN="lp_你的接入密钥"
Invoke-RestMethod -Method Post -Uri "$env:LOGPILOT_URL/api/v1/ingest/logs" `
  -Headers @{ Authorization = "Bearer $env:TOKEN" } `
  -ContentType "application/json" `
  -InFile ".\sample.json"
```
