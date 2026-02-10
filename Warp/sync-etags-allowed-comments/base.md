# WARP TASK: Enhance `channels.sync` to persist ETags and detect per-video comment availability

You are an AI code assistant working in this repo. Implement the following changes **end-to-end** so that the existing
`channels.sync` endpoint (the one that currently triggers the main sync) also:

1) **Persists ETags** for YouTube entities we fetch (Channels, Playlists, Videos) and uses conditional requests (
   If-None-Match) on subsequent syncs.
2) **Determines whether comments are allowed** for each synced Video and persists this flag.

Keep changes small and focused, with clean diffs, tests, and docs.

---

## Scope

### A. Data model updates (Domain + EF Core)

**Add fields:**

- `Channel`
    - `string? ETag` (nullable, max ~128)
- `Playlist`
    - `string? ETag` (nullable, max ~128)
    - *(already has `LastMembershipSyncAt`)*
- `Video`
    - `string? ETag` (nullable, max ~128)
    - `bool? CommentsAllowed` (nullable; `true`/`false` known, `null` unknown/not checked)

**EF mappings (Tubester.Persistence/TubesterDb):**

- Map new properties, ensure Sqlite compatibility (no special conversion needed for strings/bools).
- Add a small index on `Playlist(ChannelId)` already exists; **no new index required** for ETags.
- Migration:
    - Add columns with sensible null defaults.
    - No backfill required.

### B. Integration surface

**IYouTubeIntegration** (and implementation):

- Ensure `GetVideosAsync`, `GetMyPlaylistsAsync`, and channel fetch path **return per-resource ETag** alongside the
  data.
    - For videos: shape return to include `Id`, `ETag`, existing fields.
    - For playlists/channels: include `(Id, Title?, ETag)` as available.
- When making subsequent calls for the same resource set, **use `If-None-Match`** with the stored ETag (when present).
    - On `304 Not Modified`, skip payload processing.
- **Comments allowed detection** for a video:
    - Preferred: call `commentThreads.list(part=id, videoId=..., maxResults=1)`.
        - `200 OK` → `CommentsAllowed = true`.
        - `403` with error reason `commentsDisabled` → `CommentsAllowed = false`.
    - Optimization: if `video.status.madeForKids == true` or `selfDeclaredMadeForKids == true`, short-circuit to `false`
      without `commentThreads.list`.
    - Only check for videos that are **new or updated** in this sync (to save quota). For existing/unchanged videos with
      known `CommentsAllowed`, skip.

> Implementation hint: Extend the typed DTOs so that every resource carries an `ETag` string from `resource.etag`. For
> conditional requests, set the request’s `IfNoneMatch` with the previously stored ETag.

### C. Sync orchestration (`channels.sync` flow)

When `channels.sync` is invoked for a channel:

1. **Uploads delta (Videos):**
    - As today, get new/changed IDs (cutoff logic unchanged).
    - **Fetch `videos.list` in batches**:
        - Capture each item’s `etag` → `Video.ETag`.
        - Upsert the video row.
        - For **each upserted (new/changed)** video:
            - Determine `CommentsAllowed` using the strategy above.
            - Persist `Video.CommentsAllowed`.
2. **Playlists + membership delta:**
    - Fetch `playlists.list(mine=true)`:
        - If we have a stored `ETag` for the playlist collection/page, send `If-None-Match`.
        - On `304`, skip; else upsert `Playlist` rows with `Playlist.ETag`.
    - For each playlist being reconciled:
        - Membership logic stays the same.
        - If you discover video IDs not yet in `Videos`, batch `videos.list`:
            - Persist `Video.ETag`, upsert row.
            - Compute `CommentsAllowed` for those new/changed videos.

3. **Channels:**
    - Wherever you currently fetch channel info, capture and persist the channel `ETag`.
    - Use it in subsequent channel fetches with `If-None-Match`.

4. **Error handling & idempotency:**
    - Do **not** update `CommentsAllowed` or `ETag` on partial failures for a given video; either complete the unit of
      work or leave previous values unchanged.
    - Maintain existing transactions boundaries around upsert + membership diff where present.

### D. Repository changes

Update repository contracts/implementations to support the new fields:

- `IVideoRepository.UpsertAsync(IEnumerable<Video> videos, ...)` → ensure it writes `ETag` and `CommentsAllowed`.
- `IPlaylistRepository.UpsertAsync(...)` → writes `ETag`.
- `IChannelRepository` (if present) or whichever persists `Channel` → writes `ETag`.

No new methods are needed; extend existing upsert logic and EF mappings.

### E. Quota & performance

- Only call `commentThreads.list` for videos **that changed in this run** (or are newly discovered).
- Use conditional requests (ETags) wherever supported to receive `304 Not Modified`.
- Keep batch size for `videos.list` at 50.

---

## Acceptance criteria

- [ ] New columns exist: `Channel.ETag`, `Playlist.ETag`, `Video.ETag`, `Video.CommentsAllowed`.
- [ ] During a sync, ETags from `channels.list`, `playlists.list`, and `videos.list` are captured and persisted.
- [ ] On subsequent syncs, requests include `If-None-Match` when an ETag is available. `304` responses avoid unnecessary
  processing.
- [ ] `Video.CommentsAllowed` is set for videos that are new or updated in the current sync:
    - [ ] `true` when `commentThreads.list` returns 200.
    - [ ] `false` when `commentThreads.list` returns 403 with `commentsDisabled`, or when `madeForKids`/
      `selfDeclaredMadeForKids` is true.
    - [ ] Not touched for videos that were not part of the current delta unless they had `null` previously and we
      already have enough info to set it without extra calls.
- [ ] Unit tests cover:
    - [ ] Mapping: EF columns exist and round-trip.
    - [ ] ETag: 304 path skips upsert; 200 path updates row and ETag.
    - [ ] Comments: Proper interpretation of 200/403(`commentsDisabled`) and kids flags.
- [ ] Integration test (happy path) for `channels.sync` proving:
    - [ ] Videos upserted with ETag and CommentsAllowed.
    - [ ] Playlists upserted with ETag.
    - [ ] Second run with same ETags yields fewer integration calls (simulate `304` in the mock).

---

## Code pointers (where to edit)

- `Tubester.Domain/*` → add properties to `Channel`, `Playlist`, `Video`.
- `Tubester.Persistence/TubesterDb.cs` → add EF property mappings; create migration.
- `Tubester.Integration/*` → surface `etag` from YouTube resources; set `IfNoneMatch` on subsequent calls.
- `Tubester.Application/*` (or where the sync orchestration lives) → integrate ETag persistence and comments
  detection into the existing delta steps.
- Tests under `Tubester.*Tests/*`.

---

## Dev notes

- Keep property names consistent: `ETag` (exact casing) and `CommentsAllowed`.
- For Sqlite, no special conversions are needed for strings/bools; keep DateTimeOffset conversions as-is.
- Guard for partial API outages: if `commentThreads.list` rate limits or fails, leave `CommentsAllowed` unchanged for
  that video and log a warning.

Please implement and open a PR titled:  
**feat(sync): persist ETags and detect video comments availability**
