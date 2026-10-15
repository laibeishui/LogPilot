# Python HTTP JSON 批量接入示例

仅使用 Python 标准库（3.8+），无第三方依赖。

```bash
export LOGPILOT_URL=http://127.0.0.1:5080
export TOKEN=lp_你的接入密钥
export SERVICE_NAME=python-demo
export COUNT=5
python ingest_demo.py
```

Windows PowerShell：

```powershell
$env:LOGPILOT_URL="http://127.0.0.1:5080"
$env:TOKEN="lp_你的接入密钥"
python .\ingest_demo.py
```

发送成功后会打印 `accepted` / `rejected` 统计，可在「日志检索」页验证。

## 环境变量

| 变量 | 默认值 | 说明 |
|---|---|---|
| `LOGPILOT_URL` | `http://127.0.0.1:5080` | LogPilot 服务地址 |
| `TOKEN` | 无（必填） | 接入 Token，在「接入中心」生成 |
| `SERVICE_NAME` | `python-demo` | 日志的 service.name |
| `COUNT` | `5` | 单批发送条数（服务端上限 500） |
