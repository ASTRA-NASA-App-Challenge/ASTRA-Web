# ASTRA

## Projects

- `Common`: shared role names and service result models.
- `Entities`: persisted entities, including the Identity application user.
- `Data`: EF Core context, SQLite initialization, and migrations.
- `Services`: account provisioning, credential validation/protection, and personnel queries.
- `Web`: application startup, HTTP authorization, controllers, Razor pages, views, and static assets.
- `Tests`: account, authorization, migration, and personnel-query tests.

Dependencies flow from `Web` to `Services`, `Data`, `Entities`, and `Common`.
`Services` depends on `Data`, `Entities`, and `Common`; `Data` depends on `Entities`.
Class libraries do not depend on `Web`.

The home page uses `IOnboardPersonnelService` to query crew and medic role
memberships from the database on each request. Counts exclude ground control
and users without either onboard role; an empty role displays `00`.

See [account setup](Services/Identity/README.md) for credentials, roles, and PIN handling.
The runtime SQLite file stays at `Web/astra.db`, relative to the web app content root.

## Database migrations

Migrations belong to `Data`; use `Web` as the startup project:

```powershell
dotnet ef migrations add MigrationName --project Data --startup-project Web --output-dir Migrations
```

The application applies migrations at startup and preserves the existing
migration history. Moving the projects does not recreate the database.
