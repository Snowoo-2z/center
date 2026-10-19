using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Center.Services;

public sealed class OllamaException : Exception
{
    public OllamaException(string message) : base(message)
    {
    }
}

/// <summary>
/// Talks to the local Ollama daemon (http://localhost:11434).
/// Streaming is done over NDJSON: one JSON object per line.
/// </summary>
public sealed class OllamaClient : IDisposable
{
    private readonly HttpClient _http;

    public OllamaClient(Uri baseAddress)
    {
        _http = new HttpClient
        {
            BaseAddress = baseAddress,
            // Generation can take minutes on a 1.5B model running on CPU.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));
    }

    public string Endpoint => _http.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;

    /// <summary>Cheap health check used by the sidebar status badge.</summary>
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            using var response = await _http.GetAsync("/api/tags", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends the conversation and invokes <paramref name="onToken"/> for every streamed chunk.
    /// </summary>
    public async Task StreamChatAsync(
        string model,
        IReadOnlyList<OllamaMessage> messages,
        Action<string> onToken,
        CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = new List<OllamaMessage>(messages),
            Stream = true
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request, OllamaJsonContext.Default.OllamaChatRequest)
        };

        using var response = await _http.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new OllamaException(
                $"Ollama a répondu {(int)response.StatusCode}. {Trim(body, 300)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        await foreach (var chunk in JsonSerializer.DeserializeAsyncEnumerable(
                           stream,
                           OllamaJsonContext.Default.OllamaChatChunk,
                           cancellationToken))
        {
            var piece = chunk?.Message?.Content;
            if (!string.IsNullOrEmpty(piece))
                onToken(piece);

            if (chunk?.Done == true)
                break;
        }
    }

    private static string Trim(string value, int max)
    {
        value = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return value.Length <= max ? value : value[..max] + "…";
    }

    public void Dispose() => _http.Dispose();
}
