# .NET 控制台 → LogPilot 对接演示

向 LogPilot 写入几条测试日志，然后在控制台「日志检索」中查看。

## 前置

1. 本地已启动 LogPilot（以浏览器地址栏为准，例如 `https://localhost:7243`）
2. VictoriaLogs 可达（你已配远程或本机）
3. 在 LogPilot：**项目管理** 建项目 → **接入中心** 生成 Token（`lp_...`，只显示一次）

## 配置 Token

复制示例并填入密钥（该文件已 gitignore，不会进仓库）：

```powershell
copy examples\dotnet-console-ingest\local.settings.json.example examples\dotnet-console-ingest\local.settings.json
# 编辑 local.settings.json 中的 TOKEN / LOGPILOT_URL
```

也可继续用环境变量或 `--token`（优先级更高）。

## 运行（交互式）

```powershell
dotnet run --project examples/dotnet-console-ingest
```

启动后一直监听输入，两种写法：

```text
内容> 用户登录成功
条数> 5

# 或一行：
内容> 10 支付超时
```

其它命令：`zh`（发送 10 条内置中文样例，含中文异常消息与堆栈）/ `level Error` / `mode otlp` / `help` / `quit`

## 中文检索自测

输入 `zh`（或 `中文`）会一次性发送 10 条中文样例日志，覆盖：

- 中文正文（`用户登录成功`、`数据库连接超时`…）
- **中文子串**场景：`连接` 只是 `数据库连接超时` 里的一段
- **异常内容**：2 条带中文 `exceptionMessage` / `exceptionStackTrace`（`未将对象引用设置到对象的实例`、`库存不足：商品编号 SKU-8899`）

发完后在检索页依次试：`超时`、`连接`、`订单`、`库存不足`、`每日对账`，观察哪些能命中、哪些命不中。

## 在平台上查看

1. 打开 `https://localhost:7243/logs`
2. 选项目 →「最近 15 分钟」→ 查询
3. 关键词 `[demo]` 或服务名 `demo-console`

## 说明

| 模式 | 接口 | 用途 |
|---|---|---|
| `json` | `POST /api/v1/ingest/logs` | 快速冒烟、看清 HTTP 响应 |
| `otlp` | `POST /v1/logs` | 模拟真实 .NET / OpenTelemetry SDK 接入 |
