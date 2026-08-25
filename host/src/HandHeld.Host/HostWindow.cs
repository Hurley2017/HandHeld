using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HandHeld.Host.Core;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfImage = System.Windows.Controls.Image;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfButton = System.Windows.Controls.Button;

namespace HandHeld.Host;

/// <summary>Host control panel: connected clients (kick), running game (close), start/stop host.</summary>
public sealed class HostWindow : Window
{
    private readonly HostCore _core;
    private readonly WpfListBox _clientList;
    private readonly TextBlock _gameText;
    private readonly WpfButton _closeGameBtn;
    private readonly WpfButton _startStopBtn;
    private readonly TextBlock _statusText;
    private readonly ObservableCollection<string> _clients = new();

    public HostWindow(HostCore core)
    {
        _core = core;
        Title = "HandHeld Host";
        Width = 460;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = App.TaskbarIcon ?? App.IconSource;

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        _statusText = new TextBlock { FontSize = 13, Foreground = new WpfSolidColorBrush(WpfColor.FromArgb(255, 140, 160, 180)) };
        Grid.SetRow(_statusText, 0);
        root.Children.Add(_statusText);

        // Clients list
        var clientPanel = new StackPanel();
        clientPanel.Children.Add(new TextBlock
        {
            Text = "Connected clients",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 4),
        });
        _clientList = new WpfListBox { ItemsSource = _clients, Height = 140 };
        clientPanel.Children.Add(_clientList);
        var kickBtn = new WpfButton { Content = "Kick selected client", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = WpfHorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4) };
        kickBtn.Click += (_, _) =>
        {
            if (_clientList.SelectedItem is string ip) _core.KickClient(ip);
        };
        clientPanel.Children.Add(kickBtn);
        Grid.SetRow(clientPanel, 1);
        root.Children.Add(clientPanel);

        // Running game
        var gamePanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = WpfVerticalAlignment.Center };
        gamePanel.Children.Add(new TextBlock { Text = "Game: ", FontWeight = FontWeights.SemiBold });
        _gameText = new TextBlock { Text = "—", VerticalAlignment = WpfVerticalAlignment.Center };
        gamePanel.Children.Add(_gameText);
        _closeGameBtn = new WpfButton
        {
            Content = "Close Game",
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(12, 2, 12, 2),
            VerticalAlignment = WpfVerticalAlignment.Center,
        };
        _closeGameBtn.Click += (_, _) => _core.CloseGame();
        gamePanel.Children.Add(_closeGameBtn);
        Grid.SetRow(gamePanel, 2);
        root.Children.Add(gamePanel);

        // Start / stop
        _startStopBtn = new WpfButton { Content = "Stop Host", Padding = new Thickness(16, 6, 16, 6), HorizontalAlignment = WpfHorizontalAlignment.Left };
        _startStopBtn.Click += (_, _) =>
        {
            if (_core.IsRunning) { _core.Stop(); _startStopBtn.Content = "Start Host"; }
            else { _core.Start(); _startStopBtn.Content = "Stop Host"; }
            Refresh();
        };
        Grid.SetRow(_startStopBtn, 3);
        root.Children.Add(_startStopBtn);

        Content = root;

        _core.ClientsChanged += () => Dispatcher.Invoke(Refresh);
        _core.GameStarted += _ => Dispatcher.Invoke(Refresh);
        _core.GameStopped += () => Dispatcher.Invoke(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        _statusText.Text = $"Host {( _core.IsRunning ? "listening" : "stopped" )} · {_core.DisplayName} · discovery 45310 · control 45320";
        _clients.Clear();
        foreach (var c in _core.Clients) _clients.Add(c);
        _gameText.Text = _core.CurrentStatus;
        _closeGameBtn.IsEnabled = _core.HasActiveGame;
        _startStopBtn.Content = _core.IsRunning ? "Stop Host" : "Start Host";
    }
}
