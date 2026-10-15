#!/usr/bin/env python3
"""LogPilot HTTP JSON 批量接入示例（仅使用 Python 标准库）。

用法：
    export LOGPILOT_URL=http://127.0.0.1:5080
    export TOKEN=lp_你的接入密钥
    python ingest_demo.py
"""
import json
import os
import sys
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone


def env(name: str, fallback: str) -> str:
    value = os.environ.get(name)
    return value.strip() if value and value.strip() else fallback


def build_batch(service_name: str, count: int) -> dict:
    """构造一批日志；每 5 条包含一条 Error。"""
    trace_id = uuid.uuid4().hex
    logs = []
    for i in range(1, count + 1):
        logs.append(
            {
                "timestamp": datetime.now(timezone.utc)
                .isoformat(timespec="milliseconds")
                .replace("+00:00", "Z"),
                "serviceName": service_name,
                "environment": "dev",
                "level": "Error" if i % 5 == 0 else "Info",
                "message": f"[demo] ({i}/{count}) hello LogPilot from python-ingest",
                "traceId": trace_id,
                "attributes": {"module": "python-ingest", "seq": i},
            }
        )
    return {"logs": logs}


def main() -> int:
    base_url = env("LOGPILOT_URL", "http://127.0.0.1:5080").rstrip("/")
    token = env("TOKEN", "")
    service_name = env("SERVICE_NAME", "python-demo")
    count = int(env("COUNT", "5"))

    if not token:
        print("缺少 TOKEN，请先设置环境变量 TOKEN=lp_xxx", file=sys.stderr)
        return 1

    payload = json.dumps(build_batch(service_name, count)).encode("utf-8")
    request = urllib.request.Request(
        f"{base_url}/api/v1/ingest/logs",
        data=payload,
        method="POST",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
        },
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            print(f"HTTP {response.status}")
            print(json.dumps(json.loads(response.read().decode("utf-8")), ensure_ascii=False, indent=2))
    except urllib.error.HTTPError as exc:
        print(f"HTTP {exc.code}", file=sys.stderr)
        print(exc.read().decode("utf-8", "replace"), file=sys.stderr)
        return 1
    except urllib.error.URLError as exc:
        print(f"请求失败: {exc.reason}", file=sys.stderr)
        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
