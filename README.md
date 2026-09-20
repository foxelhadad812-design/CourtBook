# CourtBook

A court booking web application for **football**, **padel**, and **tennis** courts. Venue owners can list and manage their courts with schedules and pricing. Clients can browse available slots and make bookings. Admins oversee the entire platform.

---

## Tech Stack

| Layer | Technology |
|---|---|
| API | ASP.NET Core Web API (.NET 10) |
| ORM | Entity Framework Core 10 (Code First) |
| Database | SQL Server (local) |
| Architecture | Clean Architecture |
| API Docs | Swagger / Swashbuckle |

---

## Project Structure

```
CourtBook/
├── src/
│   ├── CourtBook.Domain/         # Entities and enums — no dependencies
│   ├── CourtBook.Application/    # Use cases and interfaces (Phase 2+)
│   ├── CourtBook.Infrastructure/ # EF Core DbContext, configs, migrations
│   └── CourtBook.API/            # Controllers, Program.cs, Swagger
└── CourtBook.sln
```

---

## Getting Started

### Prerequisites
- .NET 10 SDK
- SQL Server (local instance, accessible via Windows Authentication)
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`

### 1. Configure the connection string

Open [`src/CourtBook.API/appsettings.Development.json`](src/CourtBook.API/appsettings.Development.json) and update the `DefaultConnection` value:

```json
"DefaultConnection": "Server=localhost;Database=CourtBookDB;Trusted_Connection=True;TrustServerCertificate=True;"
```

> **Instance name not `localhost`?**  
> Replace `localhost` with your actual SQL Server instance name, for example:
> - `.\SQLEXPRESS` for SQL Server Express
> - `DESKTOP-ABC123\SQLEXPRESS` for a named instance on a specific machine

### 2. Apply the migration

```bash
dotnet ef database update \
  --project src/CourtBook.Infrastructure \
  --startup-project src/CourtBook.API
```

### 3. Run the API

```bash
dotnet run --project src/CourtBook.API
```

Swagger UI will be available at `https://localhost:{port}/swagger`.

---

## Roles

| Role | Description |
|---|---|
| `Admin` | Full platform access |
| `Owner` | Manages venues and courts |
| `Client` | Browses and books courts |

---

## Entities

- **User** — registered users with a role
- **Venue** — a physical location owned by an Owner
- **Court** — a bookable court within a venue (football / padel / tennis)
- **CourtSchedule** — opening hours per day of the week
- **Booking** — a time-slot reservation made by a client

---

## Known TODOs

### 🚧 Overlapping bookings constraint (Phase 2)

A "no overlapping bookings for the same court" rule is currently enforced only in application code (to be implemented in Phase 2). A proper **database-level constraint** (filtered index or trigger) cannot be expressed through EF Core's Fluent API and will be added as raw SQL in a dedicated migration in a later phase.

See also the `// TODO` comment in [`BookingConfiguration.cs`](src/CourtBook.Infrastructure/Persistence/Configurations/BookingConfiguration.cs).

### 🔒 Authentication & Authorization (Phase 2)

JWT-based auth and role-based authorization policies are not yet implemented.

---

## Roadmap

- **Phase 1** ✅ — Solution scaffold, entities, EF Core, first migration
- **Phase 2** — Application services, JWT auth, controllers (CRUD)
- **Phase 3** — Booking overlap enforcement, availability check, notifications
