## Scope
- Add **GET /api/videos** with **cursor-based paging** (`pageToken`) optional **title** filter (`?title=`) 
and optional **visibility** filter.
- Sort by **PublishedAt DESC**, then **VideoId DESC** (tiebreaker).
- Use **Base64url("{publishedAt:o}|{videoId}")** for the page token.
- **DTOs**
    - `VideoListItemDto { string VideoId, string Title, DateTimeOffset PublishedAt }`
    - `PagedResult<T> { IReadOnlyList<T> Items, string? NextPageToken }`
- **Layers**
    - Controller → **Tubester.Api** (`VideosController`)
    - Service → **Tubester.Application** (`IVideoService` + implementation)
    - Repository → **Tubester.Persistence** (`IVideoRepository` + implementation)
- Repository query rules:
    - `AsNoTracking()`
    - Apply **title filter (case-insensitive)** **before** pagination
    - Cursor predicate: return items **strictly earlier** than the token:
        - `PublishedAt < T` **OR** (`PublishedAt = T` **AND** `VideoId < V`)
    - `Take(pageSize + 1)`; if extra exists, set `NextPageToken` from the first `pageSize` items
- Validate `pageSize` (default **30**, min **1**, max **100**).
- Return **400** for malformed `pageToken`.

### Parameter Visibility
- Name: visibility
- Type: optional VideoVisibility[] array. Accept: Public, Unlisted, Private, Scheduled.
- Multi-select: allow repeated keys (?visibility=Public&visibility=Unlisted) or comma-separated (?visibility=Public,Unlisted). Case-insensitive.
- If omitted → no visibility filter (all videos).
- Bind the pageToken to the current filter
- If provided → filter to the set. Invalid enum values automatically return 400 via model binding.

## Implementation Decisions
1. Title filter: case-insensitive substring match (contains). SQLite implementation via `EF.Functions.Like` with NOCASE collation.
2. Ordering: primary by `PublishedAt` descending, tie-breaker `VideoId` descending for deterministic order.
3. Cursor/page token: URL-safe Base64 of the UTF-8 string `"{publishedAt:o}|{videoId}"`. On decode, any failure or invalid shape yields HTTP 400 from the API layer.
4. Pagination: `pageSize` default 30 if omitted, valid range 1..100 inclusive. If outside range, return HTTP 400 with a validation message.
5. Page retrieval: repository fetches `pageSize + 1` items to detect `hasMore`. Response returns only first `pageSize`. `NextPageToken` comes from the last item included in the response (index `pageSize - 1`).
6. Cursor semantics: "strictly earlier" in the sort order. Predicate is `(v.PublishedAt < token.PublishedAt) OR (v.PublishedAt == token.PublishedAt AND v.VideoId < token.VideoId)` where comparisons align to the descending order used.
7. Indexes: composite index on `(PublishedAt, VideoId)` and a NOCASE title index for performance.
8. Swagger: enable XML documentation in Api project so action XML comments appear in Swagger UI.
9. Visibility filtering: include all videos regardless of visibility (`Public`/`Unlisted`/`Private`/`Scheduled`).
10. DTO includes only `VideoId`, `Title`, `PublishedAt` - no `ThumbnailUrl` or `Duration`.

## Deliverables
1. New interfaces and implementations (service).
2. Controller action with XML/Swagger docs.
3. Index migration: composite index on `(PublishedAt DESC, VideoId DESC)` and case-insensitive index for `Title`.

## API Documentation

### GET /api/videos

Returns a paginated list of videos with optional title filtering.

#### Query Parameters
- `title` (string, optional): Case-insensitive substring filter for video titles
- `pageSize` (integer, optional): Number of items per page (1-100, default: 30)
- `pageToken` (string, optional): Cursor token for pagination

#### Response Schema
```json
{
  "items": [
    {
      "videoId": "string",
      "title": "string",
      "publishedAt": "2023-10-20T13:20:14Z"
    }
  ],
  "nextPageToken": "string" // null if last page
}
```

#### Examples
- `GET /api/videos` - First page with default size (30)
- `GET /api/videos?pageSize=5` - First page with 5 items
- `GET /api/videos?title=react` - Filter videos with "react" in title
- `GET /api/videos?pageToken=MjAyMy0xMC0yMFQxMzoyMDoxNFp8dmlkZW9faWQ` - Next page

#### Error Responses
- `400 Bad Request` when pageSize is outside valid range (1-100)
- `400 Bad Request` when pageToken is malformed

#### Page Token Format
Tokens are URL-safe Base64 encoded strings containing `"{publishedAt:o}|{videoId}"` for cursor-based pagination.

## Implementation Notes

### Database Indexes
1. **Composite Index**: `IX_Videos_PublishedAt_VideoId` on `(PublishedAt, VideoId)` for efficient sorting and pagination
2. **Title Index**: `IX_Videos_Title_NOCASE` using SQLite's NOCASE collation for case-insensitive title filtering

The NOCASE title index is created via raw SQL in the migration because EF Core has limited fluent API support for SQLite collations in index definitions.

### Cursor Pagination
- Uses "strictly earlier" semantics: `(PublishedAt < cursor.PublishedAt) OR (PublishedAt = cursor.PublishedAt AND VideoId < cursor.VideoId)`
- Deterministic ordering by `PublishedAt DESC, VideoId DESC`
- Fetches `pageSize + 1` items to detect if more pages exist

## Assumptions
1) Title filter semantics:
   case-insensitive substring match (i.e., Title contains ?title)
   For SQLite, I plan to use a case-insensitive substring filter, and, if we add an index, use COLLATE NOCASE (or an index on lower(Title)).

2) pageSize validation behavior:
   •  If outside [1, 100], return 400

3) pageToken format details:
   •  encode as URL-safe base64 (replace + with -, / with _, trim = padding), using UTF-8 for the raw string "{publishedAt:o}|{videoId}".
   •  On decode, any failure or invalid shape (no pipe, bad date) will produce 400.

4) NextPageToken source item:
   - If you get ≤ pageSize back → there’s no next page, so nextPageToken = null.
   - If you get pageSize + 1 back → there is a next page.
   - You return only the first pageSize items to the client and derive the token from the last returned item. The extra “look-ahead” item is just a signal that more exists.

5) Visibility filtering:
   Visibility parameter

6) DTO shaping:
   - VideoListItemDto includes VideoId, Title, PublishedAt, ThumbnailUrl

7) Indexes (SQLite):
   - Composite index: Videos(PublishedAt DESC, VideoId DESC)
   - Title index with NOCASE collation for case-insensitive filter performance

8) Swagger/XML docs:
    XML doc generation turned on in the Api csproj so the action’s XML comments show in Swagger
