using Hangfire;
using Hangfire.RecurringJobExtensions;
using Hangfire.Server;
using Microsoft.Extensions.Logging;
using Tubester.Application.Credits;

namespace Tubester.Application.Jobs;

public sealed class CreditPeriodMaintenanceJob(
    ILogger<CreditPeriodMaintenanceJob> logger,
    ICreditsService creditsService) : IRecurringJob
{
    [Queue("default")]
    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
    public void Execute(PerformContext context)
    {
        var cancellationToken = context.CancellationToken;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var updatedWalletCount =
                creditsService.GrantPeriodCreditsIfDueAsync(cancellationToken.ShutdownToken)
                    .GetAwaiter()
                    .GetResult();

            logger.LogInformation(
                "Credit period maintenance job completed. Updated {UpdatedWalletCount} wallets",
                updatedWalletCount);
        }
        catch (JobAbortedException)
        {
            logger.LogInformation("Credit period maintenance job aborted by Hangfire");
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.ShutdownToken.IsCancellationRequested)
        {
            logger.LogInformation("Credit period maintenance job cancelled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Credit period maintenance job failed");
            throw;
        }
    }
}