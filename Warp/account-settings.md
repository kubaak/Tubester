You are working on the Tubester backend.

Tech stack:
- ASP.NET Core
- EF Core
- PostgreSQL

Important context:
This feature should follow the same architectural concept and implementation style as Channel Settings.
Reuse the same patterns wherever it makes sense:
- dedicated settings entity/table
- one row per owner
- lazy creation with defaults
- service layer similar to channel settings service
- authenticated current-context resolution instead of passing arbitrary ids from the client
- GET + PUT endpoints
- DTO/request/response style aligned with Channel Settings
- EF configuration style aligned with Channel Settings
- migration style aligned with Channel Settings

Goal:
Implement backend-only support for Account Settings for the current authenticated user.

Focus on:
- entity/model
- EF Core configuration
- migration
- API endpoints
- validation
- current user resolution
- subscription read model integration

Main requirement:
The authenticated user should be able to:
- view their account settings
- update their preferred theme
- update their preferred language
- see their current subscription summary

Important product distinction:
- subscription is read-only in this feature
- theme and language are editable preferences
- do not implement direct mutation of billing/subscription state here

Design approach
Implement this very similarly to Channel Settings.

That means:
- create a dedicated AccountSettings entity/table
- key it by UserId
- one row per user
- if settings do not exist yet, create them automatically with defaults
- keep the controller/service flow very similar to Channel Settings, but for current authenticated user instead of current channel

Data model
Add an AccountSettings entity/table.

Fields:
- UserId
- PreferredTheme : string
- PreferredLanguage : string
- UpdatedAtUtc

Constraints:
- primary key on UserId
- FK to Users(Id) with cascade delete
- explicit EF configuration
- PreferredTheme max length 50
- PreferredLanguage max length 20

Defaults
Use these defaults:
- PreferredTheme = "system"
- PreferredLanguage = "en"

Behavior:
- if settings do not exist yet, create them with defaults and return them
- follow the same get-or-create pattern as Channel Settings

Subscription handling
GET account settings should also return the current subscription summary for the current user.

Subscription should be read-only in this feature.
Reuse existing subscription entities/services/repositories.
Do not invent direct subscription editing.

Suggested subscription summary fields:
- PlanCode
- PlanName
- Status
- PeriodStartUtc
- PeriodEndUtc
- MonthlyCredits

API
Implement current-user endpoints only.

Suggested endpoints:
- GET /api/account/settings
- PUT /api/account/settings

Behavior:
- GET returns account settings plus subscription summary
- PUT updates editable preferences only:
    - PreferredTheme
    - PreferredLanguage

Do not accept userId in route or body.
Resolve the current user from the authenticated context, similar to how Channel Settings resolves current channel context.

Validation
Implement server-side validation:
- PreferredTheme is required, trimmed, max length 50
- PreferredLanguage is required, trimmed, max length 20

PreferredTheme:
- use allowed values:
    - light
    - dark
    - system

PreferredLanguage:
- accept language code string
- examples:
    - en
    - es
    - cs
    - pt-BR

Authorization / identity
- endpoint must require authentication
- resolve current user from existing auth/current-user abstraction
- do not require userId from client
- only current authenticated user can access/update their own account settings

Implementation details
Please implement:
- AccountSettings entity/model
- DbSet
- EF Core configuration
- migration
- DTOs / request / response models
- service interface + implementation, following the same concept as Channel Settings
- controller matching the style of Channel Settings, but using current user instead of channel id
- subscription summary mapping

Persistence / timestamps
- set CreatedAtUtc on insert
- update UpdatedAtUtc on every change
- keep timestamp handling consistent with the rest of the project

Implementation guidance
- follow existing project structure and naming conventions
- keep this feature as parallel as possible to Channel Settings
- reuse similar naming and patterns where sensible
- keep it minimal and production-sensible
- avoid overengineering
- add integration tests covering both endpoints and verifying the resultset

Suggested names
Use these names consistently unless there is a strong reason not to:
- AccountSettings
- AccountSettingsDto
- UpdateAccountSettingsRequest
- SubscriptionSummaryDto
- IAccountSettingsService
- AccountSettingsService