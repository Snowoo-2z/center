using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Center.Common;

namespace Center.Models;

public enum MessageRole
{
    User,
    Assistant
}

public sealed class ChatMessage : ObservableObject
{
    private string _content = string.Empty;
    private string? _status;
    private bool _isStreaming;

    public ChatMessage()
    {
        Sources.CollectionChanged += OnSourcesChanged;
    }

    public MessageRole Role { get; init; }

    public bool IsUser => Role == MessageRole.User;

    public bool IsAssistant => Role == MessageRole.Assistant;

    /// <summary>Streamed text. Updated on every token.</summary>
    public string Content
    {
        get => _content;
        set
        {
            if (SetProperty(ref _content, value))
                OnPropertyChanged(nameof(IsThinking));
        }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (SetProperty(ref _isStreaming, value))
            {
                OnPropertyChanged(nameof(IsNotStreaming));
                OnPropertyChanged(nameof(IsThinking));
            }
        }
    }

    public bool IsNotStreaming => !_isStreaming;

    /// <summary>True while we wait for the first token (or for the web search).</summary>
    public bool IsThinking => _isStreaming && _content.Length == 0;

    /// <summary>Small grey label next to the author name ("Recherche web · 4 sources").</summary>
    public string? Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    public ObservableCollection<WebSource> Sources { get; } = new();

    public bool HasSources => Sources.Count > 0;

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasSources));
}
