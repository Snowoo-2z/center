using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Center.Services;
using Center.ViewModels;

namespace Center.Views;

public partial class MainWindow : Window
{
    private readonly ScrollViewer _chatScroll;
    private readonly TextBox _composer;
    private readonly DispatcherTimer _scrollTimer;

    private MainWindowViewModel? _viewModel;
    private bool _stickToBottom = true;
    private double _lastOffset;

    public MainWindow()
    {
        InitializeComponent();

        _chatScroll = this.FindControl<ScrollViewer>("ChatScroll")!;
        _composer = this.FindControl<TextBox>("Composer")!;

        _chatScroll.ScrollChanged += OnScrollChanged;

        // Coalesces the "scroll to bottom" work while tokens arrive.
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _scrollTimer.Tick += OnScrollTick;

        DataContextChanged += OnDataContextChanged;
        Opened += (_, _) =>
        {
            WindowChrome.ApplyDarkTitleBar(this);
            _composer.Focus();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainWindowViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsBusy))
        {
            if (_viewModel?.IsBusy == true)
            {
                _stickToBottom = true;
                _scrollTimer.Start();
            }
            else
            {
                _scrollTimer.Stop();
                Dispatcher.UIThread.Post(ScrollToBottom);
            }
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.ActiveConversation))
        {
            _stickToBottom = true;
            Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Background);
        }
    }

    private void OnScrollTick(object? sender, EventArgs e)
    {
        if (_stickToBottom)
            _chatScroll.ScrollToEnd();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var distance = _chatScroll.Extent.Height - _chatScroll.Offset.Y - _chatScroll.Viewport.Height;
        var offsetChanged = Math.Abs(_chatScroll.Offset.Y - _lastOffset) > 0.5;
        _lastOffset = _chatScroll.Offset.Y;

        if (distance <= 72)
            _stickToBottom = true;
        else if (offsetChanged)
            _stickToBottom = false;
    }

    private void ScrollToBottom()
    {
        _chatScroll.ScrollToEnd();
        _stickToBottom = true;
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        if (_viewModel is not null && _viewModel.SendCommand.CanExecute(null))
            _viewModel.SendCommand.Execute(null);

        e.Handled = true;
    }
}
