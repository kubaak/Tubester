namespace Tubester.Application.Contracts.Replies;

public sealed record BatchIgnoreResult(
    int Requested,
    int Ignored,
    int AlreadyIgnored,
    int SkippedPosted,
    int NotFound,
    string[] IgnoredIds,
    string[] AlreadyIgnoredIds,
    string[] SkippedPostedIds,
    string[] NotFoundIds);