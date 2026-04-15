# Tubester Backend Rules

## Repository Role

This repository contains the backend for Tubester.

Main projects and responsibilities:

- `Tubester.Api` = HTTP API, auth, swagger, app wiring, endpoint layer
- `Tubester.Application` = application services, use-case orchestration, contracts, jobs, options, exceptions
- `Tubester.Domain` = core domain concepts and domain events
- `Tubester.Persistence` = EF Core persistence, PostgreSQL, migrations, data access implementation
- `Tubester.Integration` = external provider integration code, external DTOs, provider configuration
- `Tubester.Worker` = background processing host
- `Tubester.Migrator` = migration/apply-schema executable
- `Tubester.Abstractions` = shared contracts/interfaces grouped by business area
- `tests/Tubester.IntegrationTests` = integration tests, OpenAPI tests, test host, domain event tests

Respect these boundaries. Do not blur project responsibilities casually.

## Primary Goal

Produce production-ready backend changes that are:

- correct
- explicit
- maintainable
- easy to review
- safe to deploy
- friendly to the generated frontend client

Prefer simple, robust, boring solutions over clever solutions.

## Architectural Boundaries

### Tubester.Api

Use `Tubester.Api` for:

- controllers/endpoints
- auth wiring
- swagger/openapi configuration
- DI registration and app startup wiring
- endpoint-specific infrastructure

Do not place business logic or EF Core query logic in controllers.

Controllers should stay thin:

- receive request
- validate/delegate validation
- call application layer
- map result to HTTP response

### Tubester.Application

Use `Tubester.Application` for:

- business use-case orchestration
- application services
- request handling logic
- contracts/DTOs used across layers
- jobs and background workflow orchestration
- application exceptions
- options/configuration objects

This is the main place for use-case behavior.
Do not move domain-independent orchestration into controllers or persistence classes.

### Tubester.Domain

Use `Tubester.Domain` for:

- core domain concepts
- invariants
- domain events
- domain-level behavior that should not depend on transport or persistence

Keep domain code clean.
Do not put HTTP concerns, EF concerns, or provider concerns in the domain layer.

### Tubester.Persistence

Use `Tubester.Persistence` for:

- EF Core DbContext and mappings
- PostgreSQL persistence concerns
- migrations
- repository/data access implementation
- query/update logic that belongs to persistence

Do not place HTTP or UI-facing concerns here.
Do not put external provider integration logic here.

### Tubester.Integration

Use `Tubester.Integration` for:

- external API/provider communication
- external DTOs
- provider-specific exceptions
- provider-specific configuration

Keep provider logic isolated here.
Do not leak provider-specific assumptions throughout the rest of the codebase.

### Tubester.Worker

Use `Tubester.Worker` for:

- background host bootstrapping
- job execution hosting
- worker-specific configuration

Keep worker entry-point concerns separate from job/business logic.

### Tubester.Migrator

Use `Tubester.Migrator` for:

- migration startup logic
- schema migration execution

Do not mix normal app runtime behavior into the migrator.

### Tubester.Abstractions

Use `Tubester.Abstractions` for:

- shared contracts/interfaces grouped by business area
- cross-project abstractions already established in the solution

Do not dump arbitrary models here.
Keep abstractions purposeful and stable.

## Feature Organization

Business areas are organized by feature folders such as:

- `Account`
- `Analytics`
- `Auth`
- `Channels`
- `Credits`
- `DomainEvents`
- `Playlists`
- `Replies`
- `Users`
- `Videos`

When adding or changing code:

- follow the existing feature grouping
- keep feature-related code together
- prefer extending an existing feature area over inventing a new cross-cutting folder without reason

## API Contract Discipline

The frontend depends on generated API contracts.
Treat OpenAPI/schema stability as a first-class concern.

Rules:

- keep request DTOs explicit
- keep response DTOs explicit
- keep content types explicit
- preserve route consistency
- preserve query parameter naming consistency
- do not casually change nullability
- do not casually change enum semantics
- do not introduce undocumented response shapes
- preserve predictable error contracts

When changing an endpoint, always think about:

- generated client impact
- frontend runtime impact
- filter/query compatibility
- paging compatibility
- auth flow impact

## Validation

