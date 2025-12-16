namespace YouTubester.Application;

public interface ICommentScanService
{
    string ScanCommentsAsync(CancellationToken cancellationToken);
}