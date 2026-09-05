using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ClaudeDesktopTools.Views;

namespace ClaudeDesktopTools;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        TrySetMicaBackdrop();
        ExtendsContentIntoTitleBar = true;

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    private void TrySetMicaBackdrop()
    {
        if (MicaController.IsSupported())
        {
            this.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardView));
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "Dashboard":
                    ContentFrame.Navigate(typeof(DashboardView));
                    break;
                case "Sessions":
                    ContentFrame.Navigate(typeof(SessionsView));
                    break;
                case "Context":
                    ContentFrame.Navigate(typeof(ContextDiscoveryView));
                    break;
                case "ProcessMonitor":
                    ContentFrame.Navigate(typeof(ProcessMonitorView));
                    break;
                case "Settings":
                    ContentFrame.Navigate(typeof(SettingsView));
                    break;
            }
        }
    }
}
