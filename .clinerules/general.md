# Tubester Workspace Rules

## Workspace Structure

This workspace contains two separate repositories that form one product:

- `Tubester/` = backend API, worker, domain, persistence, integrations
- `Tubester-Client/` = frontend React application

Treat the product as one system with two codebases.

## Primary Goal

Produce production-ready changes that are:

- correct
- minimal
- maintainable
- consistent with the existing architecture
- easy to review
- easy to test
- safe to deploy

Prefer robust, boring solutions over clever solutions.

## How to Work

Before changing code:

1. Identify whether the task is backend-only, frontend-only, or full-stack.
2. Read the relevant files first.
3. Reuse existing project patterns before introducing new ones.
4. Limit changes to the smallest correct surface area.

Always optimize for:

- consistency
- predictability
- reviewability
- low regression risk

## Cross-Repo Discipline

Do not modify both repos unless necessary.

When changing backend contracts, always check frontend impact:

- endpoint routes
- request body shape
- query parameter names
- response DTO shape
- enum values
- nullability
- validation behavior
- pagination and filtering semantics
- authentication/authorization behavior
- error response shape

When changing frontend behavior that depends on the API, verify the backend contract first.

## Backend/Frontend Contract Safety

Treat API contract stability as important.

Rules:

- preserve backward compatibility unless the task explicitly allows breaking changes
- if a breaking change is necessary, update all affected frontend code
- do not silently change semantics
- do not change DTO shape casually
- do not change enum values casually
- do not change paging tokens, default page sizes, or filter behavior without checking all callers

## Change Philosophy

Prefer:

- small, focused diffs
- explicit code
- incremental refactors
- preserving established patterns

Avoid:

- broad rewrites
- speculative abstractions
- dependency churn
- style-only churn
- unrelated cleanup

## Dependency Policy

Do not add new libraries unless there is a strong reason.

Before adding a dependency:

- check whether the project already has an established solution
- prefer platform/framework capabilities
- prefer lightweight solutions
- justify the addition briefly

## Security

Never weaken security for convenience.

Be careful around:

- OAuth flows
- tokens
- cookies
- redirect URLs
- auth scopes
- permission checks
- user input
- HTML rendering
- file handling
- external webhooks/integrations

Do not hardcode secrets, credentials, or private URLs.

## Reliability and Performance

Prefer solutions that:

- preserve cancellation where relevant
- avoid unnecessary database round trips
- avoid unnecessary API calls
- avoid N+1 patterns
- fail predictably
- log meaningful context
- keep behavior deterministic

## File Hygiene

Keep diffs clean:

- no unrelated renames
- no formatting churn unless needed
- no dead code left behind
- no commented-out code
- no placeholder TODOs unless explicitly requested

## Communication Expectations

When completing a task:

- state assumptions when relevant
- call out risks
- summarize impacted files
- mention what should be tested manually
- do not claim something was tested unless it actually was tested

## Workspace-Specific Guidance

Tubester is an AI-assisted SaaS product for YouTube creators.
The backend and frontend are tightly connected.
When in doubt:

- prefer shared-root awareness
- prefer repository-local conventions
- preserve generated-client friendliness
- preserve production-ready SaaS UX quality

## Ignored Files

Respect `.clineignore`. Do not inspect or rely on ignored, generated, build, dependency, or secret files unless the task explicitly requires it.
