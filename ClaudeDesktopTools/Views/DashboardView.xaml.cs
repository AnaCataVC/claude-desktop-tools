using System;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools.Views;

public sealed partial class DashboardView : Page
{
    public DashboardViewModel ViewModel { get; }

    // Semaphore prevents WinUI 3 dialog collision crashes
    private static readonly SemaphoreSlim _dialogLock = new(1, 1);

    public DashboardView()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        this.InitializeComponent();
        this.Loaded += async (s, e) => await ViewModel.ScanAsync();
    }

    private async void DeleteTranscripts_Click(object sender, RoutedEventArgs e)
    {
        if (!await _dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Liberar espacio en disco",
                Content = "Esta acción eliminará de forma permanente los transcripts de Claude CLI anteriores a la retención configurada.\n\n" +
                          "Nota de seguridad: Cualquier archivo modificado en las últimas 24 horas se conservará intacto independientemente de la retención.",
                PrimaryButtonText = "Eliminar y liberar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteStaleTranscriptsAsync();
            }
        }
        finally
        {
            _dialogLock.Release();
        }
    }
}
