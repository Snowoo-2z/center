using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Center.Services;

public readonly record struct SearchHit(string Title, string Url, string? Snippet);

/// <summary>
/// "Recherche Web via Chrome" connector.
///
/// 1. It scrapes a public HTML SERP (DuckDuckGo, then Bing) with regexes.
/// 2. The resulting snippets are injected into the model context.
/// 3. Chrome is opened on the query so the user sees the same search.
///
/// Regexes are source-generated => no interpreter, no reflection, AOT safe.
/// </summary>
public sealed partial class WebSearchConnector : IDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private readonly HttpClient _http;

    public WebSearchConnector()
    {
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en;q=0.7");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    }

    public static string BuildSearchUrl(string query)
        => "https://www.google.com/search?q=" + Uri.EscapeDataString(query.Trim());

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchHit>();

        var hits = await TryProviderAsync(
            "https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query),
            DuckDuckGoLink(),
            DuckDuckGoSnippet(),
            cancellationToken);

        if (hits.Count == 0)
        {
            hits = await TryProviderAsync(
                "https://www.bing.com/search?q=" + Uri.EscapeDataString(query) + "&setlang=fr",
                BingLink(),
                BingSnippet(),
                cancellationToken);
        }

        return hits;
    }

    private async Task<List<SearchHit>> TryProviderAsync(
        string url,
        Regex linkRegex,
        Regex snippetRegex,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchHit>();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return results;

            var html = await response.Content.ReadAsStringAsync(timeout.Token);
            var snippets = snippetRegex.Matches(html);

            foreach (Match match in linkRegex.Matches(html))
            {
                var rawUrl = WebUtility.HtmlDecode(match.Groups[1].Value);
                var title = CleanText(match.Groups[2].Value);

                var resolved = ResolveUrl(rawUrl);
                if (resolved is null || title.Length < 3)
                    continue;

                if (results.Exists(r => string.Equals(r.Url, resolved, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var snippet = results.Count < snippets.Count
                    ? CleanText(snippets[results.Count].Groups[1].Value)
                    : null;

                results.Add(new SearchHit(title, resolved, string.IsNullOrWhiteSpace(snippet) ? null : snippet));

                if (results.Count >= 5)
                    break;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Provider timeout: just fall through to the next one.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recherche web échouée ({url}): {ex.Message}");
        }

        return results;
    }

    private static string? ResolveUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (raw.StartsWith("//", StringComparison.Ordinal))
            raw = "https:" + raw;

        // DuckDuckGo wraps outbound links: //duckduckgo.com/l/?uddg=<encoded url>&rut=...
        var marker = raw.IndexOf("uddg=", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var encoded = raw[(marker + 5)..];
            var amp = encoded.IndexOf('&');
            if (amp >= 0)
                encoded = encoded[..amp];
            raw = WebUtility.UrlDecode(encoded);
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        // Skip ads / internal links.
        if (uri.Host.Contains("duckduckgo.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("bing.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Contains("google.com", StringComparison.OrdinalIgnoreCase))
            return null;

        return uri.ToString();
    }

    private static string CleanText(string html)
    {
        var withoutTags = Tags().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Whitespace().Replace(decoded, " ").Trim();
    }

    // ---------- source-generated regexes ----------

    [GeneratedRegex(@"<a[^>]+href=""([^""]*uddg=[^""]+)""[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DuckDuckGoLink();

    [GeneratedRegex(@"<a[^>]*class=""[^""]*result__snippet[^""]*""[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DuckDuckGoSnippet();

    [GeneratedRegex(@"<h2[^>]*>\s*<a[^>]+href=""(https?://[^""]+)""[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BingLink();

    [GeneratedRegex(@"<p[^>]*class=""[^""]*b_lineclamp[^""]*""[^>]*>(.*?)</p>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BingSnippet();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Whitespace();

    public void Dispose() => _http.Dispose();
}
