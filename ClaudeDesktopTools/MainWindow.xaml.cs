using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using ClaudeDesktopTools.Views;

namespace ClaudeDesktopTools;

public sealed partial class MainWindow : Window
{
    // Below this, the header/action bar content some pages pack into their ListView.Header no
    // longer fits comfortably next to the NavigationView pane -- a floor keeps a resized (or
    // small-screen) window usable instead of relying on every page to reflow perfectly.
    private const int MinWindowWidth = 900;
    private const int MinWindowHeight = 600;

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

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWindowWidth;
            presenter.PreferredMinimumHeight = MinWindowHeight;
        }

        if (AppWindow.Size.Width < MinWindowWidth || AppWindow.Size.Height < MinWindowHeight)
        {
            AppWindow.Resize(new SizeInt32(
                System.Math.Max(AppWindow.Size.Width, MinWindowWidth),
                System.Math.Max(AppWindow.Size.Height, MinWindowHeight)));
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
