# 🎥 Tubester

**Tubester** is a layered .NET solution that automates YouTube metadata updates, playlists and comment management.
It integrates with the **YouTube Data API**, persists state with **EF Core**, and can use a local AI model to generate titles, descriptions, and replies.

This solution demonstrates:
- ASP.NET Core Web API + SPA host (`Tubester.Api`)
- Background worker with Hangfire recurring jobs (`Tubester.Worker`)
- Clean architecture separation (Domain, Persistence, Application, Abstractions, Integration)
- EF Core with migrations (PostgreSQL)
- Integration layer for YouTube Data API
- Optional data migrator from legacy SQLite to PostgreSQL (`Tubester.Migrator`)
- Integration tests (`tests/Tubester.IntegrationTests`)

---

## ✨ Features
- 🔑 OAuth2 authentication with YouTube Data API
- 🏷 Sync video tags and append hashtags to descriptions
- 📂 Add Shorts to playlists automatically
- 💬 List unanswered comments and generate AI-powered draft replies
- ✅ Review/edit/approve drafts via API (SPA client is hosted separately in `Tubester-Client`)
- 📝 Persist drafts, replies, channels, videos and credits with EF Core
- 🌍 Configurable persistence (PostgreSQL)
- 🤖 Local AI integration via Ollama (no external API costs)
- 🧰 Hangfire dashboard for background jobs (`/hangfire`)

---

## 🏗️ Architecture

### Projects
- `Tubester.Domain` – pure business entities and domain logic.
- `Tubester.Abstractions` – shared contracts and interfaces between layers.
- `Tubester.Persistence` – EF Core DbContext, entity mappings, repositories.
- `Tubester.Application` – application services and business rules (comments scanning, replies, credits, templates, etc.).
- `Tubester.Integration` – integration with Google / YouTube Data API.
- `Tubester.Api` – ASP.NET Core Web API, auth, DI wiring, Swagger, SPA host.
- `Tubester.Worker` – background worker that registers and runs Hangfire recurring jobs.
- `Tubester.Migrator` – one-off console tool to migrate data from legacy SQLite to PostgreSQL (see `README.migrator.md`).
- `tests/Tubester.IntegrationTests` – integration tests for the API and persistence.

### Layers
- **Domain** – no EF or API dependencies.
- **Persistence** – EF Core + database configuration.
- **Application** – orchestrates domain, persistence and integrations.
- **Integration** – external services (YouTube, HTTP, etc.).
- **API / Worker** – hosting, HTTP endpoints, background jobs.

In development the API also proxies the SPA client from `Tubester-Client` (Vite, default at `http://localhost:5173`).
In production the built SPA is served from `wwwroot`.

---

## ⚡ Getting Started

### 1. Clone and restore
```bash
git clone https://github.com/kubaak/Tubester.git
cd Tubester
dotnet restore
```

If you also want the web client, clone it next to this repo:
```bash
git clone https://github.com/kubaak/Tubester-Client.git
```

### 2. Configure API secrets
```bash
dotnet user-secrets init --project Tubester.Api

dotnet user-secrets set "YouTube:ClientId" "your-client-id" --project Tubester.Api
dotnet user-secrets set "YouTube:ClientSecret" "your-client-secret" --project Tubester.Api

# Optional – local AI via Ollama
dotnet user-secrets set "YouTube:AI:Endpoint" "http://localhost:11434" --project Tubester.Api
dotnet user-secrets set "YouTube:AI:Model" "gemma3:12b" --project Tubester.Api
```

### 3. Database
Create / update the database schema from the solution root:
```powershell
dotnet ef database update -p Tubester.Persistence -s Tubester.Api
```

To add a new migration:
```powershell
dotnet ef migrations add <MigrationName> -p Tubester.Persistence -s Tubester.Api
```

Roolback to a specific migration:
```powershell
dotnet ef database update <TargetMigrationName> -p Tubester.Persistence -s Tubester.Api
```

For details on migrating from the legacy SQLite database to PostgreSQL, see `README.migrator.md`.

---

## 🌐 Running the API + SPA

From the solution root:
```bash
dotnet run --project Tubester.Api
```

- Swagger UI (development): `https://localhost:5094/swagger`
- Hangfire dashboard: `https://localhost:5094/hangfire`
- SPA client (development): served from `Tubester-Client` dev server via proxy on non-`/api` routes.

---

## 🛠 Running the Worker

The worker hosts recurring jobs (for example credit maintenance):

```bash
dotnet run --project Tubester.Worker
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
   dotnet ef database update -p Tubester.Persistence -s Tubester.Api
   ```
2. (Optional) Seed sample data in development by enabling `Seed:Enable = true` in configuration.
3. Start the API (and Hangfire dashboard):
   ```bash
   dotnet run --project Tubester.Api
   ```
4. Start the SPA client (in the `Tubester-Client` repo):
   ```bash
   npm install
   npm run dev
   ```
5. (Optional) start the worker if you want recurring jobs to run:
   ```bash
   dotnet run --project Tubester.Worker
   ```
6. Run tests as needed:
   ```bash
   dotnet test
   ```

## GitHub
### Publish a package
todo versioned tags and private packages
#### Login to Github (Bash)
```
read -s GH_TOKEN
echo "$GH_TOKEN" | docker login ghcr.io -u kubaak --password-stdin
```

#### Tag
```js
docker tag tubester-api ghcr.io/kubaak/tubester-api:latest
docker tag tubester-client ghcr.io/kubaak/tubester-client:latest
```

#### Push
```
docker push ghcr.io/kubaak/tubester-api:latest
docker push ghcr.io/kubaak/tubester-client:latest
```