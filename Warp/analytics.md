Implement product analytics in YouTubester (public SaaS): an append-only user activity log stored in Postgres.

High-level goals

- Track every login + feature usage:
    - Copy template executed
    - AI template enqueued
    - AI template submitted to YouTube
    - Reply posted to YouTube
- Store events in dedicated Postgres schema: `analytics`
- Keep DB naming conventions: PascalCase table name `UserEvents` (not snake_case)
- Use EF Core entity + migration + a logging abstraction (repo/service)

1) Persistence / EF Core model

- In `YouTubester.Persistence`, add new EF entity `UserEvent` (or `UserEventEntity`) and include it in `YouTubesterDb`.
- Map entity to: schema `analytics`, table `UserEvents`.
- Migration must:
    - `migrationBuilder.EnsureSchema("analytics");`
    - Create table `analytics."UserEvents"` with:
        - `Id` bigint identity PK
        - `UserId` text NOT NULL
        - `OccurredAtUtc` timestamp with time zone NOT NULL
        - `EventType` text NOT NULL (store enum as string; prefer readability)
        - `VideoId` text NULL
        - `CommentId` text NULL
        - `MetadataJson` jsonb NULL (store serialized metadata)
- Add indexes:
    - (UserId, OccurredAtUtc DESC)
    - (EventType, OccurredAtUtc DESC)
    - (OccurredAtUtc DESC) optional but recommended for later partitioning/retention queries

2) Event type definition

- Add `UserEventType` enum in `YouTubester.Application` (preferred; Abstractions only if needed across boundaries) with
  values:
    - Login
    - CopyTemplateExecuted
    - AiTemplateEnqueued
    - AiTemplateSubmitted
    - ReplyPostedToYouTube
- Ensure EF stores enum as string (ValueConverter) or use a string property for EventType and store enum name
  explicitly.

3) Logging abstraction

- Add `IUserEventLogger` interface (place it in Application unless you have a strong reason to put it in Abstractions):
  Task LogAsync(
  string userId,
  UserEventType eventType,
  string? videoId = null,
  string? commentId = null,
  object? metadata = null,
  CancellationToken ct = default);

- Implement `UserEventLogger` in Persistence using `YouTubesterDb`:
    - Always set `OccurredAtUtc = DateTimeOffset.UtcNow`
    - Serialize metadata via System.Text.Json into `MetadataJson` (jsonb)
    - One insert per call (simple implementation)

4) DI wiring

- Register `IUserEventLogger` in:
    - `YouTubester.Api` DI container
    - `YouTubester.Worker` DI container
      as Scoped.

5) Instrumentation points to add
   A) Login events

- In Google auth `OnTicketReceived` for both schemes (GoogleRead and GoogleWrite):
    - After user upsert and token store upsert, log:
        - EventType = Login
        - Metadata: { scheme: context.Scheme.Name, hasRefreshToken: bool }

B) Copy template usage

- After successful `CopyTemplateAsync` (after YouTube update + DB upsert), log:
    - EventType = CopyTemplateExecuted
    - videoId = TargetVideoId
    - Metadata: { sourceVideoId, copyTags, copyLocation, copyPlaylists, copyCategory, copyDefaultLanguages }

C) AI template usage

- In `AiTemplateOrchestrationService` when enqueue succeeds, log:
    - EventType = AiTemplateEnqueued
    - videoId = TargetVideoId
    - Metadata: { generateTitle, generateDescription, generateTags }
- When AI template is posted to YouTube (the submit endpoint / update call), log:
    - EventType = AiTemplateSubmitted
    - videoId = TargetVideoId
    - Metadata: { generateTitle, generateDescription, generateTags } OR include only what you actually know at submit
      time.

D) Reply posted to YouTube

- When a reply is posted to YouTube (wherever you call YouTubeIntegration.ReplyAsync / post reply):
    - EventType = ReplyPostedToYouTube
    - commentId = commentId
    - videoId if available
    - Metadata: DO NOT store full finalText. Store safe info like { length: int } or { wasEdited: bool }.

6) Safety / privacy

- Never store access tokens / refresh tokens.
- Do not store prompt text.
- Avoid storing full reply text; store only safe metadata (length, flags, counts).

Deliverables

- `UserEvent` EF entity mapped to `analytics.UserEvents`
- EF migration: ensure schema + create table + indexes
- `UserEventType` enum
- `IUserEventLogger` + `UserEventLogger` implementation + DI registration in API and Worker
- Add event logging to the described hook points
- Keep changes minimal and consistent with existing solution structure and naming conventions
