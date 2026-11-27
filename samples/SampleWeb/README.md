# SampleWeb

A small classic ASP.NET site for trying out `Ark.SessionState.AdoNet` with real requests. It uses the provider through `web.config` exactly as an app would.

By default it uses **SQL Server LocalDB**, which comes with Visual Studio. On first run it creates the `ArkSessionSample` database and the `AspNetSessionState` table.

## Run it

```powershell
dotnet build samples/SampleWeb
& "C:\Program Files\IIS Express\iisexpress.exe" /path:"$PWD\samples\SampleWeb" /port:5080
```

Then open http://localhost:5080.

## What's on the page

- **Run all checks** tests values persisting across requests, Unicode strings, a 200 KB value, the row in the database, lock serialization between concurrent requests, read-only requests waiting for a lock, lock release, `Session.Timeout` changes and `Session.Abandon`.
- **Run expiry check** sets `Session.Timeout` to 1 minute, waits about 70 seconds, and confirms the session is gone.
- **Manual** buttons call each operation one at a time and show the JSON response.
- **Database rows** shows the raw rows in the session table. This is for diagnostics only; never expose session data like this in a real app.

## Use another database

1. Add the database's ADO.NET driver package to `SampleWeb.csproj` (e.g. `Oracle.ManagedDataAccess`).
2. In `Web.config`, replace the `SessionState` connection string with one of the commented examples.
3. Rebuild and run. The site creates the table from the matching script in `sql/` if it doesn't exist.

Creating the table is a sample-only convenience; the provider itself never creates tables. To test with a least-privilege account, create the table yourself, set `Sample:CreateSchema` to `false` in `Web.config`, and connect as a user with only `SELECT`, `INSERT`, `UPDATE` and `DELETE` on the table. See [Database permissions](../../README.md#database-permissions) in the main README.

The site's endpoints are in `Handlers.cs`, and the startup schema setup is in `Global.asax.cs`.
