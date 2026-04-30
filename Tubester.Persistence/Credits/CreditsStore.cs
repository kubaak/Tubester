using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Tubester.Abstractions.Credits;

namespace Tubester.Persistence.Credits;

public sealed class CreditsStore(
    TubesterDb databaseContext,
    ILogger<CreditsStore> logger) : ICreditsStore
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
            logger.LogInformation(
                "Credits spent successfully for user {UserId}, action {ActionType}, cost {Cost}, new balance {NewBalance}",
                userId, actionType, cost, newBalance);
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
            logger.LogWarning(
                "Spend failed for user {UserId}, action {ActionType}: no wallet found",
                userId, actionType);
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
            logger.LogWarning(
                "Spend failed for user {UserId}, action {ActionType}: wallet expired (period ended {PeriodEndUtc})",
                userId, actionType, walletState.PeriodEndUtc);
            return new SpendResult
            {
                Succeeded = false,
                WasDuplicate = false,
                NewBalance = walletState.Balance,
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

        await tx.CommitAsync(ct);

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
            await transaction.RollbackAsync(ct);
            return;
        }

        await transaction.CommitAsync(ct);
    }

    public async Task<GrantResult> GrantCreditsAsync(
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

        await using var tx = await databaseContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var metadataJson =
            JsonSerializer.Serialize(new { type = "admin_grant", amount }, _jsonSerializerOptions);
        
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

            await tx.RollbackAsync(ct);
            
            if (walletDto is null)
            {
                logger.LogError("Ledger entry already exists for idempotency key '{IdempotencyKey}', but wallet was not found", idempotencyKey);
                throw new InvalidOperationException(
                    $"Ledger entry already exists, but wallet was not found.");
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

        await tx.CommitAsync(ct);

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

        var result = await cmd.ExecuteScalarAsync(ct) ?? throw new InvalidOperationException("Wallet admin grant upsert did not return a balance.");

        return Convert.ToInt32(result);
    }
}
