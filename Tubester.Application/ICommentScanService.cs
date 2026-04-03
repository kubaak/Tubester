namespace Tubester.Application;

public interface ICommentScanService
{
    Task<string?> ScanCommentsAsync(CancellationToken cancellationToken);
}
