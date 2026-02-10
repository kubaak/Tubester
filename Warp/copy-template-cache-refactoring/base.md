# Refactor Copy Template flow to use **videoIds** + cross-host integration test

Refactor the copy-template feature to accept **video IDs** (not URLs) end-to-end, use **cached videos** from DB, and add
an integration test that:

1) Uses **ApiTestWebAppFactory** to call the controller and **register** the Hangfire job into the *
   *CapturingBackgroundJobClient**.
2) Uses **WorkerTestHostFactory** to **execute** the captured job against the worker DI container.

---

## Scope

### 1) Request contract change (URLs → IDs)

- Replace the properties of the current CopyVideoTemplateRequest:
    - SourceVideoUrl with SourceVideoId, TargetVideoUrl with TargetVideoId
- **Controller** must accept the updated request (no URL parsing).
- **Validation**:
    - `SourceVideoId` and `TargetVideoId` are required, non-empty, different.

### 2) Service behavior (cached reads, external write)

- `VideoTemplatingService` must:
    - Load **source** and **target** **from DB** using the provided IDs (no YouTube GETs).
    - Build the final metadata to apply (fields from source; if `AiSuggestionOptions != null`, override
      title/description via
      `IAiClient.SuggestMetadataAsync` with optional `PromptEnrichment`).
    - Call **`IYouTubeIntegration.UpdateVideoMetadataAsync(TargetVideoId, payload, ct)`** to push updates.
    - **Only after a successful YouTube update**, persist the same metadata into the **target** `Video` entity via
      `ApplyDetails(...)` and save.
    - On YouTube failure: **do not** persist DB changes; return error.

### 3) Job & enqueue path

- `CopyVideoTemplateJob.Run(CopyVideoTemplateRequest req, JobCancellationToken token)` stays the same **signature-wise
  **, but now the request contains **IDs**.
- Controller action enqueues the job using the updated request.

---

## Test: cross-host E2E (one happy-path test)

**Name**: `CopyTemplate_EnqueueViaApi_RunOnWorker_UsesCachedSource_UpdatesTargetAndPersists`

**Arrange**

- Use **ApiTestWebAppFactory** and `SqliteCleaner` to start from an empty DB.
- Seed DB:
    - Introduce some Seeder helper class which will utilize autofixture via the property TestFixture.Auto
    - **Source video**: rich metadata (title, description, tags, category, langs, location…).
    - **Target video**: different metadata initially.
- **Mocks**:
    - `IYouTubeIntegration.UpdateVideoMetadataAsync(TargetVideoId, …)` → return **success**; capture the payload.
    - `IAiClient.SuggestMetadataAsync(...)`:
        - If `AiSuggestionOptions is not null`, return a title/description/tags payload to override title/description (
          and tags if spec
          requires).
        - If `AiSuggestionOptions is null`, assert no AI call.
- **Enqueue** via API:
    - POST `/api/videos/copy-template` with **IDs** (`SourceVideoId`, `TargetVideoId`, optional
      `AiSuggestionOptions`,...).
    - Assert HTTP 200 and that **ApiTestWebAppFactory**’s `CapturingBackgroundJobClient` captured **one**
      `CopyVideoTemplateJob` with the expected request.

**Execute on Worker**

- The test will run in the similar manner as current WorkerHost_SmokeTests.CopyVideoTemplateJob_Runs_Successfully

**Assert**

- `IYouTubeIntegration.UpdateVideoMetadataAsync` was called **once** with:
    - The **TargetVideoId** from the request.
    - A payload equal to the **effective template** (source fields, AI overrides if enabled).
- The **target `Video`** row in DB now equals the effective metadata (and `UpdatedAt` changed).
- No DB changes occurred **before** the YouTube call (i.e., only persisted after success).

---

## Controller & route

- Keep the existing route: `POST /api/videos/copy-template`
- Replace the request body with the **IDs-based** `CopyVideoTemplateRequest`.
- Update Swagger/XML docs accordingly.

---

## Files to touch / add

- **Contracts**
    - Update `CopyVideoTemplateRequest` to `{ SourceVideoId, TargetVideoId,.. }`
- **API**
    - Controller action to accept the **IDs-based** request (no URL parsing).
- **Application**
    - `VideoTemplatingService` refactor to DB reads + YouTube write + conditional DB persist on success.
- **Job**
    - `CopyVideoTemplateJob` still calls the service; no external reads.
- **Integration tests**
    - test with AiSuggestionOptions != null
    - test with AiSuggestionOptions == null

---

## Output

- a new feature branch:
    - `feature/copy-template-cache-refactoring`
- Include build & test commands:
    - `dotnet build`
    - `dotnet test tests/Tubester.IntegrationTests -c Release`

---

## Guardrails

- **Do not** start Hangfire Server in tests.
- Keep DB updates **strictly** after successful YouTube update.
- Use **Moq** & **FluentAssertions**.
- Test uses **ApiTestWebAppFactory** to enqueue and **WorkerTestHostFactory** to execute.