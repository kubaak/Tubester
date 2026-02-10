# WARP.md

This file provides guidance for **WARP (warp.dev)** when working with code in this repository.

---

## Commands

### Setup and Build
```powershell
dotnet restore
dotnet build --no-restore
```

### Run API
Run the ASP.NET Core API (Swagger in Development, Hangfire dashboard at `/hangfire`):
```powershell
dotnet run --project Tubester.Api
# API available at: https://localhost:7031, http://localhost:5000
# Swagger UI at: https://localhost:7031/swagger (Development only)
# Hangfire dashboard at: https://localhost:7031/hangfire (Development only)
```

### Run Background Worker
Runs the Hangfire server with queues: `replies`, `templating`, `default`.
```powershell
dotnet run --project Tubester.Worker
```

### Local Infrastructure (Optional)
Local PostgreSQL container (application uses SQLite by default):
```powershell
# Start PostgreSQL container (configured but not used by default)
docker compose up -d
# Stop infrastructure
docker compose down
```

### Database (EF Core, SQLite at `./.data/Tubester.db`)
```powershell
# Add a migration
dotnet ef migrations add <Name> --project Tubester.Persistence
# Apply migrations
dotnet ef database update --project Tubester.Persistence
# Reset database
Remove-Item ./.data/Tubester.db -Force -ErrorAction SilentlyContinue
```

### Lint and Format
Uses `.editorconfig` and Roslyn analyzers:
```powershell
dotnet format
dotnet format analyzers
```

### Tests
Integration test suite:
```powershell
# Run all tests
dotnet test --nologo
# Run integration tests only
dotnet test tests/Tubester.IntegrationTests --nologo
# Run with coverage
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
# Run specific test by name pattern
dotnet test --filter "FullyQualifiedName~VideosEndpoint_SmokeTests"
```

### User Secrets (YouTube OAuth + AI Integration)
```powershell
# YouTube OAuth credentials
dotnet user-secrets set "YouTubeAuth:ClientId" "{{YOUTUBE_CLIENT_ID}}" --project Tubester.Api
dotnet user-secrets set "YouTubeAuth:ClientSecret" "{{YOUTUBE_CLIENT_SECRET}}" --project Tubester.Api

# AI configuration (Ollama or compatible)
dotnet user-secrets set "AI:Endpoint" "http://localhost:11434" --project Tubester.Api
dotnet user-secrets set "AI:Model" "gemma3:12b" --project Tubester.Api
dotnet user-secrets set "AI:Endpoint" "http://localhost:11434" --project Tubester.Worker
dotnet user-secrets set "AI:Model" "gemma3:12b" --project Tubester.Worker
```

---

## API Endpoints

### Videos
- `GET /api/videos` — List videos with pagination, title/visibility filtering
- `POST /api/videos/sync/{channelName}` — Sync channel videos from YouTube
- `POST /api/videos/copy-template` — Enqueue video template copy job

### Replies
- `GET /api/replies` — Get draft replies for approval
- `DELETE /api/replies/{id}` — Delete a draft reply
- `POST /api/replies/approve` — Batch approve replies (schedules posting)
- `POST /api/replies/batch-ignore` — Batch ignore replies

---

## Key DTOs

- `VideoListItemDto` — Video for listing (VideoId, Title, PublishedAt, ThumbnailUrl)
- `PagedResult<T>` — Paginated response wrapper (Items, NextPageToken)
- `SyncVideosResult` — Video sync operation result
- `BatchDecisionResultDto` — Batch reply operation result
- `CopyVideoTemplateRequest` — Template copy job request

*Note: All `DateTimeOffset` fields use ISO 8601 format in JSON per project coding rules.*

---

## Architecture Overview

