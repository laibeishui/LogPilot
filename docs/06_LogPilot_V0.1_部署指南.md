# LogPilot V0.1 部署指南

> 文档编号：06｜日期：2026-10-13  
> 目标：同一套代码同时支持 **Docker 全栈**、**Windows 本机**、**Linux 本机**。

## 1. 先认清架构（避免走弯路）

LogPilot 运行期固定需要 **两个组件**：

| 组件 | 作用 | 跨平台说明 |
|---|---|---|
| **LogPilot** | Web / API / SQLite 元数据 | .NET 8，原生支持 Windows / Linux / 容器 |
| **VictoriaLogs** | 日志存储与检索 | 官方镜像以 Linux 为主；Windows 上优先用 Docker/WSL 跑 VL |

因此推荐三选一：

1. **Docker 全栈**（最省事，Windows / Linux / macOS 都适用）  
2. **本机 LogPilot + Docker 只跑 VictoriaLogs**（开发/混合部署最常见）  
3. **Linux 全本机**：LogPilot systemd + VictoriaLogs 二进制/容器  

> Windows 上不建议强行找 VictoriaLogs 原生 exe文件；用 Docker Desktop / WSL2 跑 VL 更稳。

所有配置均可通过环境变量覆盖，嵌套配置用双下划线，例如：

```text
Storage__Sqlite__DataSource=./data/logpilot.db
Storage__VictoriaLogs__BaseUrl=http://127.0.0.1:9428
ASPNETCORE_URLS=http://0.0.0.0:8080
```

## 2. 路径 A：Docker 全栈（推荐）

### 前置

- Docker Engine + Compose v2  
- 开放宿主机端口（默认 `8080`）

### 步骤

```bash
cp deploy/.env.example deploy/.env
# 按需编辑端口与数据目录

docker compose -f deploy/compose.yml --env-file deploy/.env up -d --build
```

访问：

- 控制台：`http://localhost:8080`  
- 存活：`http://localhost:8080/health/live`  
- 就绪：`http://localhost:8080/health/ready`

首次打开会进入 `/setup` 创建管理员。

### 数据与升级

- SQLite：卷 `LOGPILOT_DATA_PATH`（默认命名卷 `logpilot-data`）  
- VictoriaLogs：卷 `VICTORIALOGS_DATA_PATH`  
- 升级：改镜像 tag / 重新 `build` 后 `up -d`；**先备份卷**  
- 停止：`docker compose -f deploy/compose.yml down`（加 `-v` 会删数据，慎用）

### 安全提醒

- 生产请在前面加 HTTPS 反向代理（Caddy / Nginx / IIS）  
- 不要把 VictoriaLogs `9428` 直接暴露公网（全栈 compose 默认不映射）

## 3. 路径 B：Windows 本机 LogPilot + Docker VictoriaLogs

适合：本机开发、公司 Windows 服务器已装 .NET、但用容器跑 VL。

### B1. 启动 VictoriaLogs

```powershell
copy deploy\.env.example deploy\.env
docker compose -f deploy\compose.vl-only.yml --env-file deploy\.env up -d
```

确认：`http://127.0.0.1:9428/` 可访问。

### B2. 发布并运行 LogPilot

```powershell
# 需安装 .NET 8 SDK（发布）+ ASP.NET Core 8 Runtime（运行机器）
powershell -ExecutionPolicy Bypass -File deploy\windows\publish.ps1
powershell -ExecutionPolicy Bypass -File deploy\windows\run.ps1
```

浏览器：`http://127.0.0.1:8080`

### B3. 开机自启（可选）

管理员 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File deploy\windows\install-service.ps1
Start-ScheduledTask -TaskName LogPilot
```

正式 Windows Service 可用 NSSM（脚本内有示例命令）。

## 4. 路径 C：Linux 本机 LogPilot +（容器或本机）VictoriaLogs

### C1. VictoriaLogs

容器方式：

```bash
cp deploy/.env.example deploy/.env
docker compose -f deploy/compose.vl-only.yml --env-file deploy/.env up -d
```

或按 VictoriaLogs 官方文档安装二进制，监听 `9428`。

### C2. 发布 LogPilot

```bash
# 安装 aspnetcore-runtime-8.0 与（构建机）dotnet-sdk-8.0
bash deploy/linux/publish.sh
bash deploy/linux/run.sh
```

### C3. systemd（生产）

```bash
sudo bash deploy/linux/install-service.sh
curl -s http://127.0.0.1:8080/health/live
journalctl -u logpilot -f
```

服务单元：`deploy/linux/logpilot.service`（默认安装到 `/opt/logpilot`）。

## 5. 首次验收清单（三条路径通用）

1. `/setup` 创建管理员，不可重复初始化  
2. 创建项目 → 接入中心生成 Token（只显示一次）  
3. 发送测试日志（见 `examples/curl-json`）  
4. `/logs` 按项目与最近 15 分钟能查到  
5. 停掉 VictoriaLogs 后再写入 → **不得假成功**（应 503/失败）  
6. 重启 LogPilot 后，项目/账号仍在（SQLite 持久化目录正确）

## 6. 常见问题

| 现象 | 原因 | 处理 |
|---|---|---|
| `/health/ready` 503 | VictoriaLogs 未启动或地址错误 | 检查 `Storage__VictoriaLogs__BaseUrl` |
| 写入 401 | Token 错/已撤销/项目停用 | 重新生成 Token |
| Windows 找不到 docker | 未装 Docker Desktop | 装 Docker，或改用远程 VL 地址 |
| SQLite 只读/丢数据 | 数据目录未持久化 | 检查卷或 `./data` 路径权限 |
| 反向代理登录异常 | Cookie Secure / 路径 | 配置 HTTPS 与转发头（后续可加 ForwardedHeaders） |

## 7. 配置速查

| 变量 | 含义 | 默认示例 |
|---|---|---|
| `ASPNETCORE_URLS` | 监听地址 | `http://0.0.0.0:8080` |
| `Storage__Sqlite__DataSource` | SQLite 文件 | `./data/logpilot.db` 或 `/app/data/logpilot.db` |
| `Storage__VictoriaLogs__BaseUrl` | VL 地址 | 容器内 `http://victorialogs:9428`；本机 `http://127.0.0.1:9428` |
| `Ingestion__MaxBatchRecords` | 单批条数 | `500` |
| `Query__MaxRangeHours` | 最大查询窗 | `24` |
