using Avalonia.Media;
using Center.Common;

namespace Center.Models;

/// <summary>
/// A capability the model can use before answering (web search, local files, ...).
/// </summary>
public sealed class Connector : ObservableObject
{
    private bool _isEnabled;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>24x24 stroke geometry, rendered by a Path element.</summary>
    public required Geometry Icon { get; init; }

    public bool IsAvailable { get; init; } = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}
