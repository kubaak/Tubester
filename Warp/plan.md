implement a configurable credit system where “costly actions” deduct credits and monthly credits are granted per tier.
Action costs MUST be configurable in the database (no hardcoding).

Context / Goal

- Public SaaS credit system: users have a monthly credit allowance based on tier (plan).
- Performing certain actions deducts credits.
- Free tier has limited monthly usage (implemented via credits).
- Action “cost” is configurable in DB so we can change pricing without redeploying.

Key design decisions

- Use a ledger-based system (append-only) + wallet balance for fast reads.
- Charge at enqueue time for background jobs; charge before execution for synchronous endpoints.
- Ensure idempotency for charges (same action request/job should not double-charge).
- Default policy: “no carryover” (monthly reset sets balance to plan’s monthly credits). Make carryover a config flag
  later if needed.

Data model (Postgres + EF Core)

1) Plans table (or extend existing subscription/user plan tables)

- Plans
    - Id (PK)
    - Code (unique string e.g. Free/Pro)
    - Name
    - MonthlyCredits (int)
    - IsActive (bool)
    - CreatedAtUtc, UpdatedAtUtc

2) Subscriptions (or UserPlan)

- Subscriptions
    - UserId (PK/FK)
    - PlanId (FK)
    - PeriodStartUtc
    - PeriodEndUtc
    - Status (Active/Cancelled/etc)

3) Wallets (1:1 with user)

- Wallets
    - UserId (PK/FK)
    - Balance (int)
    - PeriodStartUtc
    - PeriodEndUtc
    - UpdatedAtUtc

4) LedgerEntries (append-only audit)

- LedgerEntries
    - Id (bigint identity PK)
    - UserId (FK)
    - OccurredAtUtc (timestamptz)
    - ActionType (string)
    - Delta (int)  // negative spend, positive grant/refund/adjustment
    - IdempotencyKey (string, unique)
    - ReferenceId (string? e.g. videoId/commentId/jobId)
    - MetadataJson (jsonb?; small, no secrets)

5) Action pricing table (NEW — configurable costs)

- ActionCosts
    - ActionType (PK string)  // e.g. CopyTemplateExecuted, AiTemplateEnqueued, AiReplyGenerated, ReplyPostedToYouTube
    - Cost (int)              // credits to deduct (>=0)
    - IsEnabled (bool)        // allow disabling billing for action
    - UpdatedAtUtc
    - Notes (string? optional)

Indexes / constraints

- Unique index on LedgerEntries.IdempotencyKey
- Indexes on LedgerEntries(UserId, OccurredAtUtc DESC), (ActionType, OccurredAtUtc DESC)
- Index on ActionCosts(IsEnabled) optional

Service layer
Create ICreditsService with:

- Task<int> GetBalanceAsync(userId)
- Task<bool> TrySpendAsync(userId, actionType, idempotencyKey, referenceId?, metadata?, ct)
- Task GrantMonthlyCreditsIfDueAsync(userId or all users) (used by monthly job)
  Rules:
- Costs are read from DB (ActionCosts). If missing row => treat as “not billable” OR fail fast (choose and
  document). Recommended: fail fast in prod to avoid unpriced costly actions.
- TrySpendAsync must be atomic + idempotent:
    - If idempotencyKey already exists => return success (do not double-charge)
    - Else:
        - load cost for actionType (must be enabled)
        - verify wallet period is current; if not, run/reset monthly grant first (or block and let job handle)
        - if Balance < Cost => return false (insufficient credits)
        - insert ledger row Delta = -Cost
        - update wallet Balance = Balance - Cost
- Use a DB transaction with proper isolation (or a single SQL UPDATE … WHERE Balance >= cost returning rowcount) to
  avoid race conditions.

Monthly refill / period management

- Add Hangfire recurring job (daily) “CreditPeriodMaintenanceJob”:
    - Finds users whose PeriodEndUtc <= now
    - Resets wallet period to new month
    - Sets Balance = Plan.MonthlyCredits (no carryover)
    - Inserts ledger row Delta = +MonthlyCredits with idempotencyKey like “grant:{userId}:{periodStart}”
- Also, on-demand: when TrySpendAsync is called and period is expired, perform the same reset in-line (so users aren’t
  blocked if the maintenance job didn’t run yet).

Where to charge

- Copy template (synchronous endpoint):
    - Before doing YouTube update, call TrySpendAsync(userId, CopyTemplateExecuted, idempotencyKey= “copy:{userId}:
      {source}:{target}:{timestamp/nonce}”, referenceId=targetVideoId)
    - If false => return 400/402-like response with message “Insufficient credits”
- AI template enqueue (async):
    - Charge before enqueueing Hangfire job.
    - idempotencyKey should include targetVideoId + some request id so retries don’t double charge.
- AI reply generation / other AI:
    - Charge at request time (before calling AI provider).
- ReplyPostedToYouTube:
    - Decide if it’s billable; if yes, charge before posting (or at approval time). Keep configurable by DB so can set
      cost=0.

Admin / configuration workflow

- Provide a simple seed migration or seeding step to populate ActionCosts with initial rows for:
    - CopyTemplateExecuted
    - AiTemplateEnqueued
    - AiTemplateSubmitted
    - AiReplyGenerated (if exists)
    - ReplyPostedToYouTube (likely cost=0 initially)
- Provide an admin-only endpoint OR manual SQL instructions (for now) to update costs.

Security / privacy

- Do not store prompts, token values, or full reply text in metadata.
- metadata should include only small operational info (counts/flags/length).

Deliverables

- Update plan.md with this design.
- Add EF entities + migrations for Plans/Subscriptions/Wallets/LedgerEntries/ActionCosts (or
  integrate with existing tables if present).
- Implement CreditsService with atomic, idempotent charging and monthly reset.
- Wire DI in API + Worker.
- Add charging calls to relevant endpoints/jobs.
- Add a Hangfire recurring maintenance job for monthly refill.
