using Serilog.Core;
using Serilog.Events;

namespace Tubester.Observability;

public sealed class LogLevelEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddOrUpdateProperty(
            propertyFactory.CreateProperty("Level", logEvent.Level.ToString()));
    }
}