using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tubester.Abstractions.Credits;

namespace Tubester.Persistence.Credits;

public sealed class CreditsStore(TubesterDb databaseContext) : ICreditsStore
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<CreditActionCostDto?> GetActionCostAsync(string actionType, CancellationToken cancellationToken)
    {
        //todo cache
        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Action type is required.", nameof(actionType));
        }

        var creditActionCost = await databaseContext.ActionCosts
            .AsNoTracking()
            .FirstOrDefaultAsync(cost => cost.ActionType == actionType, cancellationToken);

        if (creditActionCost is null)
        {
            return null;
        }

        var result = new CreditActionCostDto
        {
            ActionType = creditActionCost.ActionType,
            Cost = creditActionCost.Cost,
            IsEnabled = creditActionCost.IsEnabled,
            UpdatedAtUtc = creditActionCost.UpdatedAtUtc
        };

        return result;
    }

    public async Task<WalletDto?> GetWalletAsync(string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var walletEntity = await databaseContext.Wallets
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.UserId == userId, cancellationToken);

        if (walletEntity is null)
        {
            return null;
        }

        var wallet = new WalletDto
        {
            UserId = walletEntity.UserId,
            Balance = walletEntity.Balance,
            PeriodStartUtc = walletEntity.PeriodStartUtc,
            PeriodEndUtc = walletEntity.PeriodEndUtc,
            UpdatedAtUtc = walletEntity.UpdatedAtUtc
        };

        return wallet;
    }

    public async Task<IReadOnlyList<string>> GetUserIdsWithExpiredWalletsAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var userIds = await databaseContext.Wallets
            .AsNoTracking()
            .Where(wallet => wallet.PeriodEndUtc <= nowUtc)
            .Select(wallet => wallet.UserId)
            .ToListAsync(cancellationToken);

        return userIds;
    }

    public async Task<UserPlanDto?> GetActiveUserPlanAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var subscription = await databaseContext.Subscriptions
            .AsNoTracking()
            .Include(entity => entity.Plan)
            .Where(entity => entity.UserId == userId
                             && entity.Status == SubscriptionStatus.Active
                             && entity.PeriodStartUtc <= nowUtc
                             && entity.PeriodEndUtc > nowUtc)
            .OrderByDescending(entity => entity.PeriodStartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null || !subscription.Plan.IsActive)
        {
            return null;
        }

        var result = new UserPlanDto
        {
            UserId = subscription.UserId,
            PlanId = subscription.PlanId,
            PlanCode = subscription.Plan.Code,
            PeriodCredits = subscription.Plan.MonthlyCredits,
            PeriodStartUtc = subscription.PeriodStartUtc,
            PeriodEndUtc = subscription.PeriodEndUtc
        };

        return result;
    }

    public async Task<SpendResult> TrySpendAsync(
        string userId,
        string actionType,
        int cost,
        string idempotencyKey,
        string? referenceId,
        string? metadataJson,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Action type is required.", nameof(actionType));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        if (cost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cost), "Cost cannot be negative.");
        }

        // Ensure UTC offset for Npgsql timestamptz (avoid Offset != 0 issues)
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            occurredAtUtc = occurredAtUtc.ToUniversalTime();
        }

        await using var tx = await databaseContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // 1) Idempotency gate (requires UNIQUE index on IdempotencyKey)
        var delta = -cost;

        var inserted = await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""

             INSERT INTO "LedgerEntries"
                 ("UserId", "OccurredAtUtc", "ActionType", "Delta", "IdempotencyKey", "ReferenceId", "MetadataJson")
             VALUES
                 ({userId}, {occurredAtUtc}, {actionType}, {delta}, {idempotencyKey}, {referenceId}, {metadataJson}::jsonb)
             ON CONFLICT ("UserId","IdempotencyKey") DO NOTHING;

             """, ct);

        if (inserted == 0)
        {
            await tx.CommitAsync(ct);

            var bal = await databaseContext.Wallets
                .AsNoTracking()
                .Where(w => w.UserId == userId)
                .Select(w => (int?)w.Balance)
                .SingleOrDefaultAsync(ct);

            return new SpendResult
            {
                Succeeded = true,
                WasDuplicate = true,
                NewBalance = bal,
                FailureReason = SpendFailureReason.None
            };
        }

        // 2) Atomic wallet update (spend OR fail)

        await using var cmd = databaseContext.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = databaseContext.Database.CurrentTransaction!.GetDbTransaction();

        cmd.CommandText = """
                              UPDATE "Wallets"
                              SET "Balance" = "Balance" - @cost,
                                  "UpdatedAtUtc" = @occurredAtUtc
                              WHERE "UserId" = @userId
                                AND "PeriodEndUtc" > @occurredAtUtc
                                AND "Balance" >= @cost
                              RETURNING "Balance"
                          """;

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;
        var pCost = cmd.CreateParameter();
        pCost.ParameterName = "cost";
        pCost.Value = cost;
        var pAt = cmd.CreateParameter();
        pAt.ParameterName = "occurredAtUtc";
        pAt.Value = occurredAtUtc;

        cmd.Parameters.Add(pUser);
        cmd.Parameters.Add(pCost);
        cmd.Parameters.Add(pAt);

        var result = await cmd.ExecuteScalarAsync(ct);
        var newBalance = result == null ? (int?)null : Convert.ToInt32(result);

        if (newBalance is not null)
        {
            await tx.CommitAsync(ct);
            return new SpendResult
            {
                Succeeded = true,
                WasDuplicate = false,
                NewBalance = newBalance,
                FailureReason = SpendFailureReason.None
            };
        }

        // 3) Spend failed => rollback so the inserted ledger row disappears
        await tx.RollbackAsync(ct);

        // One small read to classify reason + return current balance
        var walletState = await databaseContext.Wallets
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => new { w.Balance, w.PeriodEndUtc })
            .SingleOrDefaultAsync(ct);

        if (walletState is null)
        {
            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = null,
                FailureReason = SpendFailureReason.NoWallet
            };
        }

        if (walletState.PeriodEndUtc <= occurredAtUtc)
        {
            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = walletState.Balance,
                FailureReason = SpendFailureReason.WalletExpired
            };
        }

        return new SpendResult
        {
            Succeeded = false,
            WasDuplicate = false,
            NewBalance = walletState.Balance,
            FailureReason = SpendFailureReason.InsufficientBalance
        };
    }

    public async Task<GrantResult> GrantPeriodCreditsAsync(
        string userId,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        int periodCredits,
        string idempotencyKey,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (periodCredits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(periodCredits), "Period credits cannot be negative.");
        }

        // Ensure UTC for timestamptz
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            occurredAtUtc = occurredAtUtc.ToUniversalTime();
        }

        if (periodStartUtc.Offset != TimeSpan.Zero)
        {
            periodStartUtc = periodStartUtc.ToUniversalTime();
        }

        if (periodEndUtc.Offset != TimeSpan.Zero)
        {
            periodEndUtc = periodEndUtc.ToUniversalTime();
        }

        await using var tx = await databaseContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var metadataJson =
            JsonSerializer.Serialize(new { type = "period_grant", periodStartUtc, periodEndUtc, periodCredits },
                _jsonSerializerOptions);

        // 1) Idempotency gate (insert ledger once)
        var inserted = await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""

             INSERT INTO "LedgerEntries"
                 ("UserId", "OccurredAtUtc", "ActionType", "Delta", "IdempotencyKey", "ReferenceId", "MetadataJson")
             VALUES
                 ({userId}, {occurredAtUtc}, {"PeriodGrant"}, {periodCredits}, {idempotencyKey}, {null}, {metadataJson}::jsonb)
             ON CONFLICT ("UserId","IdempotencyKey") DO NOTHING;

             """, ct);

        if (inserted == 0)
        {
            // Duplicate grant attempt
            await tx.CommitAsync(ct);

            var existingBalance = await databaseContext.Wallets
                .AsNoTracking()
                .Where(w => w.UserId == userId)
                .Select(w => (int?)w.Balance)
                .SingleOrDefaultAsync(ct);

            return new GrantResult { Granted = false, NewBalance = existingBalance ?? 0 };
        }

        // 2) Upsert wallet for new period (RESET balance model)
        await databaseContext.Database.ExecuteSqlInterpolatedAsync(
            $"""

             INSERT INTO "Wallets"
                 ("UserId", "Balance", "PeriodStartUtc", "PeriodEndUtc", "UpdatedAtUtc")
             VALUES
                 ({userId}, {periodCredits}, {periodStartUtc}, {periodEndUtc}, {occurredAtUtc})
             ON CONFLICT ("UserId") DO UPDATE
             SET "Balance" = EXCLUDED."Balance",
                 "PeriodStartUtc" = EXCLUDED."PeriodStartUtc",
                 "PeriodEndUtc" = EXCLUDED."PeriodEndUtc",
                 "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc";

             """, ct);

        await tx.CommitAsync(ct);

        return new GrantResult { Granted = true, NewBalance = periodCredits };
    }

    public async Task<UserSubscriptionDto?> GetUserSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var subscription = await databaseContext.Subscriptions
            .AsNoTracking()
            .Include(entity => entity.Plan)
            .FirstOrDefaultAsync(entity => entity.UserId == userId, cancellationToken);

        if (subscription is null)
        {
            return null;
        }

        return new UserSubscriptionDto
        {
            UserId = subscription.UserId,
            PlanId = subscription.PlanId,
            PlanCode = subscription.Plan.Code,
            IsActive = subscription.Status == SubscriptionStatus.Active
        };
    }

    public async Task AssignFreeSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        var freePlan = await databaseContext.Plans
            .AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Code == "free", cancellationToken)
            ?? throw new InvalidOperationException("Free plan not found.");

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            nowUtc = nowUtc.ToUniversalTime();
        }

        var periodStartUtc = nowUtc;
        var periodEndUtc = nowUtc.AddDays(30);
        var status = nameof(SubscriptionStatus.Active);

        var inserted = await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""

             INSERT INTO "Subscriptions" ("UserId", "PlanId", "PeriodStartUtc", "PeriodEndUtc", "Status")
             VALUES ({userId}, {freePlan.Id}, {periodStartUtc}, {periodEndUtc}, {status})
             ON CONFLICT ("UserId") DO NOTHING;

             """, cancellationToken);

        if (inserted == 0)
        {
            return;
        }

        var idempotencyKey = $"free_assign:{userId}:{periodStartUtc:O}";

        await GrantPeriodCreditsAsync(
            userId,
            periodStartUtc,
            periodEndUtc,
            freePlan.MonthlyCredits,
            idempotencyKey,
            nowUtc,
            cancellationToken);
    }

    public async Task RefundAsync(
        string userId,
        string actionType,
        string originalIdempotencyKey,
        string refundIdempotencyKey,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Action type is required.", nameof(actionType));
        }

        if (string.IsNullOrWhiteSpace(originalIdempotencyKey))
        {
            throw new ArgumentException("Original idempotency key is required.", nameof(originalIdempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(refundIdempotencyKey))
        {
            throw new ArgumentException("Refund idempotency key is required.", nameof(refundIdempotencyKey));
        }

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            occurredAtUtc = occurredAtUtc.ToUniversalTime();
        }

        await using var transaction =
            await databaseContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var originalEntry = await databaseContext.LedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.UserId == userId
                         && entry.ActionType == actionType
                         && entry.IdempotencyKey == originalIdempotencyKey,
                ct);

        if (originalEntry is null || originalEntry.Delta >= 0)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var refundAmount = -originalEntry.Delta;
        var metadataJson = originalEntry.MetadataJson;

        var inserted = await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""

             INSERT INTO "LedgerEntries"
                 ("UserId", "OccurredAtUtc", "ActionType", "Delta", "IdempotencyKey", "ReferenceId", "MetadataJson")
             VALUES
                 ({userId}, {occurredAtUtc}, {actionType}, {refundAmount}, {refundIdempotencyKey},
                  {originalEntry.ReferenceId}, {metadataJson}::jsonb)
             ON CONFLICT ("UserId","IdempotencyKey") DO NOTHING;

             """, ct);

        if (inserted == 0)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        await using var command = databaseContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = databaseContext.Database.CurrentTransaction!.GetDbTransaction();

        command.CommandText = """
                                  UPDATE "Wallets"
                                  SET "Balance" = "Balance" + @refundAmount,
                                      "UpdatedAtUtc" = @occurredAtUtc
                                  WHERE "UserId" = @userId
                                  RETURNING "Balance";
                              """;

        var userParameter = command.CreateParameter();
        userParameter.ParameterName = "userId";
        userParameter.Value = userId;

        var amountParameter = command.CreateParameter();
        amountParameter.ParameterName = "refundAmount";
        amountParameter.Value = refundAmount;

        var occurredAtParameter = command.CreateParameter();
        occurredAtParameter.ParameterName = "occurredAtUtc";
        occurredAtParameter.Value = occurredAtUtc;

        command.Parameters.Add(userParameter);
        command.Parameters.Add(amountParameter);
        command.Parameters.Add(occurredAtParameter);

        var result = await command.ExecuteScalarAsync(ct);
        var newBalance = result == null ? (int?)null : Convert.ToInt32(result);

        if (newBalance is null)
        {
            await transaction.RollbackAsync(ct);
            return;
        }

        await transaction.CommitAsync(ct);
    }
}