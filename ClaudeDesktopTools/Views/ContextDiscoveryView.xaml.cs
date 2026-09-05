using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools.Views;

public sealed partial class ContextDiscoveryView : Page
{
    public ContextDiscoveryViewModel ViewModel { get; }

    public ContextDiscoveryView()
    {
        ViewModel = App.Services.GetRequiredService<ContextDiscoveryViewModel>();
        this.InitializeComponent();
    }

    public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private void SelectAllInGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CandidateGroup group })
        {
            ViewModel.SetGroupSelection(group, true);
        }
    }

    private void DeselectAllInGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CandidateGroup group })
        {
            ViewModel.SetGroupSelection(group, false);
        }
    }
}