Validate inputs clearly at the boundary.

Rules:

- reject invalid requests early
- normalize inputs only when that matches current project behavior
- keep validation behavior predictable
- do not silently reinterpret invalid data
- keep trimming/normalization consistent across similar endpoints

## Error Handling

Use exceptions intentionally.

Rules:

- do not swallow exceptions
- preserve useful context
- avoid duplicate logging
- prefer centralized error handling patterns where established
- do not use exceptions as normal control flow unless that pattern already exists in the area

## Logging and Observability

Use structured logging with meaningful context.

Prefer logs that include:

- operation name
- entity identifiers
- user/channel/video/reply identifiers when relevant
- job identifiers
- provider identifiers for integration calls
- failure reason

Avoid:

- noisy logs
- duplicate logs for the same failure
- logging secrets/tokens
- meaningless logs

## Async and Cancellation

Use async properly.

Rules:

- use async EF Core APIs
- pass `CancellationToken` through call chains where supported
- do not block on async code
- do not introduce fire-and-forget work unless explicit infrastructure supports it
- do not wrap sync code in fake async without reason

## EF Core and PostgreSQL Discipline

EF Core is the primary persistence technology.
PostgreSQL is the database.

Rules:

- use EF Core consistently
- prefer readable LINQ
- project only the data needed
- avoid unnecessary `Include`
- prefer projection for list endpoints
- keep filtering/paging/order explicit
- keep transaction boundaries intentional
- think about concurrency and idempotency when updating data
- consider PostgreSQL semantics, not SQL Server assumptions

Be mindful of:

- `ILIKE` / case-sensitivity behavior where relevant
- timestamp/timezone correctness
- migration safety
- enum storage implications
- Npgsql translation behavior
- index friendliness of queries

Do not suggest Dapper or SQL Server-specific patterns unless explicitly requested.

## Pagination, Filtering, and Search

This codebase includes list/search flows for areas such as replies and videos.
These must remain predictable.

Rules:

- preserve existing paging semantics
- preserve page token behavior where used
- preserve default/max page size behavior unless explicitly changing it
- keep filtering explicit and composable
- avoid silent ordering changes
- avoid accidental behavior drift in search/list endpoints

## Background Jobs and Domain Events

The repository contains:

- application jobs
- domain events and handlers
- worker execution paths

When changing background or event-driven flows:

- think about retry behavior
- think about duplicate execution risk
- think about idempotency
- think about partial failures
- preserve observability

Do not introduce hidden side effects that become dangerous under retries.

## External Integrations

External systems are unreliable.

Rules:

- isolate provider-specific logic in `Tubester.Integration`
- handle timeouts/failures explicitly
- handle unexpected payloads
- avoid spreading provider DTOs across unrelated layers
- translate provider concerns into app-friendly contracts where appropriate

## Testing Expectations

There is a dedicated integration test project under `tests/Tubester.IntegrationTests`, including areas like:

- `OpenApi`
- `TestHost`
- `DomainEvents`

Prefer adding tests for:

- endpoint contract behavior
- validation behavior
- paging/filtering behavior
- bug fixes
- openapi/schema-sensitive behavior
- important integration flows

Keep tests:

- deterministic
- readable
- behavior-focused
- aligned with the existing test host/integration approach

## Code Style

Prefer:

- small focused methods
- descriptive names
- clear guard clauses
- straightforward control flow
- explicitness over magic

Avoid:

- giant service classes
- giant methods
- deep nesting
- boolean soup
- speculative abstractions
- introducing a new architecture style for one change

## Safe Change Policy
- never stage your changes in git
- 
When fixing bugs:

- make the smallest correct fix
- preserve existing behavior unless that behavior is the bug
- avoid unrelated refactors

When adding features:

- implement the requested scope only
- preserve project boundaries
- do not future-framework the solution

## Tubester-Specific Mindset

This backend serves an AI-assisted SaaS product for YouTube creators.
Important qualities:

- predictable API contracts
- stable generated-client compatibility
- reliable background processing
- clean feature boundaries
- production-readiness over cleverness

## Ignored Files

Respect `.clineignore`. Do not inspect or rely on ignored, generated, build, dependency, or secret files unless the task explicitly requires it.
