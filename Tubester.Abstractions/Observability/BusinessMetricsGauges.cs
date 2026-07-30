using System.Diagnostics.Metrics;

namespace Tubester.Abstractions.Observability;

/// <summary>
/// Observable business gauges that are refreshed periodically to avoid expensive DB queries.
/// </summary>
public sealed class BusinessMetricsGauges
{
    private readonly ObservableGauge<long> _usersGauge;
    private readonly ObservableGauge<long> _channelsGauge;
    private readonly ObservableGauge<long> _videosGauge;
    private readonly ObservableGauge<long> _repliesGauge;

    private long _usersCount;
    private long _channelsCount;
    private long _videosCount;
    private long _repliesCount;

    public BusinessMetricsGauges(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MetricNames.MeterName);

        _usersGauge = meter.CreateObservableGauge(
            MetricNames.UsersTotal,
            () => _usersCount,
            description: "Total number of users");

        _channelsGauge = meter.CreateObservableGauge(
            MetricNames.ChannelsTotal,
            () => _channelsCount,
            description: "Total number of channels");

        _videosGauge = meter.CreateObservableGauge(
            MetricNames.VideosTotal,
            () => _videosCount,
            description: "Total number of videos");

        _repliesGauge = meter.CreateObservableGauge(
            MetricNames.RepliesTotal,
            () => _repliesCount,
            description: "Total number of replies");
    }

    public void UpdateCounts(long users, long channels, long videos, long replies)
    {
        Interlocked.Exchange(ref _usersCount, users);
        Interlocked.Exchange(ref _channelsCount, channels);
        Interlocked.Exchange(ref _videosCount, videos);
        Interlocked.Exchange(ref _repliesCount, replies);
    }
}
