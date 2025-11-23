-- =========================
-- SCHEMA FOR GPT-MEMORY v3.0
-- Compatible with OpenAI MCP Connector
-- =========================

-- Key-Value Store (Backward Compatible)
CREATE TABLE IF NOT EXISTS kv_store (
    key TEXT PRIMARY KEY NOT NULL,
    value TEXT NOT NULL,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_kv_updated ON kv_store(updated_at DESC);

-- Threads (Thread Metadata)
CREATE TABLE IF NOT EXISTS threads (
    id TEXT PRIMARY KEY NOT NULL,
    title TEXT,
    status TEXT DEFAULT 'active' CHECK(status IN ('active', 'archived', 'resolved')),
    category TEXT DEFAULT 'general',
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_threads_status ON threads(status);
CREATE INDEX IF NOT EXISTS idx_threads_category ON threads(category);
CREATE INDEX IF NOT EXISTS idx_threads_updated ON threads(updated_at DESC);

-- Entries (Thread Entries / Memory Entries)
CREATE TABLE IF NOT EXISTS entries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    thread_id TEXT NOT NULL,
    parent_id INTEGER,
    content TEXT NOT NULL,
    entry_type TEXT DEFAULT 'thought' CHECK(entry_type IN ('thought', 'journal', 'decision', 'note', 'task')),
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (thread_id) REFERENCES threads(id) ON DELETE CASCADE,
    FOREIGN KEY (parent_id) REFERENCES entries(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_entries_thread ON entries(thread_id);
CREATE INDEX IF NOT EXISTS idx_entries_parent ON entries(parent_id);
CREATE INDEX IF NOT EXISTS idx_entries_type ON entries(entry_type);
CREATE INDEX IF NOT EXISTS idx_entries_created ON entries(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_entries_content ON entries(content);

-- Tags (Entry Tags for Organization)
CREATE TABLE IF NOT EXISTS tags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    entry_id INTEGER NOT NULL,
    tag TEXT NOT NULL,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (entry_id) REFERENCES entries(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_tags_entry ON tags(entry_id);
CREATE INDEX IF NOT EXISTS idx_tags_tag ON tags(tag);

-- Friction Log (Development Friction Tracking)
CREATE TABLE IF NOT EXISTS friction_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    description TEXT NOT NULL,
    category TEXT DEFAULT 'unspecified' CHECK(category IN ('unspecified', 'tooling', 'documentation', 'api', 'infrastructure', 'workflow', 'other')),
    severity TEXT DEFAULT 'medium' CHECK(severity IN ('low', 'medium', 'high', 'critical')),
    status TEXT DEFAULT 'unresolved' CHECK(status IN ('unresolved', 'resolved', 'wont-fix')),
    solution TEXT,
    created_at TEXT DEFAULT CURRENT_TIMESTAMP,
    resolved_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_friction_status ON friction_log(status);
CREATE INDEX IF NOT EXISTS idx_friction_severity ON friction_log(severity);
CREATE INDEX IF NOT EXISTS idx_friction_category ON friction_log(category);
CREATE INDEX IF NOT EXISTS idx_friction_created ON friction_log(created_at DESC);
