# Java HTTP JSON 批量接入示例

单文件源码，JDK 11+ 直接运行，无需 Maven / Gradle 或第三方依赖。

```bash
export LOGPILOT_URL=http://127.0.0.1:5080
export TOKEN=lp_你的接入密钥
java IngestDemo.java
```

Windows PowerShell：

```powershell
$env:LOGPILOT_URL="http://127.0.0.1:5080"
$env:TOKEN="lp_你的接入密钥"
java .\IngestDemo.java
```

发送成功后会打印响应体中的 `accepted` / `rejected` 统计，可在「日志检索」页验证。

## 环境变量

| 变量 | 默认值 | 说明 |
|---|---|---|
| `LOGPILOT_URL` | `http://127.0.0.1:5080` | LogPilot 服务地址 |
| `TOKEN` | 无（必填） | 接入 Token，在「接入中心」生成 |
| `SERVICE_NAME` | `java-demo` | 日志的 service.name |
| `COUNT` | `5` | 单批发送条数（服务端上限 500） |

> 若需使用 OpenTelemetry SDK（Java）自动采集，可将日志导出器指向 `${LOGPILOT_URL}/v1/logs`
> 并将协议设为 `http/protobuf`、鉴权 Header 设为 `Authorization=Bearer <TOKEN>`。
