using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace GoXlrControl.App.Services;

public sealed class TrayService : IDisposable
{
    private TaskbarIcon? _icon;
    private readonly Lazy<AppBootstrapper> _bootstrap;

    public TrayService(Lazy<AppBootstrapper> bootstrap) => _bootstrap = bootstrap;

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = "GoXLR Control Studio",
            Visibility = Visibility.Visible
        };

        try
        {
            _icon.Icon = System.Drawing.SystemIcons.Application;
        }
        catch
        {
            // Icon optional
        }

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(CreateItem("Öffnen", (_, _) => ShowMain()));
        menu.Items.Add(CreateItem("Controller pausieren/fortsetzen", (_, _) => TogglePause()));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(CreateItem("Beenden", (_, _) => Application.Current.Shutdown()));
        _icon.ContextMenu = menu;
        _icon.TrayMouseDoubleClick += (_, _) => ShowMain();
    }

    private void TogglePause()
    {
        var s = _bootstrap.Value.Settings;
        s.ControllerPaused = !s.ControllerPaused;
        _bootstrap.Value.SaveSettings(s);
    }

    private static void ShowMain()
    {
        var main = Application.Current.MainWindow;
        if (main is null) return;
        main.Show();
        main.WindowState = WindowState.Normal;
        main.Activate();
    }

    private static System.Windows.Controls.MenuItem CreateItem(string header, RoutedEventHandler handler)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    public void Dispose() => _icon?.Dispose();
}
