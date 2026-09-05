using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools.Views;

public sealed partial class ProcessMonitorView : Page
{
    public ProcessMonitorViewModel ViewModel { get; }

    private readonly DispatcherTimer _refreshTimer;

    public ProcessMonitorView()
    {
        ViewModel = App.Services.GetRequiredService<ProcessMonitorViewModel>();
        this.InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (s, e) => _ = ViewModel.RefreshCommand.ExecuteAsync(null);

        this.Loaded += (s, e) =>
        {
            _ = ViewModel.RefreshCommand.ExecuteAsync(null);
            _refreshTimer.Start();
        };
        this.Unloaded += (s, e) => _refreshTimer.Stop();
    }

    private void TrimRam_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ClaudeProcessInfo item }) return;
        _ = ViewModel.TrimWorkingSetCommand.ExecuteAsync(item);
    }

    private void TogglePriority_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ClaudeProcessInfo item }) return;
        _ = ViewModel.ToggleLowPriorityCommand.ExecuteAsync(item);
    }
}
