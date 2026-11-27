# Ark.SessionState.AdoNet

A custom **ASP.NET session state store provider** for classic ASP.NET (System.Web) that stores sessions in **any database with an ADO.NET provider**:

| Database | ADO.NET provider (`providerName`) | Schema |
|---|---|---|
| SQL Server / Azure SQL | `System.Data.SqlClient`, `Microsoft.Data.SqlClient` | [sql/sqlserver.sql](sql/sqlserver.sql) |
| Oracle | `Oracle.ManagedDataAccess.Client`, `Oracle.DataAccess.Client` | [sql/oracle.sql](sql/oracle.sql) |
| PostgreSQL | `Npgsql` | [sql/postgresql.sql](sql/postgresql.sql) |
| MySQL / MariaDB | `MySql.Data.MySqlClient`, `MySqlConnector` | [sql/mysql.sql](sql/mysql.sql) |
| SQLite | `System.Data.SQLite`, `Microsoft.Data.Sqlite` | [sql/sqlite.sql](sql/sqlite.sql) |
| Anything else | ODBC / OLE DB, or your own [dialect](#custom-databases) | adapt one of the above |

The library has **no dependency on any database driver**. You install the driver you use, and the provider finds it through `DbProviderFactory`.

Works with WebForms, MVC 5 and Web API 2 hosted in IIS / System.Web on **.NET Framework 4.6.2+**. ASP.NET Core does not use `SessionStateStoreProviderBase`, so this provider doesn't apply there.

## Features

- Drop-in `mode="Custom"` provider with no code changes; `Session["key"]` just works.
- Correct ASP.NET locking semantics: exclusive locks, lock age, and lock IDs, so concurrent requests for the same session are serialized.
- Sliding expiration that respects per-session `Session.Timeout` changes.
- Stores sessions as binary (BLOB / varbinary / bytea), so there's no 4000-character limit.
- Background cleanup of expired sessions (configurable, one sweep per table even with many app instances).
- Several applications can share one table (`applicationName`).
- Times are stored in UTC.
- Parameterized SQL only; the table name is validated.

## Installation

```powershell
dotnet add package Ark.SessionState.AdoNet
```

Then install the ADO.NET driver for your database (e.g. `Oracle.ManagedDataAccess`, `Npgsql`, `MySqlConnector`). `System.Data.SqlClient` is built into .NET Framework.

## Setup

### 1. Create the table

Run the script for your database from the [`sql`](sql) folder. The table name defaults to `AspNetSessionState`, and you can change it (see `tableName` below).

### 2. Add a connection string

```xml
<connectionStrings>
  <add name="SessionState"
       providerName="Oracle.ManagedDataAccess.Client"
       connectionString="User Id=SESSION_USER;Password=...;Data Source=MYDB;" />
</connectionStrings>
```

`providerName` chooses the ADO.NET driver and the SQL dialect. If you leave it out, the default is `System.Data.SqlClient`.

### 3. Register the provider

```xml
<system.web>
  <sessionState mode="Custom" customProvider="DbSessionState" timeout="20">
    <providers>
      <add name="DbSessionState"
           type="Ark.SessionState.AdoNet.DbSessionStateStore, Ark.SessionState.AdoNet"
           connectionStringName="SessionState" />
    </providers>
  </sessionState>
</system.web>
```

### Provider attributes

| Attribute | Default | Description |
|---|---|---|
| `connectionStringName` | **required** | Name of the entry in `<connectionStrings>`. |
| `tableName` | `AspNetSessionState` | Session table. Can be schema-qualified, e.g. `sessions.AspNetSessionState`. |
| `applicationName` | app virtual path | Keeps sessions of different apps apart when they share a table. |
| `dialect` | `auto` | `SqlServer`, `Oracle`, `PostgreSql`, `MySql`, `Sqlite`, `Odbc`, `OleDb`, or a custom type name. `auto` detects it from the provider. |
| `providerFactoryType` | none | Assembly-qualified `DbProviderFactory` type, e.g. `Npgsql.NpgsqlFactory, Npgsql`. Use this to skip registering the driver under `<system.data>`. |
| `cleanupInterval` | `10` | Minutes between sweeps that delete expired sessions. `0` turns the sweep off, so schedule a database job instead. |
| `commandTimeout` | driver default | Command timeout in seconds. |
| `writeExceptionsToEventLog` | `false` | If `true`, database errors go to the Windows Application event log and a generic `ProviderException` is thrown. |

## Registering the ADO.NET driver

On .NET Framework, `DbProviderFactories` only knows drivers registered in `machine.config` or `web.config`. SqlClient, ODBC and OLE DB are always registered. ODP.NET's NuGet package adds its own entry. For other drivers, do one of the following.

**Option A: register it in web.config**

```xml
<system.data>
  <DbProviderFactories>
    <remove invariant="Npgsql" />
    <add name="Npgsql" invariant="Npgsql" description="PostgreSQL"
         type="Npgsql.NpgsqlFactory, Npgsql" />
  </DbProviderFactories>
</system.data>
```

**Option B: point the provider at the factory type**

```xml
<add name="DbSessionState"
     type="Ark.SessionState.AdoNet.DbSessionStateStore, Ark.SessionState.AdoNet"
     connectionStringName="SessionState"
     providerFactoryType="Npgsql.NpgsqlFactory, Npgsql" />
```

## Examples

<details>
<summary>SQL Server</summary>

```xml
<add name="SessionState" providerName="System.Data.SqlClient"
     connectionString="Server=.;Database=MyApp;Integrated Security=true" />
```
</details>

<details>
<summary>Oracle (ODP.NET managed)</summary>

```xml
<add name="SessionState" providerName="Oracle.ManagedDataAccess.Client"
     connectionString="User Id=SESSION_USER;Password=...;Data Source=MYDB" />
```

The Oracle dialect turns on `BindByName` and binds session data as a BLOB (by reflection, so this library doesn't reference ODP.NET).
</details>

<details>
<summary>PostgreSQL</summary>

```xml
<add name="SessionState" providerName="Npgsql"
     connectionString="Host=localhost;Database=myapp;Username=app;Password=..." />
```
</details>

<details>
<summary>MySQL / MariaDB</summary>

```xml
<add name="SessionState" providerName="MySqlConnector"
     connectionString="Server=localhost;Database=myapp;User ID=app;Password=..." />
```
</details>

<details>
<summary>SQLite</summary>

```xml
<add name="SessionState" providerName="System.Data.SQLite"
     connectionString="Data Source=|DataDirectory|\sessions.db" />
```
</details>

## Custom databases

All statements are plain ANSI SQL (`SELECT` / `INSERT` / `UPDATE` / `DELETE` with no vendor functions). The only thing that varies between drivers is how parameters are written. If your database isn't detected automatically, implement `ISqlDialect` or subclass `SqlDialect`:

```csharp
public class FirebirdDialect : SqlDialect
{
    public FirebirdDialect() : base("Firebird", "@") { }
}
```

```xml
<add ... dialect="MyApp.FirebirdDialect, MyApp" />
```

Parameters are always added in the order they appear in the SQL, so positional `?` placeholders work too (see `PositionalSqlDialect`).

## Notes

- **Serialization**: session values are serialized with ASP.NET's own `SessionStateItemCollection` format, the same one StateServer and SQLServer modes use. Values must be primitive types or `[Serializable]`.
- **Session_End** is not raised. This is the same as every out-of-process session mode.
- **Expired sessions** are removed when they're accessed and by the periodic sweep. If you set `cleanupInterval="0"`, schedule `DELETE FROM AspNetSessionState WHERE Expires <= <UTC now>` in the database yourself.
- Ad-hoc cleanup is available as `DbSessionStateStore.DeleteExpiredSessions()`.

## Migrating from Ark.SessionState.Oracle.Provider

1. Create the new table from [sql/oracle.sql](sql/oracle.sql). The old schema stored base64 text; the new one stores a BLOB. Existing sessions aren't migrated, so users simply get a new session.
2. Add `providerName="Oracle.ManagedDataAccess.Client"` to the connection string.
3. Change the provider `type` to `Ark.SessionState.AdoNet.DbSessionStateStore, Ark.SessionState.AdoNet`.
4. Replace the `SessionTableView` app setting with the `tableName` attribute (if you used a non-default name).

## Building

```powershell
dotnet build
dotnet test
dotnet pack src/Ark.SessionState.AdoNet -c Release
```

The tests run the data layer against an in-memory SQLite database.

## Releasing

Releases are published to nuget.org by GitHub Actions using [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so no API key is stored. To release a version:

1. Set `<Version>` in `src/Ark.SessionState.AdoNet/Ark.SessionState.AdoNet.csproj` and update `CHANGELOG.md`.
2. Commit, then tag and push the tag:

   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```

The `release` workflow checks that the tag matches the csproj version. It then builds, runs the tests, packs, and pushes to nuget.org.

## License

[MIT](LICENSE)
