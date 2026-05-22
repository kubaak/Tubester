using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions;
using Tubester.Abstractions.Credits;

namespace Tubester.Persistence.Credits;

public sealed class CreditsStore(
    TubesterDb databaseContext,
    IMemoryCache cache,
    ILogger<CreditsStore> logger) : ICreditsStore
{
    private static readonly TimeSpan _actionCostsCacheDuration = TimeSpan.FromMinutes(10);
    private const string ActionCostsCacheKey = "credits:action-costs";
    private const string ActionCostCacheKeyPrefix = "credits:action-cost:";

    public async Task<IReadOnlyList<CreditActionCostDto>> GetActionCostsAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<IReadOnlyList<CreditActionCostDto>>(ActionCostsCacheKey, out var cachedCosts))
        {
            if (cachedCosts != null)
            {
                return cachedCosts;
            }
        }

        var costs = await databaseContext.ActionCosts
            .AsNoTracking()
            .Where(c => c.IsEnabled)
            .Select(cost => new CreditActionCostDto
            {
                ActionType = cost.ActionType,
                Cost = cost.Cost
            })
            .ToListAsync(cancellationToken);

        cache.Set(ActionCostsCacheKey, costs, _actionCostsCacheDuration);

        return costs;
    }

    public async Task<CreditActionCostDto?> GetActionCostAsync(string actionType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Action type is required.", nameof(actionType));
        }

        var cacheKey = $"{ActionCostCacheKeyPrefix}{actionType}";

        if (cache.TryGetValue<CreditActionCostDto?>(cacheKey, out var cachedCost))
        {
            return cachedCost;
        }

        var creditActionCost = await databaseContext.ActionCosts
            .AsNoTracking()
            .Where(c => c.IsEnabled)
            .FirstOrDefaultAsync(cost => cost.ActionType == actionType, cancellationToken);

        if (creditActionCost is null)
        {
            cache.Set<CreditActionCostDto?>(cacheKey, null, _actionCostsCacheDuration);
            return null;
        }

        var result = new CreditActionCostDto
        {
            ActionType = creditActionCost.ActionType,
            Cost = creditActionCost.Cost
        };

        cache.Set(cacheKey, result, _actionCostsCacheDuration);

        return result;
    }

    public async Task<WalletDto?> GetWalletAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        return await databaseContext.Wallets
            .AsNoTracking()
            .Where(wallet => wallet.UserId == userId)
            .Select(wallet => new WalletDto
            {
                UserId = wallet.UserId,
                Balance = wallet.Balance,
                PeriodStartUtc = wallet.PeriodStartUtc,
                PeriodEndUtc = wallet.PeriodEndUtc,
                UpdatedAtUtc = wallet.UpdatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);
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

    public async Task<SubscriptionDto?> GetActiveSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            nowUtc = nowUtc.ToUniversalTime();
        }

        var subscription = await databaseContext.Subscriptions
            .AsNoTracking()
            .Include(entity => entity.Plan)
            .Where(entity => entity.UserId == userId
                             && entity.Status == SubscriptionStatus.Active
                             && entity.PeriodStartUtc <= nowUtc
                             && entity.PeriodEndUtc > nowUtc
                             && entity.Plan.IsActive)
            .OrderByDescending(entity => entity.PeriodStartUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return subscription is null ? null : ToSubscriptionDto(subscription);
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

        await using var tx = await BeginTransactionIfNeededAsync(ct);

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
            await CommitIfOwnedAsync(tx, ct);

            logger.LogDebug("Spend duplicate detected for user {UserId}, action {ActionType}", userId, actionType);

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
                FinalCost = cost,
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
            await CommitIfOwnedAsync(tx, ct);

            logger.LogInformation(
                "Credits spent successfully for user {UserId}, action {ActionType}, cost {Cost}, new balance {NewBalance}",
                userId, actionType, cost, newBalance);

            return new SpendResult
            {
                Succeeded = true,
                WasDuplicate = false,
                NewBalance = newBalance,
                FinalCost = cost,
                FailureReason = SpendFailureReason.None
            };
        }

        // 3) Spend failed => rollback so the inserted ledger row disappears
        await RollbackIfOwnedAsync(tx, ct);

        // One small read to classify reason + return current balance
        var walletState = await databaseContext.Wallets
            .AsNoTracking()
            .Where(w => w.UserId == userId)
            .Select(w => new { w.Balance, w.PeriodEndUtc })
            .SingleOrDefaultAsync(ct);

        if (walletState is null)
        {
            logger.LogWarning(
                "Spend failed for user {UserId}, action {ActionType}: no wallet found",
                userId, actionType);

            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = null,
                FinalCost = cost,
                FailureReason = SpendFailureReason.NoWallet
            };
        }

        if (walletState.PeriodEndUtc <= occurredAtUtc)
        {
            logger.LogWarning(
                "Spend failed for user {UserId}, action {ActionType}: wallet expired (period ended {PeriodEndUtc})",
                userId, actionType, walletState.PeriodEndUtc);

            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = walletState.Balance,
                FinalCost = cost,
                FailureReason = SpendFailureReason.WalletExpired
            };
        }

        logger.LogWarning(
            "Spend failed for user {UserId}, action {ActionType}: insufficient balance (current balance {Balance}, cost {Cost})",
            userId, actionType, walletState.Balance, cost);

        return new SpendResult
        {
            Succeeded = false,
            WasDuplicate = false,
            NewBalance = walletState.Balance,
            FinalCost = cost,
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

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var metadataJson =
            JsonSerializer.Serialize(new { type = "period_grant", periodStartUtc, periodEndUtc, periodCredits },
                TubesterJsonSerializerOptions.DefaultWrite);

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
            await CommitIfOwnedAsync(tx, ct);

            logger.LogDebug(
                "Period grant duplicate detected for user {UserId}, idempotency key {IdempotencyKey}",
                userId, idempotencyKey);

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

        await CommitIfOwnedAsync(tx, ct);

        logger.LogInformation(
            "Period credits granted for user {UserId}, amount {PeriodCredits}, period {PeriodStartUtc} to {PeriodEndUtc}",
            userId, periodCredits, periodStartUtc, periodEndUtc);

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
        var periodEndUtc = nowUtc.AddMonths(1);
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

    public async Task<SubscriptionSummaryDto?> GetSubscriptionSummaryAsync(
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

        return new SubscriptionSummaryDto
        {
            PlanCode = subscription.Plan.Code,
            PlanName = subscription.Plan.Name,
            Status = subscription.Status.ToString(),
            PeriodStartUtc = subscription.PeriodStartUtc,
            PeriodEndUtc = subscription.PeriodEndUtc,
            MonthlyCredits = subscription.Plan.MonthlyCredits
        };
    }

    public async Task RefundAsync(
        string userId,
        string actionType,
        string originalIdempotencyKey,
        string refundIdempotencyKey,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionType);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalIdempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(refundIdempotencyKey);

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            occurredAtUtc = occurredAtUtc.ToUniversalTime();
        }

        await using var transaction = await BeginTransactionIfNeededAsync(ct);

        var originalEntry = await databaseContext.LedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.UserId == userId
                         && entry.ActionType == actionType
                         && entry.IdempotencyKey == originalIdempotencyKey,
                ct);

        if (originalEntry is null || originalEntry.Delta >= 0)
        {
            await CommitIfOwnedAsync(transaction, ct);
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
            await CommitIfOwnedAsync(transaction, ct);

            logger.LogDebug(
                "Refund duplicate detected for user {UserId}, action {ActionType}, original idempotency key {OriginalIdempotencyKey}",
                userId, actionType, originalIdempotencyKey);

            return;
        }

        logger.LogInformation(
            "Refund completed for user {UserId}, action {ActionType}, amount {RefundAmount}",
            userId, actionType, refundAmount);

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
            await RollbackIfOwnedAsync(transaction, ct);
            return;
        }

        await CommitIfOwnedAsync(transaction, ct);
    }

    public async Task<GrantResult> AdminGrantCreditsAsync(
        string userId,
        int amount,
        string idempotencyKey,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            occurredAtUtc = occurredAtUtc.ToUniversalTime();
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        var metadataJson =
            JsonSerializer.Serialize(new { type = "admin_grant", amount }, TubesterJsonSerializerOptions.DefaultWrite);

        // 1) Idempotency gate
        var inserted = await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""

             INSERT INTO "LedgerEntries"
                 ("UserId", "OccurredAtUtc", "ActionType", "Delta", "IdempotencyKey", "ReferenceId", "MetadataJson")
             VALUES
                 ({userId}, {occurredAtUtc}, {"AdminGrant"}, {amount}, {idempotencyKey}, {null}, {metadataJson}::jsonb)
             ON CONFLICT ("UserId","IdempotencyKey") DO NOTHING;

             """, ct);

        if (inserted == 0)
        {
            var walletDto = await databaseContext.Wallets
                .AsNoTracking()
                .Where(w => w.UserId == userId)
                .Select(w => new { w.Balance, w.PeriodStartUtc, w.PeriodEndUtc })
                .SingleOrDefaultAsync(ct);

            await RollbackIfOwnedAsync(tx, ct);

            if (walletDto is null)
            {
                logger.LogError(
                    "Ledger entry already exists for idempotency key '{IdempotencyKey}', but wallet was not found",
                    idempotencyKey);

                throw new InvalidOperationException("Ledger entry already exists, but wallet was not found.");
            }

            return new GrantResult
            {
                Granted = true,
                AlreadyProcessed = true,
                Reason = "Duplicate grant attempt",
                NewBalance = walletDto.Balance
            };
        }

        // 2) Ensure wallet exists (create if necessary)
        var newBalance = await UpsertWalletForAdminGrantAsync(
            userId,
            amount,
            occurredAtUtc,
            ct);

        await CommitIfOwnedAsync(tx, ct);

        logger.LogInformation(
            "Admin credits granted for user {UserId}, amount {Amount}, new balance {NewBalance}",
            userId, amount, newBalance);

        return new GrantResult
        {
            Granted = true,
            AlreadyProcessed = false,
            NewBalance = newBalance
        };
    }

    public async Task<SubscriptionDto?> TryRenewSubscriptionAsync(
        string userId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (nowUtc.Offset != TimeSpan.Zero)
        {
            nowUtc = nowUtc.ToUniversalTime();
        }

        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);

        var subscription = await databaseContext.Subscriptions
            .FromSqlInterpolated($"""
                SELECT *
                FROM "Subscriptions"
                WHERE "UserId" = {userId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            await CommitIfOwnedAsync(transaction, cancellationToken);
            return null;
        }

        await databaseContext.Entry(subscription)
            .Reference(entity => entity.Plan)
            .LoadAsync(cancellationToken);

        if (subscription.Status != SubscriptionStatus.Active || !subscription.Plan.IsActive)
        {
            await CommitIfOwnedAsync(transaction, cancellationToken);
            return null;
        }

        if (subscription.PeriodStartUtc <= nowUtc && subscription.PeriodEndUtc > nowUtc)
        {
            await CommitIfOwnedAsync(transaction, cancellationToken);
            return ToSubscriptionDto(subscription);
        }

        var oldPeriodStartUtc = subscription.PeriodStartUtc;
        var oldPeriodEndUtc = subscription.PeriodEndUtc;

        var newPeriodStartUtc = oldPeriodStartUtc;
        var newPeriodEndUtc = oldPeriodEndUtc;

        while (newPeriodEndUtc <= nowUtc)
        {
            newPeriodStartUtc = newPeriodEndUtc;
            newPeriodEndUtc = newPeriodStartUtc.AddMonths(1);
        }

        await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SubscriptionHistories"
                ("UserId", "PlanId", "Status", "PeriodStartUtc", "PeriodEndUtc", "CreatedAtUtc")
            VALUES
                ({subscription.UserId}, {subscription.PlanId}, {subscription.Status.ToString()},
                 {oldPeriodStartUtc}, {oldPeriodEndUtc}, {nowUtc})
            ON CONFLICT ("UserId", "PlanId", "PeriodStartUtc", "PeriodEndUtc") DO NOTHING;
            """, cancellationToken);

        subscription.PeriodStartUtc = newPeriodStartUtc;
        subscription.PeriodEndUtc = newPeriodEndUtc;

        await databaseContext.SaveChangesAsync(cancellationToken);
        await CommitIfOwnedAsync(transaction, cancellationToken);

        logger.LogInformation(
            "Renewed subscription for user {UserId}, plan {PlanCode}, old period {OldPeriodStartUtc} to {OldPeriodEndUtc}, new period {NewPeriodStartUtc} to {NewPeriodEndUtc}",
            userId,
            subscription.Plan.Code,
            oldPeriodStartUtc,
            oldPeriodEndUtc,
            newPeriodStartUtc,
            newPeriodEndUtc);

        return ToSubscriptionDto(subscription);
    }

    private async Task<int> UpsertWalletForAdminGrantAsync(
        string userId,
        int amount,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        var periodEndUtc = occurredAtUtc.AddDays(30);

        await using var cmd = databaseContext.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = databaseContext.Database.CurrentTransaction!.GetDbTransaction();

        cmd.CommandText = """
            INSERT INTO "Wallets"
                ("UserId", "Balance", "PeriodStartUtc", "PeriodEndUtc", "UpdatedAtUtc")
            VALUES
                (@userId, @amount, @periodStartUtc, @periodEndUtc, @occurredAtUtc)
            ON CONFLICT ("UserId") DO UPDATE
            SET "Balance" = CASE
                WHEN "Wallets"."PeriodEndUtc" <= @occurredAtUtc
                THEN EXCLUDED."Balance"
                ELSE "Wallets"."Balance" + EXCLUDED."Balance"
            END,
            "PeriodStartUtc" = CASE
                WHEN "Wallets"."PeriodEndUtc" <= @occurredAtUtc
                THEN EXCLUDED."PeriodStartUtc"
                ELSE "Wallets"."PeriodStartUtc"
            END,
            "PeriodEndUtc" = CASE
                WHEN "Wallets"."PeriodEndUtc" <= @occurredAtUtc
                THEN EXCLUDED."PeriodEndUtc"
                ELSE "Wallets"."PeriodEndUtc"
            END,
            "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            RETURNING "Balance";
            """;

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = userId;

        var pAmount = cmd.CreateParameter();
        pAmount.ParameterName = "amount";
        pAmount.Value = amount;

        var pPeriodStart = cmd.CreateParameter();
        pPeriodStart.ParameterName = "periodStartUtc";
        pPeriodStart.Value = occurredAtUtc;

        var pPeriodEnd = cmd.CreateParameter();
        pPeriodEnd.ParameterName = "periodEndUtc";
        pPeriodEnd.Value = periodEndUtc;

        var pAt = cmd.CreateParameter();
        pAt.ParameterName = "occurredAtUtc";
        pAt.Value = occurredAtUtc;

        cmd.Parameters.Add(pUser);
        cmd.Parameters.Add(pAmount);
        cmd.Parameters.Add(pPeriodStart);
        cmd.Parameters.Add(pPeriodEnd);
        cmd.Parameters.Add(pAt);

        var result = await cmd.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException("Wallet admin grant upsert did not return a balance.");

        return Convert.ToInt32(result);
    }

    public async Task<SpendResult> TrySpendBatchAsync(
    BatchSpendRequest request,
    IReadOnlyList<int> costs,
    DateTimeOffset occurredAtUtc,
    CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);

        if (request.Actions.Count == 0)
        {
            throw new ArgumentException("Number of actions must not be empty.", nameof(costs));
        }

        if (request.Actions.Count != costs.Count)
        {
            throw new ArgumentException("Number of actions must match number of costs.", nameof(costs));
        }

        // Ensure UTC offset for Npgsql timestamptz
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            occurredAtUtc = occurredAtUtc.ToUniversalTime();
        }

        await using var tx = await BeginTransactionIfNeededAsync(ct);

        // Track if any action is a duplicate
        var anyDuplicate = false;
        var totalCost = 0;

        // 1) Insert ledger entries for all actions (idempotency gate)
        for (var i = 0; i < request.Actions.Count; i++)
        {
            var action = request.Actions[i];
            var cost = costs[i];
            totalCost += cost;

            if (string.IsNullOrWhiteSpace(action.IdempotencyKey))
            {
                throw new ArgumentException("Idempotency key is required for each action.", nameof(action));
            }

            var metadataJson = action.Metadata is null
                ? null
                : JsonSerializer.Serialize(action.Metadata, TubesterJsonSerializerOptions.DefaultWrite);

            var inserted = await databaseContext.Database.ExecuteSqlInterpolatedAsync($"""

             INSERT INTO "LedgerEntries"
                 ("UserId", "OccurredAtUtc", "ActionType", "Delta", "IdempotencyKey", "ReferenceId", "MetadataJson")
             VALUES
                 ({request.UserId}, {occurredAtUtc}, {action.ActionType}, {-cost}, {action.IdempotencyKey}, {action.ReferenceId}, {metadataJson}::jsonb)
             ON CONFLICT ("UserId","IdempotencyKey") DO NOTHING;

             """, ct);

            if (inserted == 0)
            {
                anyDuplicate = true;
            }
        }

        if (anyDuplicate)
        {
            // Check if ALL are duplicates (entire batch was already processed)
            var allDuplicates = true;

            for (var i = 0; i < request.Actions.Count; i++)
            {
                var action = request.Actions[i];

                var exists = await databaseContext.LedgerEntries
                    .AsNoTracking()
                    .AnyAsync(
                        entry => entry.UserId == request.UserId &&
                                 entry.IdempotencyKey == action.IdempotencyKey,
                        ct);

                if (!exists)
                {
                    allDuplicates = false;
                    break;
                }
            }

            if (allDuplicates)
            {
                await CommitIfOwnedAsync(tx, ct);

                logger.LogDebug("Batch spend duplicate detected for user {UserId}", request.UserId);

                var bal = await databaseContext.Wallets
                    .AsNoTracking()
                    .Where(w => w.UserId == request.UserId)
                    .Select(w => (int?)w.Balance)
                    .SingleOrDefaultAsync(ct);

                return new SpendResult
                {
                    Succeeded = true,
                    WasDuplicate = true,
                    NewBalance = bal,
                    FinalCost = totalCost,
                    FailureReason = SpendFailureReason.None
                };
            }

            // Partial duplicates - some new, some existing
            // Rollback the transaction so uncommitted ledger entries are removed
            await RollbackIfOwnedAsync(tx, ct);

            // Return failure - cannot process partial batch
            logger.LogWarning(
                "Batch spend conflict for user {UserId}: some actions already processed",
                request.UserId);

            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = null,
                FinalCost = totalCost,
                FailureReason = SpendFailureReason.Conflict
            };
        }

        // 2) Atomic wallet update - deduct total cost
        await using var cmd = databaseContext.Database.GetDbConnection().CreateCommand();

        var currentTransaction = databaseContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A database transaction is required for batch credit spending.");

        cmd.Transaction = currentTransaction.GetDbTransaction();

        cmd.CommandText = """
        UPDATE "Wallets"
        SET "Balance" = "Balance" - @totalCost,
            "UpdatedAtUtc" = @occurredAtUtc
        WHERE "UserId" = @userId
          AND "PeriodEndUtc" > @occurredAtUtc
          AND "Balance" >= @totalCost
        RETURNING "Balance"
        """;

        var pUser = cmd.CreateParameter();
        pUser.ParameterName = "userId";
        pUser.Value = request.UserId;

        var pTotalCost = cmd.CreateParameter();
        pTotalCost.ParameterName = "totalCost";
        pTotalCost.Value = totalCost;

        var pAt = cmd.CreateParameter();
        pAt.ParameterName = "occurredAtUtc";
        pAt.Value = occurredAtUtc;

        cmd.Parameters.Add(pUser);
        cmd.Parameters.Add(pTotalCost);
        cmd.Parameters.Add(pAt);

        var result = await cmd.ExecuteScalarAsync(ct);
        var newBalance = result == null ? (int?)null : Convert.ToInt32(result);

        if (newBalance is not null)
        {
            await CommitIfOwnedAsync(tx, ct);

            logger.LogInformation(
                "Batch credits spent successfully for user {UserId}, total cost {TotalCost}, new balance {NewBalance}",
                request.UserId,
                totalCost,
                newBalance);

            return new SpendResult
            {
                Succeeded = true,
                WasDuplicate = false,
                NewBalance = newBalance,
                FinalCost = totalCost,
                FailureReason = SpendFailureReason.None
            };
        }

        // 3) Spend failed - rollback if this method owns the transaction.
        // If an outer transaction owns it, caller must throw to rollback.
        await RollbackIfOwnedAsync(tx, ct);

        var walletState = await databaseContext.Wallets
            .AsNoTracking()
            .Where(w => w.UserId == request.UserId)
            .Select(w => new { w.Balance, w.PeriodEndUtc })
            .SingleOrDefaultAsync(ct);

        if (walletState is null)
        {
            logger.LogWarning(
                "Batch spend failed for user {UserId}: no wallet found",
                request.UserId);

            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = null,
                FinalCost = totalCost,
                FailureReason = SpendFailureReason.NoWallet
            };
        }

        if (walletState.PeriodEndUtc <= occurredAtUtc)
        {
            logger.LogWarning(
                "Batch spend failed for user {UserId}: wallet expired",
                request.UserId);

            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = walletState.Balance,
                FinalCost = totalCost,
                FailureReason = SpendFailureReason.WalletExpired
            };
        }

        logger.LogWarning(
            "Batch spend failed for user {UserId}: insufficient balance (current balance {Balance}, total cost {TotalCost})",
            request.UserId,
            walletState.Balance,
            totalCost);

        return new SpendResult
        {
            Succeeded = false,
            WasDuplicate = false,
            NewBalance = walletState.Balance,
            FinalCost = totalCost,
            FailureReason = SpendFailureReason.InsufficientBalance
        };
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(
        CancellationToken cancellationToken)
    {
        if (databaseContext.Database.CurrentTransaction is not null)
        {
            return null;
        }

        return await databaseContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
    }

    private static async Task CommitIfOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task RollbackIfOwnedAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
    }

    private static SubscriptionDto ToSubscriptionDto(Subscription subscription)
    {
        return new SubscriptionDto
        {
            UserId = subscription.UserId,
            PlanId = subscription.PlanId,
            PlanCode = subscription.Plan.Code,
            PeriodCredits = subscription.Plan.MonthlyCredits,
            PeriodStartUtc = subscription.PeriodStartUtc,
            PeriodEndUtc = subscription.PeriodEndUtc
        };
    }
}