### Solution Layout (.NET 9)
- `Tubester.Api` — ASP.NET Core Web API with controllers, Swagger, Hangfire dashboard
- `Tubester.Application` — Application services, jobs, orchestration (Hangfire jobs)
- `Tubester.Domain` — Core entities (Video, Reply, Channel) and value objects
- `Tubester.Integration` — External services (YouTube API, AI client)
- `Tubester.Persistence` — Data access (EF Core, SQLite, repositories)
- `Tubester.Worker` — Background service host (Hangfire server, CommentScanWorker)
- `tests/Tubester.IntegrationTests` — Integration test suite

### Key Dependencies
- EF Core 9.0.9 with SQLite
- Hangfire 1.8.21 with SQLite storage
- ASP.NET Core with Swagger/OpenAPI
- Google YouTube API v3
- Moq + FluentAssertions (tests)

### Database Indexes
- **Videos:** (PublishedAt, VideoId) for pagination; UpdatedAt for sync; Title NOCASE for search
- **Replies:** VideoId for queries
- **Channels:** Primary key on ChannelId

---

## Key Flows

### Video Listing with Pagination
- **Entry:** `GET /api/videos?title=X&visibility=Public&pageSize=30&pageToken=ABC`
- **Flow:** VideosController → VideoService → VideoRepository (cursor pagination)
- **Database:** Uses (PublishedAt, VideoId) composite index for efficient keyset pagination
- **Response:** `PagedResult<VideoListItemDto>` with base64-encoded cursor token

### Video Synchronization from YouTube
- **Entry:** `POST /api/videos/sync/{channelName}`
- **Flow:** VideosController → VideoService → YouTubeIntegration → VideoRepository (batch upsert)
- **External:** YouTube Data API v3 (playlist items + video details)
- **Database:** Upserts videos with change detection; maps privacy status to domain enum

### Reply Workflow (Draft → Approve → Post)
- **Drafting:** CommentScanWorker → YouTube/AI integrations → ReplyRepository
- **Approval:** `POST /api/replies/approve` → ReplyService → enqueue PostApprovedRepliesJob
- **Posting:** Hangfire job → YouTubeIntegration.ReplyAsync → update Reply status

### Background Processing
- Worker hosts Hangfire server with queues: replies, templating, default
- CommentScanWorker scans for unanswered comments and creates AI-suggested drafts
- Jobs execute asynchronously: PostApprovedRepliesJob, CopyVideoTemplateJob

---

## Configuration

### Environment Precedence
Environment variables > user-secrets > `appsettings.{Environment}.json` > `appsettings.json`

### Required Secrets (Both API and Worker)
```powershell
# YouTube OAuth
dotnet user-secrets set "YouTubeAuth:ClientId" "{{CLIENT_ID}}"
dotnet user-secrets set "YouTubeAuth:ClientSecret" "{{CLIENT_SECRET}}"

# AI integration
dotnet user-secrets set "AI:Endpoint" "http://localhost:11434"
dotnet user-secrets set "AI:Model" "gemma3:12b"
```

### Configuration Sections
- `VideoListing`: DefaultPageSize (30), MaxPageSize (100)
- `Worker`: MaxDraftsPerRun (25), IntervalSeconds (600)
- `YouTubeAuth`: ClientId, ClientSecret, ApplicationName
- `AI`: Endpoint, Model, Enable flags
- `Seed`: Enable (Development only)

### Environment Variables (PowerShell)
```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:AI__Endpoint = "http://localhost:11434"
```

### Database
SQLite at `./.data/Tubester.db` (auto-created).  
Hangfire uses the same database.  
Docker Compose includes PostgreSQL but the app uses SQLite by default.

### External Services
- YouTube Data API v3 (OAuth 2.0)
- AI provider (HTTP JSON API, typically Ollama)

---

## Project Coding Rules

- Use **primary constructors**
- Use **collection expressions**
- Use `DateTimeOffset` for all timestamps
- Don’t use abbreviations or shortcuts for variable names
- Use `var` for local variable definitions
- Create a **separate file for every public type**
- Keep interfaces and implementations in separate files  
