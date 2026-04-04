namespace Tubester.Integration.Dtos;

public sealed record CommentThreadDto(
    string ParentCommentId,
    string VideoId,
    string AuthorChannelId,
    string Text,
    DateTimeOffset? PublishedAt
);
