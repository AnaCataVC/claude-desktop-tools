using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools.Views;

public sealed partial class SessionsView : Page
{
    public SessionsViewModel ViewModel { get; }

    public SessionsView()
    {
        ViewModel = App.Services.GetRequiredService<SessionsViewModel>();
        this.InitializeComponent();
        this.Loaded += async (s, e) => await ViewModel.LoadSessionsAsync();
    }
}
