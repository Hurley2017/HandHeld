using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HandHeld.Host.Core;
using WpfBrushes = System.Windows.Media.Brushes;

namespace HandHeld.Host;

public partial class MainWindow : Window
{
    private readonly HostCore _core;
    private readonly TextBlock _status;

    public MainWindow()
    {
        _core = HostCore.Instance;

        Title = "HandHeld Host";
        Width = 480;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(16) };
        var title = new TextBlock
        {
            Text = "HandHeld Host",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _status = new TextBlock
        {
            Text = "Starting…",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        panel.Children.Add(title);
        panel.Children.Add(_status);

        var tip = new TextBlock
        {
            Text = "Find this PC from the HandHeld app on your phone.\n"
                 + "Discovery: UDP 45310 · Control: WS 45320\n"
                 + "Video: UDP 45330 · Audio: UDP 45340 · Input: UDP 45350",
            Foreground = WpfBrushes.Gray,
            FontSize = 12,
        };
        panel.Children.Add(tip);

        Content = panel;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        _status.Text = _core.StatusText;
    }
}
