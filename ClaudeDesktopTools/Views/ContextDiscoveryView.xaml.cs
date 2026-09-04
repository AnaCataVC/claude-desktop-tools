using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
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
}
