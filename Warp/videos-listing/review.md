You are a senior reviewer. Perform a thorough code review of the changes on branch **feature/videoListing** vs **main**.

## Context / Intent

We added an endpoint to list videos with infinite scroll for the Copy Template UI:

- Route: `GET /api/videos`
- Query params:
    - `title` (optional, case-insensitive substring filter on Title)
    - `visibility` (optional, one or more of: public, unlisted, private, scheduled; comma-separated)
    - `pageSize` (optional, default 30, min 1, max 100)
    - `pageToken` (optional, opaque cursor)
- Sorting: `PublishedAt DESC`, then `VideoId DESC`
- Pagination: **cursor-based**
    - `pageToken` = Base64url of `"{publishedAt:o}|{videoId}"`
    - Next page returns items strictly older than the cursor:
        - `PublishedAt < T` OR (`PublishedAt = T` AND `VideoId < V`)
    - Fetch `pageSize + 1` to detect `hasNext`; return only first `pageSize`
- Response DTO: items `{ videoId, title, publishedAt }`, `nextPageToken`
- No `thumbnailUrl` in payload (client derives from `videoId`)
- Data source: EF Core `Video` entity (Tubester.Domain)

## What to review

1) **Correctness**
    - Filtering: title contains (case-insensitive) and visibility set
    - Cursor: encoding/decoding, strict boundary, stable ordering
    - Token binding: verify token is only used with the same filters/scope (title, visibility, uploadsPlaylistId)
    - `pageSize` clamping (default 30, max 100) and 400 on malformed `pageToken`
    - Uses `AsNoTracking()` for read queries; cancellation tokens are respected

2) **Performance**
    - Query uses index-friendly predicate (keyset pagination) rather than OFFSET
    - EF query shape: `OrderByDescending(PublishedAt).ThenByDescending(VideoId)` + `Take(pageSize+1)`
    - Optional index note: `(Visibility, PublishedAt DESC, VideoId DESC)` if visibility filtering is common
    - Avoids N+1 and unnecessary materialization; minimal projection for DTO

3) **Domain / Consistency**
    - Naming consistency: `UploadsPlaylistId` vs `UploadPlaylistId` (ensure consistent)
    - Nullability: avoid nullable arrays; handle null Title in filters
    - Don’t mutate domain entities unexpectedly in read paths

4) **Security / Robustness**
    - Consider token integrity (optional HMAC); at least validate decoded values
    - Input validation & error messages (bad token, bad visibility values)
    - Guard pageSize abuse; ensure no unbounded queries

5) **API design / DX**
    - Swagger/XML summaries reflect params, examples, and response shape
    - Clear 400 messages for invalid `pageToken` or `visibility`
    - Consistent HTTP semantics (GET, cache headers if appropriate—even if user-scoped, consider ETag/Last-Modified)

6) **Tests**
    - Unit tests for cursor encode/decode, page boundaries, mixed-page cutoff
    - Tests for title filter + visibility combinations
    - Tests for malformed token and pageSize clamping
    - Deterministic ordering with tie-breaker (same PublishedAt)

## How to proceed

1. Diff against main and read all related files (controller, service, repository, DTOs, mapping).
2. Leave **inline review comments** with specific suggestions.
3. Provide a short **review summary** in bullets: ✅ good, ⚠️ concerns, 🛠️ actionable fixes.
4. Propose a **minimal patch** (unified diff) only for issues that clearly improve correctness/clarity/perf, keeping
   code style consistent with the repo.
5. List any **follow-ups** (docs, indices, monitoring) that shouldn’t block merge but are recommended.

## Output format

- **Section 1: Review comments** (organized by file:line ranges)
- **Section 2: Review summary** (✅/⚠️/🛠️ bullets)
- **Section 3: Suggested patch (diff)** (if applicable)
- **Section 4: Follow-ups / nice-to-haves**

Begin the review now.
