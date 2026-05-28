namespace Tubester.Integration;

public interface IAiJsonResponseParser
{
    T DeserializeModelResponse<T>(string responseText);
}