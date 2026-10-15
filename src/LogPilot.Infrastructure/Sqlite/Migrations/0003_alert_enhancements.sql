-- @no-transaction
-- 告警增强：condition_type 支持 'ratio'，并新增静默期字段 silence_until。
-- SQLite 无法直接修改 CHECK 约束，需按官方推荐流程重建表；重建期间必须关闭外键，
-- 而 PRAGMA foreign_keys 在事务内无效，因此本迁移标记为 @no-transaction 并自行控制事务。

PRAGMA foreign_keys = OFF;

BEGIN;

CREATE TABLE lp_alert_rule_new (
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

    condition_type TEXT NOT NULL CHECK (condition_type IN ('count','ratio')),
    threshold INTEGER NOT NULL,
    window_minutes INTEGER NOT NULL,
    for_minutes INTEGER NOT NULL DEFAULT 0,

    channel_ids TEXT NOT NULL,
    severity TEXT NOT NULL DEFAULT 'P2' CHECK (severity IN ('P0','P1','P2')),
    cooldown_minutes INTEGER NOT NULL DEFAULT 5,
    notify_recovery INTEGER NOT NULL DEFAULT 1 CHECK (notify_recovery IN (0, 1)),
    repeat_minutes INTEGER NULL,

    silence_until TEXT NULL,

    created_by INTEGER NULL REFERENCES lp_user(id) ON DELETE SET NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

INSERT INTO lp_alert_rule_new(
    id, project_id, name, description, enabled,
    service_name, environment, levels, keyword, exception_type,
    condition_type, threshold, window_minutes, for_minutes,
    channel_ids, severity, cooldown_minutes, notify_recovery, repeat_minutes,
    silence_until, created_by, created_at, updated_at)
SELECT
    id, project_id, name, description, enabled,
    service_name, environment, levels, keyword, exception_type,
    condition_type, threshold, window_minutes, for_minutes,
    channel_ids, severity, cooldown_minutes, notify_recovery, repeat_minutes,
    NULL, created_by, created_at, updated_at
FROM lp_alert_rule;

DROP TABLE lp_alert_rule;

ALTER TABLE lp_alert_rule_new RENAME TO lp_alert_rule;

CREATE INDEX IF NOT EXISTS ix_alert_rule_project ON lp_alert_rule(project_id, enabled);

COMMIT;

PRAGMA foreign_keys = ON;
