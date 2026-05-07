using Google.GenAI;

namespace Tubester.Integration;

public interface IGeminiClientFactory
{
    Client CreateClient();
}