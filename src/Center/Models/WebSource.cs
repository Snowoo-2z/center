using System.Windows.Input;

namespace Center.Models;

public sealed class WebSource
{
    public required string Title { get; init; }

    public required string Url { get; init; }

    public string? Snippet { get; init; }

    public required ICommand OpenCommand { get; init; }
}
