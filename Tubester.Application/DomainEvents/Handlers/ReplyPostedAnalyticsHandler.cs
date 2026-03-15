using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.DomainEvents;
using Tubester.Domain.Events;

namespace Tubester.Application.DomainEvents.Handlers;

public sealed class ReplyPostedAnalyticsHandler(IUserEventLogger userEventLogger)
    : IDomainEventHandler<ReplyPostedEvent>
{
    public async Task HandleAsync(ReplyPostedEvent domainEvent, CancellationToken cancellationToken)
    {
        await userEventLogger.LogAsync(
            domainEvent.ActorUserId,
            UserEventType.ReplyPostedToYouTube,
            domainEvent.VideoId,
            domainEvent.CommentId,
            new
            {
                length = domainEvent.FinalText.Length,
                wasEdited = !string.Equals(domainEvent.FinalText, domainEvent.SuggestedText, StringComparison.Ordinal)
            },
            cancellationToken);
    }
}
