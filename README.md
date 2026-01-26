# 🎥 YouTubester

**YouTubester** is a layered .NET solution that automates YouTube metadata updates, playlists and comment management.
It integrates with the **YouTube Data API**, persists state with **EF Core**, and can use a local AI model to generate titles, descriptions, and replies.

This solution demonstrates:
- ASP.NET Core Web API + SPA host (`YouTubester.Api`)
- Background worker with Hangfire recurring jobs (`YouTubester.Worker`)
- Clean architecture separation (Domain, Persistence, Application, Abstractions, Integration)
- EF Core with migrations (PostgreSQL)
- Integration layer for YouTube Data API
- Optional data migrator from legacy SQLite to PostgreSQL (`YouTubester.Migrator`)
- Integration tests (`tests/YouTubester.IntegrationTests`)

---

## ✨ Features
- 🔑 OAuth2 authentication with YouTube Data API
- 🏷 Sync video tags and append hashtags to descriptions
- 📂 Add Shorts to playlists automatically
- 💬 List unanswered comments and generate AI-powered draft replies
- ✅ Review/edit/approve drafts via API (SPA client is hosted separately in `YouTubester-Client`)
- 📝 Persist drafts, replies, channels, videos and credits with EF Core
- 🌍 Configurable persistence (PostgreSQL)
- 🤖 Local AI integration via Ollama (no external API costs)
- 🧰 Hangfire dashboard for background jobs (`/hangfire`)

---

## 🏗️ Architecture

### Projects
- `YouTubester.Domain` – pure business entities and domain logic.
- `YouTubester.Abstractions` – shared contracts and interfaces between layers.
- `YouTubester.Persistence` – EF Core DbContext, entity mappings, repositories.
- `YouTubester.Application` – application services and business rules (comments scanning, replies, credits, templates, etc.).
- `YouTubester.Integration` – integration with Google / YouTube Data API.
- `YouTubester.Api` – ASP.NET Core Web API, auth, DI wiring, Swagger, SPA host.
- `YouTubester.Worker` – background worker that registers and runs Hangfire recurring jobs.
- `YouTubester.Migrator` – one-off console tool to migrate data from legacy SQLite to PostgreSQL (see `README.migrator.md`).
- `tests/YouTubester.IntegrationTests` – integration tests for the API and persistence.

### Layers
- **Domain** – no EF or API dependencies.
- **Persistence** – EF Core + database configuration.
- **Application** – orchestrates domain, persistence and integrations.
- **Integration** – external services (YouTube, HTTP, etc.).
- **API / Worker** – hosting, HTTP endpoints, background jobs.

In development the API also proxies the SPA client from `YouTubester-Client` (Vite, default at `http://localhost:5173`).
In production the built SPA is served from `wwwroot`.

---

## ⚡ Getting Started

### 1. Clone and restore
```bash
git clone https://github.com/kubaak/YouTubester.git
cd YouTubester
dotnet restore
```

If you also want the web client, clone it next to this repo:
```bash
git clone https://github.com/kubaak/YouTubester-Client.git
```

### 2. Configure API secrets
```bash
dotnet user-secrets init --project YouTubester.Api

dotnet user-secrets set "YouTube:ClientId" "your-client-id" --project YouTubester.Api
dotnet user-secrets set "YouTube:ClientSecret" "your-client-secret" --project YouTubester.Api

# Optional – local AI via Ollama
dotnet user-secrets set "YouTube:AI:Endpoint" "http://localhost:11434" --project YouTubester.Api
dotnet user-secrets set "YouTube:AI:Model" "gemma3:12b" --project YouTubester.Api
```

### 3. Database
Create / update the database schema from the solution root:
```powershell
dotnet ef database update -p YouTubester.Persistence -s YouTubester.Api
```

To add a new migration:
```powershell
dotnet ef migrations add <MigrationName> -p YouTubester.Persistence -s YouTubester.Api
```

Roolback to a specific migration:
```powershell
dotnet ef database update <TargetMigrationName> -p YouTubester.Persistence -s YouTubester.Api
```

For details on migrating from the legacy SQLite database to PostgreSQL, see `README.migrator.md`.

---

## 🌐 Running the API + SPA

From the solution root:
```bash
dotnet run --project YouTubester.Api
```

- Swagger UI (development): `https://localhost:5094/swagger`
- Hangfire dashboard: `https://localhost:5094/hangfire`
- SPA client (development): served from `YouTubester-Client` dev server via proxy on non-`/api` routes.

---

## 🛠 Running the Worker

The worker hosts recurring jobs (for example credit maintenance):

```bash
dotnet run --project YouTubester.Worker
```

The jobs and their status can be inspected via the Hangfire dashboard exposed by the API instance.

---

## ✅ Tests

From the solution root:
```bash
dotnet test
```

---

## 💻 Local development workflow

Typical local loop:
1. Ensure the database is up to date:
   ```bash
   dotnet ef database update -p YouTubester.Persistence -s YouTubester.Api
   ```
2. (Optional) Seed sample data in development by enabling `Seed:Enable = true` in configuration.
3. Start the API (and Hangfire dashboard):
   ```bash
   dotnet run --project YouTubester.Api
   ```
4. Start the SPA client (in the `YouTubester-Client` repo):
   ```bash
   npm install
   npm run dev
   ```
5. (Optional) start the worker if you want recurring jobs to run:
   ```bash
   dotnet run --project YouTubester.Worker
   ```
6. Run tests as needed:
   ```bash
   dotnet test
   ```
