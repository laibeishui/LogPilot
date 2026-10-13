-- V0.1 初始 schema：用户、项目、成员、应用、接入密钥、系统设置、迁移记录
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS lp_schema_migration (
    version INTEGER PRIMARY KEY,
    name TEXT NOT NULL,
    applied_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS lp_user (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    username TEXT NOT NULL COLLATE NOCASE,
    display_name TEXT NOT NULL DEFAULT '',
    password_hash TEXT NOT NULL,
    status INTEGER NOT NULL DEFAULT 1 CHECK (status IN (0, 1)),
    is_system_admin INTEGER NOT NULL DEFAULT 0 CHECK (is_system_admin IN (0, 1)),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    last_login_at TEXT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_lp_user_username ON lp_user(username);

CREATE TABLE IF NOT EXISTS lp_project (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    code TEXT NOT NULL COLLATE NOCASE,
    name TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    status INTEGER NOT NULL DEFAULT 1 CHECK (status IN (0, 1)),
    created_by INTEGER NULL REFERENCES lp_user(id) ON DELETE SET NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_lp_project_code ON lp_project(code);
CREATE INDEX IF NOT EXISTS ix_lp_project_status ON lp_project(status);

CREATE TABLE IF NOT EXISTS lp_project_member (
    project_id INTEGER NOT NULL REFERENCES lp_project(id) ON DELETE CASCADE,
    user_id INTEGER NOT NULL REFERENCES lp_user(id) ON DELETE CASCADE,
    role TEXT NOT NULL CHECK (role IN ('owner', 'admin', 'viewer')),
    created_at TEXT NOT NULL,
    PRIMARY KEY (project_id, user_id)
);
CREATE INDEX IF NOT EXISTS ix_lp_project_member_user ON lp_project_member(user_id);

CREATE TABLE IF NOT EXISTS lp_application (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL REFERENCES lp_project(id) ON DELETE CASCADE,
    service_name TEXT NOT NULL,
    display_name TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    status INTEGER NOT NULL DEFAULT 1 CHECK (status IN (0, 1)),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE(project_id, service_name)
);
CREATE INDEX IF NOT EXISTS ix_lp_application_project ON lp_application(project_id);

CREATE TABLE IF NOT EXISTS lp_api_key (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    project_id INTEGER NOT NULL REFERENCES lp_project(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    key_prefix TEXT NOT NULL,
    key_hash TEXT NOT NULL UNIQUE,
    scope TEXT NOT NULL DEFAULT 'ingest' CHECK (scope IN ('ingest')),
    status INTEGER NOT NULL DEFAULT 1 CHECK (status IN (0, 1)),
    created_by INTEGER NULL REFERENCES lp_user(id) ON DELETE SET NULL,
    created_at TEXT NOT NULL,
    expires_at TEXT NULL,
    revoked_at TEXT NULL,
    last_used_at TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_lp_api_key_project_status ON lp_api_key(project_id, status);

CREATE TABLE IF NOT EXISTS lp_system_setting (
    setting_key TEXT PRIMARY KEY,
    setting_value TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
