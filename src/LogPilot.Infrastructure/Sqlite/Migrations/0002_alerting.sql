-- V0.2 告警功能：通知渠道、告警规则、告警事件

CREATE TABLE IF NOT EXISTS lp_alert_channel (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL REFERENCES lp_project(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    type TEXT NOT NULL CHECK (type IN ('dingtalk','wecom','email','webhook')),
    config_json TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    created_by INTEGER NULL REFERENCES lp_user(id) ON DELETE SET NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_alert_channel_project ON lp_alert_channel(project_id, enabled);

CREATE TABLE IF NOT EXISTS lp_alert_rule (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL REFERENCES lp_project(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    enabled INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),

    service_name TEXT NULL,
    environment TEXT NULL,
    levels TEXT NULL,
    keyword TEXT NULL,
    exception_type TEXT NULL,

    condition_type TEXT NOT NULL CHECK (condition_type IN ('count')),
    threshold INTEGER NOT NULL,
    window_minutes INTEGER NOT NULL,
    for_minutes INTEGER NOT NULL DEFAULT 0,

    channel_ids TEXT NOT NULL,
    severity TEXT NOT NULL DEFAULT 'P2' CHECK (severity IN ('P0','P1','P2')),
    cooldown_minutes INTEGER NOT NULL DEFAULT 5,
    notify_recovery INTEGER NOT NULL DEFAULT 1 CHECK (notify_recovery IN (0, 1)),
    repeat_minutes INTEGER NULL,

    created_by INTEGER NULL REFERENCES lp_user(id) ON DELETE SET NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_alert_rule_project ON lp_alert_rule(project_id, enabled);

CREATE TABLE IF NOT EXISTS lp_alert_event (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    rule_id INTEGER NOT NULL REFERENCES lp_alert_rule(id) ON DELETE CASCADE,
    project_id INTEGER NOT NULL,
    fingerprint TEXT NOT NULL,
    status TEXT NOT NULL CHECK (status IN ('firing','acknowledged','resolved','silenced')),
    severity TEXT NOT NULL,
    title TEXT NOT NULL,
    message TEXT NOT NULL,
    sample_logs_json TEXT NULL,
    triggered_at TEXT NOT NULL,
    acknowledged_at TEXT NULL,
    resolved_at TEXT NULL,
    last_notified_at TEXT NULL,
    notify_count INTEGER NOT NULL DEFAULT 0,
    UNIQUE(rule_id, fingerprint)
);
CREATE INDEX IF NOT EXISTS ix_alert_event_status ON lp_alert_event(status, triggered_at);
