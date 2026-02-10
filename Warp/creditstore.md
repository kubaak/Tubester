Introduce a dedicated persistence abstraction for the credit system: ICreditsStore.

Goal

- Stop injecting TubesterDb directly into CreditsService.
- Keep CreditsService orchestration in Application, but move DB-specific operations (wallet/ledger/cost/plan queries,
  transactions) into a Persistence implementation.
- The store must support atomic, idempotent spending and monthly reset/grant operations.

Architecture / placement

- Create interface `ICreditsStore` in `Tubester.Abstractions.Billing` (or `Tubester.Abstractions.Credits` — pick
  the best existing namespace pattern).
- Implement it in `Tubester.Persistence.Credits` (e.g. `EfCreditsStore`) using `TubesterDb`.
- Register in DI in API + Worker:
    - `services.AddScoped<ICreditsStore, EfCreditsStore>();`
- Update `CreditsService` to depend on `ICreditsStore` instead of `TubesterDb`.

ICreditsStore: required methods (minimal but sufficient)

1) Idempotency check (ledger)

- Task<bool> LedgerEntryExistsAsync(string idempotencyKey, CancellationToken ct);

2) Read action cost (configurable in DB)

- Task<CreditActionCostDto?> GetActionCostAsync(string actionType, CancellationToken ct);
    - returns Cost + IsEnabled (and maybe UpdatedAtUtc)

3) Wallet / period access

- Task<UserCreditWalletDto?> GetWalletAsync(string userId, CancellationToken ct);
- Task UpsertWalletAsync(UserCreditWalletDto wallet, CancellationToken ct);

4) Plan/subscription access (needed for monthly grants/resets)

- Task<UserPlanDto?> GetActiveUserPlanAsync(string userId, DateTimeOffset nowUtc, CancellationToken ct);
    - returns PlanId/PlanCode/MonthlyCredits and current period start/end (or enough to compute)

5) Atomic spend (core)
   Implement ONE atomic operation which performs:

- verify wallet exists + period current (or caller ensures reset)
- verify balance >= cost
- insert ledger entry (Delta = -cost) with unique idempotencyKey
- update wallet balance
  Return result describing whether spending succeeded and new balance.

Suggested signature:

- Task<SpendResult> TrySpendAsync(
  string userId,
  string actionType,
  int cost,
  string idempotencyKey,
  string? referenceId,
  string? metadataJson,
  DateTimeOffset occurredAtUtc,
  CancellationToken ct);

SpendResult should include:

- bool Succeeded
- int? NewBalance
- bool WasDuplicate (if idempotencyKey already existed)
  (If WasDuplicate=true treat as Succeeded=true and do NOT double charge)

6) Monthly reset/grant (period maintenance)

- Task<GrantResult> GrantMonthlyCreditsAsync(
  string userId,
  DateTimeOffset periodStartUtc,
  DateTimeOffset periodEndUtc,
  int monthlyCredits,
  string idempotencyKey,
  DateTimeOffset occurredAtUtc,
  CancellationToken ct);
  This should:
- be idempotent via idempotencyKey
- set wallet balance according to policy (NO carryover): Balance = monthlyCredits
- insert ledger entry Delta = +monthlyCredits

Implementation details (EfCreditsStore)

- Use a DB transaction.
- Enforce UNIQUE index on ledger.IdempotencyKey and rely on it for concurrency safety.
- When TrySpendAsync is called concurrently:
    - either:
        - check idempotencyKey first
        - then attempt insert/update
    - or attempt insert and handle unique constraint violation to mark WasDuplicate.
- Wallet update should be race-safe:
    - Prefer single SQL `UPDATE ... WHERE Balance >= cost` with rowcount check, OR use optimistic concurrency token (
      xmin) if you already use that pattern.
- Serialize metadata in Application (CreditsService) using System.Text.Json, store as jsonb.
- Do not store secrets/prompt text.

CreditsService refactor

- CreditsService responsibilities:
    - determine actionType
    - load cost via store (or store method that returns cost)
    - compute period and if reset needed call GrantMonthlyCreditsAsync
    - call TrySpendAsync with idempotencyKey/referenceId/metadata
    - return domain-level result (insufficient credits / ok / duplicate)
- CreditsService no longer uses TubesterDb directly.

Testing

- Update integration tests to use real Postgres and verify:
    - idempotencyKey does not double-charge
    - concurrent spends cannot make balance negative
    - monthly grant is idempotent
- Provide a simple fake/in-memory ICreditsStore for unit tests only if needed, but integration tests are preferred.

Deliverables

- ICreditsStore interface + DTOs/results
- EfCreditsStore implementation using TubesterDb
- DI registration in API + Worker
- CreditsService updated to use store
- Migration ensures UNIQUE constraint on ledger idempotency key (if not already added)
