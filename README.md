# FamilyHQ

A family calendar dashboard application that aggregates and displays events from Google Calendar in a clean monthly view.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Frontend | Blazor WebAssembly (.NET 10) |
| Backend API | ASP.NET Core 10 |
| Database | PostgreSQL / EF Core |
| Real-time | SignalR |
| Auth | Google OAuth 2.0 + JWT |
| Google Integration | Google Calendar API v3 |

## Project Structure

```
src/
├── FamilyHQ.WebUi/           # Blazor WASM frontend (port 7154)
├── FamilyHQ.WebApi/          # ASP.NET Core API (port 5000/5001)
├── FamilyHQ.Services/        # Business logic, Google Calendar client
├── FamilyHQ.Data/            # EF Core DbContext and repository interfaces
├── FamilyHQ.Data.PostgreSQL/ # PostgreSQL implementation and migrations
└── FamilyHQ.Core/            # Shared models, DTOs, interfaces, validators

tests/
├── FamilyHQ.Core.Tests/
├── FamilyHQ.Services.Tests/
├── FamilyHQ.WebApi.Tests/
└── FamilyHQ.Simulator.Tests/

tests-e2e/
├── FamilyHQ.E2E.Common/      # Page objects, Playwright driver, config
├── FamilyHQ.E2E.Data/        # Test data templates, Simulator API client
├── FamilyHQ.E2E.Steps/       # Reqnroll step definitions
└── FamilyHQ.E2E.Features/    # Gherkin feature files

tools/
└── FamilyHQ.Simulator/       # Google Calendar API simulator (port 7199)
```

**Dependency flow**: `WebUi` / `WebApi` → `Services` → `Data` → `Core`

## Features

- Monthly calendar view with navigation
- Full CRUD for calendar events (create, edit, delete)
- Multi-calendar support with colour coding
- All-day and timed event display
- Event detail modal
- Google Calendar sync (on login and periodically)
- Real-time UI refresh via SignalR when events are synced

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL instance
- Google Cloud project with the Calendar API enabled and OAuth 2.0 credentials configured

## Configuration

### WebApi (`src/FamilyHQ.WebApi/appsettings.Development.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=familyhq;Username=postgres;Password=<password>"
  },
  "GoogleCalendar": {
    "ClientId": "<your-google-client-id>",
    "ClientSecret": "<your-google-client-secret>",
    "AuthPromptUrl": "https://accounts.google.com/o/oauth2/v2/auth",
    "AuthBaseUrl": "https://oauth2.googleapis.com",
    "CalendarApiBaseUrl": "https://www.googleapis.com"
  },
  "FrontendBaseUrl": "https://localhost:7154",
  "Jwt": {
    "SigningKey": "<your-secret-signing-key>"
  }
}
```

> **Development shortcut**: the default `appsettings.Development.json` already points `GoogleCalendar` URLs at the local Simulator (`https://localhost:7199`). Run the Simulator instead of connecting to real Google APIs.

## Running the Application

### 1. Start the WebApi

```bash
cd src/FamilyHQ.WebApi
dotnet run
```

EF Core migrations are applied automatically on startup.

### 2. Start the WebUI

```bash
cd src/FamilyHQ.WebUi
dotnet run
```

Open `https://localhost:7154` in your browser and click **Login** to begin the Google OAuth flow.

## Running Tests

### Unit Tests

```bash
dotnet test tests/
```

### E2E Tests

E2E tests require all three services running simultaneously.

**Install Playwright browsers** (first time only):

```bash
cd tests-e2e/FamilyHQ.E2E.Common
dotnet playwright install chromium
```

**Start the services** (three separate terminals):

```bash
# Terminal 1 – WebApi
cd src/FamilyHQ.WebApi && dotnet run

# Terminal 2 – Simulator (replaces Google Calendar for testing)
cd tools/FamilyHQ.Simulator && dotnet run

# Terminal 3 – WebUI
cd src/FamilyHQ.WebUi && dotnet run
```

**Run the tests:**

```bash
cd tests-e2e/FamilyHQ.E2E.Features
dotnet test
```

Run a single scenario:

```bash
dotnet test --filter "Scenario=View upcoming events on the dashboard month view"
```

See [`.agent/docs/e2e-testing-maintenance.md`](.agent/docs/e2e-testing-maintenance.md) for full E2E documentation.

## The Simulator

`tools/FamilyHQ.Simulator` is a lightweight ASP.NET Core API that mimics the Google Calendar and OAuth endpoints. It runs on `https://localhost:7199` and is used exclusively for local development and E2E testing — no real Google credentials are required.

The Simulator exposes a `/api/simulator` configuration endpoint that the E2E test suite uses to seed isolated per-scenario user data.

## Architecture Notes

- **Clean Architecture**: dependencies only flow inward — web projects never reference Core or each other.
- **Shared validation**: FluentValidation validators live in `FamilyHQ.Core` and run on both client and server.
- **Infrastructure isolation**: the Google Calendar client is hidden behind `IGoogleCalendarClient`; the Simulator implements the same contract.
- **DateTimeOffset**: all timestamps are stored and exchanged as UTC. See `.agent/skills/datetimeoffset-postgresql/SKILL.md`.
- **API docs**: Scalar UI is available at `/scalar/v1` when running in Development mode.