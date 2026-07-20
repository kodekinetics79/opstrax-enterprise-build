# OpsTrax PostgreSQL Local Environment Guide

## Connection Sources
- `PG_CONNECTION` in `.env`
- `ConnectionStrings__DefaultConnection` for the .NET service
- For this workspace, the verified local PostgreSQL target is the Docker container
  `zayra_pg` on `127.0.0.1:5433` with the disposable `opstrax_local` database.

## Local Development Rules
- Keep local dev and production connection strings separate.
- Use a disposable or dedicated dev PostgreSQL database for schema work.
- Never hardcode host, password, or tenant values into source.
- Confirm the target is local before applying any migration:
  - container name and port
  - database name
  - user/password pair
  - no prod host, secret, or tunnel reference

## Legacy Config Note
- `api-dotnet/appsettings.json` still contains a MySQL-era example and should be treated as legacy documentation only.
