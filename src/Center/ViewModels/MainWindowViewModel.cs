using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Center.Common;
using Center.Models;
using Center.Services;

namespace Center.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private const string ModelName = "qwen2.5:1.5b";
    private const string OllamaEndpoint = "http://localhost:11434";

    private const string SystemPrompt = """
        Tu es Center, un assistant de bureau qui tourne entièrement en local.
        Réponds dans la langue de l'utilisateur, avec un ton direct, précis et utile.
        Tu peux utiliser du Markdown simple (gras, listes, blocs de code) mais reste concis.
        Si des résultats de recherche web sont fournis dans le contexte, appuie-toi dessus
        en priorité et cite les URLs utilisées.
        """;

    private static readonly IBrush OnlineBrush = new SolidColorBrush(Color.Parse("#4ADE80"));
    private static readonly IBrush OfflineBrush = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse("#6B6B6B"));

    private readonly OllamaClient _ollama = new(new Uri(OllamaEndpoint));
    private readonly WebSearchConnector _webSearch = new();
    private readonly ChromeLauncher _chrome = new();

    private CancellationTokenSource? _cts;
    private Conversation _activeConversation = null!;
    private string _input = string.Empty;
    private bool _isBusy;
    private bool _isOllamaOnline;
    private bool _isStatusChecked;

    public MainWindowViewModel()
    {
        Conversations = new ObservableCollection<Conversation>();

        Connectors = new ObservableCollection<Connector>
        {
            new()
            {
                Id = "web",
                Name = "Recherche Web",
                Description = "Ouvre Chrome sur votre requête, récupère les meilleurs résultats et les injecte dans le contexte du modèle.",
                Icon = Geometry.Parse("M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18 M3 12h18 M12 3v18")
            },
            new()
            {
                Id = "files",
                Name = "Fichiers locaux",
                Description = "Indexation de vos documents locaux — bientôt disponible.",
                Icon = Geometry.Parse("M6 3h8l4 4v14H6z M14 3v4h4"),
                IsAvailable = false
            }
        };

        SendCommand = new AsyncRelayCommand(SendAsync);
        StopCommand = new RelayCommand(Stop, () => IsBusy);
        NewChatCommand = new RelayCommand(NewChat);

        NewChat();

        _ = CheckOllamaAsync();
    }

    // ------------------------------------------------------------------
    // Bindable state
    // ------------------------------------------------------------------

    public ObservableCollection<Conversation> Conversations { get; }

    public ObservableCollection<Connector> Connectors { get; }

    public AsyncRelayCommand SendCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand NewChatCommand { get; }

    public Conversation ActiveConversation
    {
        get => _activeConversation;
        private set
        {
            if (ReferenceEquals(_activeConversation, value))
                return;

            if (_activeConversation is not null)
                _activeConversation.Messages.CollectionChanged -= OnMessagesChanged;

            _activeConversation = value;
            _activeConversation.Messages.CollectionChanged += OnMessagesChanged;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMessages));
        }
    }

    public bool IsEmpty => ActiveConversation.Messages.Count == 0;

    public bool HasMessages => !IsEmpty;

    public string Input
    {
        get => _input;
        set => SetProperty(ref _input, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
                return;

            OnPropertyChanged(nameof(IsNotBusy));
            SendCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsNotBusy => !_isBusy;

    public string ModelLabel => $"{ModelName} · local";

    public string OllamaStatusText => !_isStatusChecked
        ? "Ollama · vérification…"
        : _isOllamaOnline
            ? "Ollama connecté"
            : "Ollama hors ligne";

    public IBrush OllamaStatusBrush => !_isStatusChecked
        ? UnknownBrush
        : _isOllamaOnline
            ? OnlineBrush
            : OfflineBrush;

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------

    private void Stop() => _cts?.Cancel();

    private void NewChat()
    {
        if (_activeConversation is not null && _activeConversation.Messages.Count == 0)
            return;

        var conversation = new Conversation();
        conversation.SelectCommand = new RelayCommand(_ => SelectConversation(conversation));

        Conversations.Insert(0, conversation);
        SelectConversation(conversation);
    }

    private void SelectConversation(Conversation conversation)
    {
        foreach (var item in Conversations)
            item.IsActive = ReferenceEquals(item, conversation);

        ActiveConversation = conversation;
    }

    // ------------------------------------------------------------------
    // Chat loop
    // ------------------------------------------------------------------

    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy)
            return;

        var conversation = ActiveConversation;
        Input = string.Empty;

        if (conversation.Messages.Count == 0)
            conversation.Title = text.Length <= 44 ? text : text[..44] + "…";

        conversation.Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = text
        });

        var assistant = new ChatMessage
        {
            Role = MessageRole.Assistant,
            IsStreaming = true
        };
        conversation.Messages.Add(assistant);

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            IReadOnlyList<WebSource> sources = Array.Empty<WebSource>();

            if (IsConnectorEnabled("web"))
            {
                assistant.Status = "Recherche web…";

                // The connector is literally "web search via Chrome".
                _chrome.OpenSearch(text);

                var hits = await _webSearch.SearchAsync(text, token);
                sources = hits
                    .Select(hit => new WebSource
                    {
                        Title = hit.Title,
                        Url = hit.Url,
                        Snippet = hit.Snippet,
                        OpenCommand = new RelayCommand(_ => _chrome.OpenInChrome(hit.Url))
                    })
                    .ToList();

                foreach (var source in sources)
                    assistant.Sources.Add(source);

                assistant.Status = hits.Count == 0
                    ? "Recherche web · aucun résultat"
                    : $"Recherche web · {hits.Count} source{(hits.Count > 1 ? "s" : string.Empty)}";
            }

            var messages = BuildMessages(conversation, sources);
            await _ollama.StreamChatAsync(ModelName, messages, chunk => AppendChunk(assistant, chunk), token);
        }
        catch (OperationCanceledException)
        {
            // Stop button: keep whatever was already streamed.
        }
        catch (HttpRequestException)
        {
            assistant.Content =
                "Impossible de joindre Ollama sur " + OllamaEndpoint + ".\n\n" +
                "Vérifiez qu'Ollama est lancé (`ollama serve`) et que le modèle est installé " +
                "(`ollama pull " + ModelName + "`).";
        }
        catch (OllamaException ex)
        {
            assistant.Content = "Ollama : " + ex.Message;
        }
        catch (Exception ex)
        {
            assistant.Content = "Erreur inattendue : " + ex.Message;
        }
        finally
        {
            assistant.IsStreaming = false;
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;

            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMessages));
        }
    }

    /// <summary>
    /// Called for every streamed token. UI updates must happen on the UI thread.
    /// </summary>
    private static void AppendChunk(ChatMessage message, string chunk)
    {
        if (Dispatcher.UIThread.CheckAccess())
            message.Content += chunk;
        else
            Dispatcher.UIThread.Post(() => message.Content += chunk);
    }

    private static List<OllamaMessage> BuildMessages(Conversation conversation, IReadOnlyList<WebSource> sources)
    {
        var messages = new List<OllamaMessage> { new("system", SystemPrompt) };

        if (sources.Count > 0)
            messages.Add(new OllamaMessage("system", BuildWebContext(sources)));

        var history = conversation.Messages
            .Where(m => m.Content.Length > 0 && !m.IsStreaming)
            .TakeLast(16);

        foreach (var message in history)
            messages.Add(new OllamaMessage(message.IsUser ? "user" : "assistant", message.Content));

        return messages;
    }

    private static string BuildWebContext(IReadOnlyList<WebSource> sources)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Résultats de recherche web récupérés à l'instant (extraits).");
        builder.AppendLine("Utilise-les s'ils répondent à la question et cite les URLs.");

        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            builder.AppendLine();
            builder.AppendLine($"[{i + 1}] {source.Title}");
            builder.AppendLine(source.Url);

            if (!string.IsNullOrWhiteSpace(source.Snippet))
            {
                var snippet = source.Snippet!;
                builder.AppendLine(snippet.Length <= 500 ? snippet : snippet[..500] + "…");
            }
        }

        return builder.ToString();
    }

    private bool IsConnectorEnabled(string id)
        => Connectors.FirstOrDefault(c => c.Id == id)?.IsEnabled == true;

    // ------------------------------------------------------------------
    // Misc
    // ------------------------------------------------------------------

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasMessages));
    }

    private async Task CheckOllamaAsync()
    {
        var online = await _ollama.PingAsync();

        _isOllamaOnline = online;
        _isStatusChecked = true;

        OnPropertyChanged(nameof(OllamaStatusText));
        OnPropertyChanged(nameof(OllamaStatusBrush));
    }
}
