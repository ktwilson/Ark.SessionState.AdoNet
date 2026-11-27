# Changelog

## 1.0.0

First release as Ark.SessionState.AdoNet, a database-agnostic rewrite of Ark.SessionState.Oracle.Provider.

- Works with any ADO.NET provider (SQL Server, Oracle, PostgreSQL, MySQL/MariaDB, SQLite, ODBC, OLE DB, custom dialects).
- Session data stored as binary instead of base64 text (no 4000-character limit on Oracle).
- Fixed locking: read-only requests now honor locks, and lock IDs increment atomically.
- Fixed new sessions not being saved, the invalid `DELETE * FROM` in RemoveItem, and mismatched Oracle bind variables.
- Times stored in UTC; per-session timeouts are respected when the lock is released or the timeout is reset.
- Background cleanup of expired sessions, replacing the one-time cleanup at startup.
- Provider settings (tableName, applicationName, ...) moved to provider attributes, replacing the SessionTableView app setting.
