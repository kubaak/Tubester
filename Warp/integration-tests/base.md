Create a new **integration test** project skeleton with two factories (API + Worker), SQLite cleaning, a capturing
Hangfire client, and two smoke tests (API + Worker). Target **.NET 9**. Do not start Hangfire Server in tests.

## Project

- Path: `tests/Tubester.IntegrationTests`
- TFM: **net9.0**
- Packages:
    - `xunit`
    - `FluentAssertions`       # MIT, free
    - `Moq`
    - `Microsoft.AspNetCore.Mvc.Testing`
    - `Microsoft.EntityFrameworkCore.Sqlite`
    - `Hangfire.Core`          # for IBackgroundJobClient
- **Do NOT** add `xunit.runner.visualstudio`.
- Reference the API and Worker projects as needed.

## Factories

1) **API factory**
    - File: `TestHost/ApiTestWebAppFactory.cs`
    - Derive from `WebApplicationFactory<Program>`.
    - Set `ASPNETCORE_ENVIRONMENT=Test`.
    - Override DB to **SQLite file** (e.g., `tests/.tmp/integration.sqlite`).
    - Ensure schema once at fixture startup (`EnsureCreated()`).
    - In `ConfigureTestServices`:
        - Replace `IBackgroundJobClient` with `CapturingBackgroundJobClient`.
        - Replace `IAiClient` and `IYouTubeIntegration` with **Moq** mocks (singletons).
        - **Do not** register `AddHangfireServer()`; if the API registers it by default, remove that hosted service
          here.

2) **Worker factory**
    - File: `TestHost/WorkerTestHostFactory.cs`
    - Build a **generic Host** mirroring the Worker’s DI registrations, but:
        - Set `ASPNETCORE_ENVIRONMENT=Test`.
        - Use the same SQLite file/connection string as the API tests.
        - Replace `IBackgroundJobClient` with `CapturingBackgroundJobClient`.
        - Replace `IAiClient` and `IYouTubeIntegration` with Moq mocks.
        - **Do not** start `AddHangfireServer()`.
    - If needed, add a minimal hook in the Worker project (non-production-impacting) to expose the DI registration or
      make `Program` partial, guarded by `EnvironmentName == "Test"`.

## SQLite cleaning (per test; no schema drop/recreate)

- File: `TestHost/SqliteCleaner.cs`
- Implement `CleanAsync(DbConnection)`:
    1. `BEGIN`
    2. `PRAGMA foreign_keys = OFF`
    3. Enumerate user tables via `sqlite_master` (`type='table'`, `name NOT LIKE 'sqlite_%'`)
    4. For each: `DELETE FROM "<table>";`
    5. `DELETE FROM sqlite_sequence;` (ignore if absent)
    6. `PRAGMA foreign_keys = ON`
    7. `COMMIT`
- Provide a base helper so each test starts with empty tables.

## Capturing Hangfire client (no server)

- File: `TestHost/CapturingBackgroundJobClient.cs`
- Implement `IBackgroundJobClient` that:
    - Records `Enqueue` / `Schedule` calls (job type, method, args).
    - Returns deterministic job IDs (e.g., incrementing strings).
    - Provides helpers:
        - `GetEnqueued<TJob>()`
        - `RunAll<TJob>(IServiceProvider, CancellationToken)` → resolve `TJob` from DI and **invoke** captured delegates
          synchronously (pass `JobCancellationToken.Null` or a `CancellationToken` as appropriate).
- Since we’re using this test double, we don’t need to clear Hangfire storage tables.

## Fixtures / Collections

- Files:
    - `TestHost/TestCollection.cs` and `TestHost/TestFixture.cs`
- `TestFixture`:
    - Creates the SQLite file once and calls `EnsureCreated()`.
    - Exposes:
        - `HttpClient` (from `ApiTestWebAppFactory`)
        - `IServiceProvider` (from API factory)
        - `WorkerServices` (from Worker factory’s built host)
        - `ResetDbAsync()` → runs the **SQLite cleaner** before each test.

## Smoke tests (phase 1 only)

Create **two** minimal tests to prove the harness; full coverage comes later.

1) **API smoke**
    - File: `Videos/VideosEndpoint_SmokeTests.cs`
    - Test: `GetVideos_EmptyDb_ReturnsEmptyList`
        - Arrange: `await fixture.ResetDbAsync()`
        - Act: `GET /api/videos?pageSize=5`
        - Assert: 200 OK; payload has `items: []`, `nextPageToken: null`

2) **Worker smoke**
    - File: `Worker/WorkerHost_SmokeTests.cs`
    - Test: `Worker_BootsAndCanResolveJobs_WithoutServer`
        - Arrange: `await fixture.ResetDbAsync()`
        - Act:
            - Ensure the **Worker host builds** and `WorkerServices` is non-null.
            - Resolve a known job type from DI (e.g., `PostApprovedCommentsJob` or any worker job you register).
            - Using `CapturingBackgroundJobClient`, **enqueue** one job call (simulate what the API would do).
            - Call `RunAll<ThatJob>(fixture.WorkerServices, ct)` to execute the captured job **synchronously**.
        - Assert:
            - No exceptions thrown.
            - (Optional) DB side-effect occurred (e.g., row updated/inserted) if a trivial safe scenario exists.
        - Note: Do **not** start HangfireServer; execution is manual via the capturing client.

## Minimal app changes (only if needed)

- In the API project, ensure `public partial class Program { }` exists so `WebApplicationFactory<Program>` compiles.
- Ensure Hangfire Server registration is **skipped in Test** environment (or remove the hosted service in the factory).

## Output

- Provide a new branch feature/integrationTestsInit with:
    - New test project (`.csproj`)
    - `TestHost/ApiTestWebAppFactory.cs`
    - `TestHost/WorkerTestHostFactory.cs`
    - `TestHost/SqliteCleaner.cs`
    - `TestHost/CapturingBackgroundJobClient.cs`
    - `TestHost/TestCollection.cs`, `TestHost/TestFixture.cs`
    - `Videos/VideosEndpoint_SmokeTests.cs`
    - `Worker/WorkerHost_SmokeTests.cs`
    - Any minimal non-invasive tweaks in API/Worker needed for test hosting
- Include commands to run:
    - `dotnet build`
    - `dotnet test tests/Tubester.IntegrationTests -c Release`

## Constraints

- Target **net9.0**
- Use **Moq** + **FluentAssertions**
- **Do not** start Hangfire Server in tests
- **Do not** recreate schema per test; **clean tables** instead
- No thumbnail derivation in tests
- Keep it editor-agnostic (Rider/CLI)
