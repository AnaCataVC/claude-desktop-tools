using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools.Views;

public sealed partial class SettingsView : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsView()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        this.InitializeComponent();
    }
}
