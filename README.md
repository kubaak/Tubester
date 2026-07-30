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
- 📊 **Observability**: Structured logging (Serilog), OpenTelemetry metrics, Prometheus, Grafana, health checks

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

## 📊 Observability

Tubester supports two local observability modes:

1. **Normal local development**
   - API and Worker can run from the IDE or `dotnet run`.
   - Logs go to console and Seq.
   - Prometheus scrapes `/metrics`.
   - Grafana shows metrics.

2. **Production-like local observability**
   - API and Worker run as Docker containers.
   - Apps write structured logs to stdout.
   - Grafana Alloy reads Docker container logs.
   - Alloy forwards logs to Loki.
   - Grafana queries Loki for logs.
   - Prometheus scrapes API and Worker metrics over the Docker network.

Tubester includes a comprehensive observability stack with structured logging, metrics, and health checks.

### Quick Start

To run the full observability stack:

```bash
# Start the API/Worker first, then:
docker compose -f docker-compose.observability.yml up -d
```

This starts:

- **Prometheus** (port 9090) - metrics collection and storage
- **Grafana** (port 3000) - dashboards and visualization
- **Seq** (port 5341) - structured log aggregation

### Accessing Observability Tools

| Tool       | URL                   | Default Credentials |
| ---------- | --------------------- | ------------------- |
| Prometheus | http://localhost:9090 | -                   |
| Grafana    | http://localhost:3000 | admin / admin       |
| Seq        | http://localhost:5341 | -                   |

### Metrics Endpoints

| Service         | Endpoint   | Metrics Available                        |
| --------------- | ---------- | ---------------------------------------- |
| Tubester.Api    | `/metrics` | Request/runtime/process metrics          |
| Tubester.Worker | `/metrics` | Business metrics (users, channels, etc.) |

⚠️ **Important**: The `/metrics` endpoint is not exposed publicly by default. It should only be accessible internally or through a protected path in production.

> **Business Metrics Location**: Business metric gauges (users, channels, videos, replies) are refreshed by the Worker process and are only visible from `Tubester.Worker:/metrics`. The API `/metrics` endpoint does not include these gauges because `BusinessMetricsGauges` is process-local and must be scraped from the Worker.

### Health Check Endpoints

| Service         | Endpoint        | Purpose         | Checks                           |
| --------------- | --------------- | --------------- | -------------------------------- |
| Tubester.Api    | `/health/live`  | Liveness probe  | Self-check only (lightweight)    |
| Tubester.Api    | `/health/ready` | Readiness probe | PostgreSQL/Hangfire connectivity |
| Tubester.Worker | `/health/live`  | Liveness probe  | Self-check only (lightweight)    |
| Tubester.Worker | `/health/ready` | Readiness probe | PostgreSQL/Hangfire connectivity |

### Kubernetes Probes

Example Kubernetes deployment configuration for health checks:

#### Tubester.Api

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 80
  initialDelaySeconds: 10
  periodSeconds: 15
  timeoutSeconds: 5
  failureThreshold: 3

readinessProbe:
  httpGet:
    path: /health/ready
    port: 80
  initialDelaySeconds: 15
  periodSeconds: 10
  timeoutSeconds: 5
  failureThreshold: 3
```

#### Tubester.Worker

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 15
  timeoutSeconds: 5
  failureThreshold: 3

readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 15
  periodSeconds: 10
  timeoutSeconds: 5
  failureThreshold: 3
```

**Note**: The Worker's health endpoints are served on port 8080 by default (configurable via `Observability:WorkerHttpPort`).

### Configuration

Observability can be configured via `appsettings.json` or environment variables:

```json
{
  "Observability": {
    "Enabled": true,
    "Prometheus": {
      "Enabled": true,
      "ExposePublicly": false
    },
    "Seq": {
      "Enabled": false,
      "Url": "http://localhost:5341"
    },
    "BusinessMetrics": {
      "RefreshIntervalSeconds": 60
    }
  }
}
```

#### Environment Variables

