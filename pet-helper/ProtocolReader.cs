using System.Text.Json;

namespace PetHelper;

public static class ProtocolReader
{
    public static ProtocolMessage? Parse(string line)
    {
        try
        {
            var message = JsonSerializer.Deserialize<ProtocolMessage>(line, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
            return message is { Version: 1, Kind: "shutdown" } ? message : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
