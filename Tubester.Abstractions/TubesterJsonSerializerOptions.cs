using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tubester.Abstractions;

public class TubesterJsonSerializerOptions
{
    public static readonly JsonSerializerOptions DefaultWrite = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static readonly JsonSerializerOptions DefaultRead = new()
    {
        PropertyNameCaseInsensitive = true
    };
}