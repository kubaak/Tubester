namespace Tubester.Abstractions.Observability;

/// <summary>
/// Configuration options for observability features.
/// </summary>
public class ObservabilityOptions
{
    public bool Enabled { get; set; } = false;
    
    public PrometheusOptions Prometheus { get; set; } = new();
    
    public SeqOptions Seq { get; set; } = new();
    
    public BusinessMetricsOptions BusinessMetrics { get; set; } = new();
    
    /// <summary>
    /// HTTP port for the Worker's health/metrics endpoints.
    /// Only applies to Tubester.Worker.
    /// Default is 8080.
    /// </summary>
    public int WorkerHttpPort { get; set; } = 8080;
}

/// <summary>
/// Configuration options for Prometheus metrics.
/// </summary>
public class PrometheusOptions
{
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Whether to expose the /metrics endpoint publicly.
    /// Default is false (internal only).
    /// </summary>
    public bool ExposePublicly { get; set; } = false;
}

/// <summary>
/// Configuration options for Seq structured logging.
/// </summary>
public class SeqOptions
{
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// URL of the Seq server (e.g., http://localhost:5341).
    /// Can also be set via OBSERVABILITY_SEQ_URL environment variable.
    /// </summary>
    public string? Url { get; set; }
    
    /// <summary>
    /// API key for Seq (optional).
    /// Can also be set via OBSERVABILITY_SEQ_APIKEY environment variable.
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Configuration options for business metrics refresh.
/// </summary>
public class BusinessMetricsOptions
{
    /// <summary>
    /// Whether to enable business metrics refresh.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Interval in seconds to refresh business metric counts from the database.
    /// Default is 60 seconds.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 60;
}
