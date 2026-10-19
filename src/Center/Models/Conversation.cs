using System.Collections.ObjectModel;
using System.Windows.Input;
using Center.Common;

namespace Center.Models;

public sealed class Conversation : ObservableObject
{
    private string _title = "Nouvelle conversation";
    private bool _isActive;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public ICommand SelectCommand { get; set; } = null!;
}
