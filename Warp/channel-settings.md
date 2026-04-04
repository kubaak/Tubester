You are working on the Tubester backend.

Tech stack:
- ASP.NET Core
- EF Core
- PostgreSQL
- Hangfire

Existing system already has:
- channels
- YouTube comment loading/synchronization
- AI reply suggestion generation
- background jobs
- authenticated users owning channels

Goal:
Implement backend-only support for per-channel comment assistant settings.

Focus on:
- domain/entity model
- EF Core configuration
- migration
- API endpoints
- validation
- authorization
- integration into existing background processing / services

Main requirement:
There should be a single channel-level toggle:
- IsCommentAssistantEnabled

When this is OFF:
- do not load comments from YouTube for that channel
- do not generate AI reply suggestions for that channel

Also add these per-channel settings:
- IsSuggestRepliesForTopLevelCommentsOnly
- MaxSuggestedRepliesPerSync
- MaxCommentAgeDays
- ReplyLanguage
- ResponseForNonTextualComments

Data model
Add a new per-channel settings entity/table.

Fields:
- ChannelId
- IsCommentAssistantEnabled : bool
- IsSuggestRepliesForTopLevelCommentsOnly : bool
- MaxSuggestedRepliesPerSync : int
- MaxCommentAgeDays : int
- ReplyLanguage : string
- ResponseForNonTextualComments : string?
- UpdatedAtUtc

Design constraints:
- one settings row per channel
- unique constraint on ChannelId
- proper FK to Channel
- string max lengths:
    - ReplyLanguage max length 50
    - ResponseForNonTextualComments max length 500
- use naming/style consistent with current codebase
- add EF configuration explicitly

Defaults
Use these defaults for newly created or auto-created settings:
- IsCommentAssistantEnabled = false
- IsSuggestRepliesForTopLevelCommentsOnly = true
- MaxSuggestedRepliesPerSync = 10
- MaxCommentAgeDays = 10
- ReplyLanguage = "English"
- ResponseForNonTextualComments = null

Behavior rules
- If IsCommentAssistantEnabled = false, comment loading and AI reply suggestion generation must both be skipped
- Respect MaxSuggestedRepliesPerSync
- Respect MaxCommentAgeDays
- If a comment is non-textual and ResponseForNonTextualComments has a value, use that value as the suggested/default reply behavior in the most minimal way that fits current architecture
- If ResponseForNonTextualComments is null or whitespace, preserve current behavior or skip such comments, whichever is more consistent with existing implementation

API
Implement backend endpoints only.

Suggested shape:
- GET /api/channels/{channelId}/settings
- PUT /api/channels/{channelId}/settings

Requirements:
- GET returns channel settings
- if settings do not exist yet, create them with defaults and return them
- PUT fully updates settings
- only channel owner can GET or PUT settings
- use existing auth/ownership patterns in the project

Validation
Implement server-side validation:
- MaxSuggestedRepliesPerSync: 0 to int.max
- MaxCommentAgeDays: 0 to int.max
- ReplyLanguage: required, trimmed, max 50
- ResponseForNonTextualComments: optional, trimmed, max 500
- reject invalid payloads using the project’s standard validation/error response style

Persistence / timestamps
- update UpdatedAtUtc on every change
- keep timestamp handling consistent with the rest of the project

Background processing integration
Update existing backend jobs/services so they respect the new channel settings.

Expected behavior:
- skip processing with structured logs when comment assistant is disabled
- only process eligible comments according to:
    - top-level-only setting
    - minimum comment age
    - max suggestions per sync
- ensure comment sync path checks settings before fetching/loading comments
- ensure reply suggestion generation path checks settings before generating suggestions

Logging
Add structured logs with channel id and skip reason for key skip scenarios:
- comment assistant disabled
- comment not eligible because not top-level
- comment not eligible because too new
- max suggestions reached

Implementation guidance
- keep it minimal and production-sensible
- avoid overengineering
- prefer creating a dedicated ChannelSettings entity/table rather than bloating Channel
- default settings should be created during SyncCurrentChannelAsync
- integrate with existing patterns instead of introducing a new architectural style
- add tests if aligning with the existing backend test conventions