using System;
using System.Diagnostics;

namespace Center.Services;

/// <summary>
/// Opens URLs in Chrome (fallback: the default browser).
/// </summary>
public sealed class ChromeLauncher
{
    public void OpenSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        OpenInChrome("https://www.google.com/search?q=" + Uri.EscapeDataString(query.Trim()));
    }

    public void OpenInChrome(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            // "chrome" is resolved through the Windows App Paths registry key,
            // so a standard Chrome install is found even if it is not on PATH.
            Process.Start(new ProcessStartInfo("chrome", $"--new-tab \"{url}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Chrome introuvable: {ex.Message}");
            OpenInDefaultBrowser(url);
        }
    }

    private static void OpenInDefaultBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Impossible d'ouvrir l'URL: {ex.Message}");
        }
    }
}