| Variable                   | Description            |
| -------------------------- | ---------------------- |
| `OBSERVABILITY_SEQ_URL`    | Seq server URL         |
| `OBSERVABILITY_SEQ_APIKEY` | Seq API key (optional) |

### Available Metrics

#### Business Metrics (Gauges)

| Metric                    | Type  | Description              |
| ------------------------- | ----- | ------------------------ |
| `tubester_users_total`    | Gauge | Total number of users    |
| `tubester_channels_total` | Gauge | Total number of channels |
| `tubester_videos_total`   | Gauge | Total number of videos   |
| `tubester_replies_total`  | Gauge | Total number of replies  |

#### Comment Scan Metrics

| Metric                                  | Type    | Description                   |
| --------------------------------------- | ------- | ----------------------------- |
| `tubester_comment_scan_started_total`   | Counter | Total comment scans started   |
| `tubester_comment_scan_succeeded_total` | Counter | Total comment scans succeeded |
| `tubester_comment_scan_failed_total`    | Counter | Total comment scans failed    |

#### Reply Generation Metrics

| Metric                                      | Type    | Description                       |
| ------------------------------------------- | ------- | --------------------------------- |
| `tubester_reply_generation_started_total`   | Counter | Total reply generations started   |
| `tubester_reply_generation_succeeded_total` | Counter | Total reply generations succeeded |
| `tubester_reply_generation_failed_total`    | Counter | Total reply generations failed    |

#### Embedding Generation Metrics

| Metric                                          | Type    | Description                           | Labels     |
| ----------------------------------------------- | ------- | ------------------------------------- | ---------- |
| `tubester_embedding_generation_started_total`   | Counter | Total embedding generations started   | `provider` |
| `tubester_embedding_generation_succeeded_total` | Counter | Total embedding generations succeeded | `provider` |
| `tubester_embedding_generation_failed_total`    | Counter | Total embedding generations failed    | `provider` |

#### YouTube API Metrics

| Metric                              | Type    | Description              | Labels      |
| ----------------------------------- | ------- | ------------------------ | ----------- |
| `tubester_youtube_api_calls_total`  | Counter | Total YouTube API calls  | `operation` |
| `tubester_youtube_api_errors_total` | Counter | Total YouTube API errors | `operation` |

#### AI Metrics

| Metric                          | Type    | Description          | Labels                  |
| ------------------------------- | ------- | -------------------- | ----------------------- |
| `tubester_ai_calls_total`       | Counter | Total AI calls       | `provider`, `operation` |
| `tubester_ai_call_errors_total` | Counter | Total AI call errors | `provider`, `operation` |

#### ASP.NET Core Instrumentation (via OpenTelemetry)

- `http.server.request.duration` - HTTP request duration histogram
- `http.server.request.count` - HTTP request count
- `http.client.request.duration` - HTTP client request duration

#### Runtime Instrumentation (via OpenTelemetry)

- `process.cpu.time` - CPU time used by the process
- `process.memory.usage` - Memory usage
- `process.threads` - Thread count
- `runtime.memory.heap` - GC heap metrics
- `runtime.gc.collections` - GC collection counts

### Grafana Dashboard

A pre-configured dashboard is available in `observability/grafana/provisioning/dashboards/tubester-overview.json`.

Panels include:

- Business metrics (users, channels, videos, replies)
- API request rate and error rate
- API response duration (p50, p95)
- Comment scan job counters
- YouTube API call rates
- AI call rates and errors

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
7. (Optional) Start observability stack:
   ```bash
   docker compose -f docker-compose.observability.yml up -d
   ```

## Tubester production Kubernetes baseline

This directory contains the production Kubernetes source of truth for Tubester on k3s.

The target platform is k3s with the bundled Traefik ingress controller. Do not install a second Traefik instance for this baseline.

## Namespace

Production resources run in the `tubester` namespace.

ServiceMonitor resources are created in the `monitoring` namespace and assume `kube-prometheus-stack` / Prometheus Operator is already installed.

## Secrets

Do not commit real secrets.

Use `secret.example.yaml` as a template only. Create the real secret manually:

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
