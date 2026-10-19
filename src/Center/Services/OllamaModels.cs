using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Center.Services;

public sealed class OllamaMessage
{
    public OllamaMessage()
    {
    }

    public OllamaMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public string Role { get; set; } = "user";

    public string Content { get; set; } = string.Empty;
}

public sealed class OllamaChatRequest
{
    public string Model { get; set; } = string.Empty;

    public List<OllamaMessage> Messages { get; set; } = new();

    public bool Stream { get; set; } = true;

    public OllamaOptions Options { get; set; } = new();
}

public sealed class OllamaOptions
{
    public double Temperature { get; set; } = 0.7;

    [JsonPropertyName("num_predict")]
    public int NumPredict { get; set; } = 2048;
}

public sealed class OllamaChatChunk
{
    public string? Model { get; set; }

    public OllamaMessage? Message { get; set; }

    public bool Done { get; set; }
}

/// <summary>
/// Source-generated JSON: the only serialization path used by the app.
/// Required because Native AOT has no reflection-based JSON.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OllamaChatRequest))]
[JsonSerializable(typeof(OllamaChatChunk))]
internal sealed partial class OllamaJsonContext : JsonSerializerContext
{
}
