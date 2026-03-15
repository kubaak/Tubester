using Tubester.Abstractions.Analytics;
using Tubester.Abstractions.DomainEvents;
using Tubester.Domain.Events;

namespace Tubester.Application.DomainEvents.Handlers;

public sealed class ReplyApprovedAnalyticsHandler(IUserEventLogger userEventLogger)
    : IDomainEventHandler<ReplyApprovedEvent>
{
    public async Task HandleAsync(ReplyApprovedEvent domainEvent, CancellationToken cancellationToken)
    {
        await userEventLogger.LogAsync(
            domainEvent.ActorUserId,
            UserEventType.ReplyApproved,
            domainEvent.VideoId,
            domainEvent.CommentId,
            new
            {
                length = domainEvent.FinalText.Length
            },
            cancellationToken);
    }
}
