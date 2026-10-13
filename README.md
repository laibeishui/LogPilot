# LogPilot

轻量、易部署、易使用的自托管日志中心。  
运行期两个组件：**LogPilot**（.NET 8）+ **VictoriaLogs**（日志引擎）。

支持三种部署方式（详见 [`docs/06_LogPilot_V0.1_部署指南.md`](docs/06_LogPilot_V0.1_部署指南.md)）：

| 方式 | 适用 | 一句话 |
|---|---|---|
| **Docker 全栈** | Win / Linux / macOS | 一条 Compose 起两个容器 |
| **Windows 本机** | Windows Server / 开发机 | `publish.ps1` + Docker 只跑 VL |
| **Linux 本机** | 服务器 | `publish.sh` + systemd，VL 用容器或二进制 |

## 技术栈

- ASP.NET Core 8 MVC + Web API（Razor + 渐进式前端）
- SQLite + Dapper（元数据）
- VictoriaLogs（业务日志）
- 接入：OTLP/HTTP Protobuf（主）+ HTTP JSON 批量（辅）

## 快速开始

### A. Docker 全栈（推荐）

```bash
cp deploy/.env.example deploy/.env
docker compose -f deploy/compose.yml --env-file deploy/.env up -d --build
```

打开 `http://localhost:8080` → 首次 `/setup` 创建管理员。

### B. Windows 本机

```powershell
# 1) VictoriaLogs（需 Docker）
copy deploy\.env.example deploy\.env
docker compose -f deploy\compose.vl-only.yml --env-file deploy\.env up -d

# 2) LogPilot
powershell -ExecutionPolicy Bypass -File deploy\windows\publish.ps1
powershell -ExecutionPolicy Bypass -File deploy\windows\run.ps1
```

### C. Linux 本机

```bash
cp deploy/.env.example deploy/.env
docker compose -f deploy/compose.vl-only.yml --env-file deploy/.env up -d   # 或自建 VL

bash deploy/linux/publish.sh
bash deploy/linux/run.sh
# 生产：sudo bash deploy/linux/install-service.sh
```

## 本地开发

```bash
dotnet restore LogPilot.slnx
dotnet build LogPilot.slnx
dotnet test LogPilot.slnx
dotnet run --project src/LogPilot.Web --urls http://127.0.0.1:5088
```

开发默认 SQLite：`./data/logpilot.dev.db`；VictoriaLogs 默认 `http://localhost:9428`。

## 写入一条测试日志

1. 登录控制台 → 创建项目 → 接入中心生成 Token  
2. 使用示例：

```bash
# 见 examples/curl-json
export LOGPILOT_URL=http://127.0.0.1:8080
export TOKEN=lp_你的密钥
curl -X POST "$LOGPILOT_URL/api/v1/ingest/logs" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d @examples/curl-json/sample.json
```

3. 打开「日志检索」查看。

## 仓库结构

```text
src/LogPilot.Web            # 唯一 Web 进程
src/LogPilot.Application    # 用例
src/LogPilot.Core           # 模型与抽象
src/LogPilot.Infrastructure # SQLite / VictoriaLogs / OTLP
tests/                      # 单元与集成测试
deploy/                     # Docker / Windows / Linux 部署
examples/                   # 接入示例
docs/                       # 产品与设计文档
```

## 实施阶段

| 阶段 | 内容 | 状态 |
|---|---|---|
| M1 | 工程骨架、健康检查 | 完成 |
| M2 | 初始化、登录、项目、API Key | 完成 |
| M3 | JSON / OTLP 写入 | 完成 |
| M4 | 日志检索与详情 | 完成 |
| M5 | 多平台部署（Docker / Win / Linux） | 完成 |

## 文档

- [部署指南](docs/06_LogPilot_V0.1_部署指南.md)
- [PRD](docs/01_LogPilot_V0.1_产品需求文档_PRD.md)
- [数据库设计](docs/02_LogPilot_V0.1_SQLite数据库与日志数据模型设计.md)
- [API 设计](docs/03_LogPilot_V0.1_API接口设计.md)
- [开发规范](docs/04_LogPilot_V0.1_项目结构与开发规范.md)
- [UI 设计](docs/05_LogPilot_V0.1_UI页面详细设计.md)

## 许可证

计划采用 Apache-2.0（正式开源前再确认依赖授权）。
