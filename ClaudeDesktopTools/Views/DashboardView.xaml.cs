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
            var preview = await ViewModel.GetStaleTranscriptsPreviewAsync();

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Liberar espacio en disco",
                Content = PreviewDialogHelper.BuildPreviewContent(
                    "Esta acción eliminará de forma permanente los transcripts de Claude CLI anteriores a la retención configurada.\n\n" +
                    "Nota de seguridad: Cualquier archivo modificado en las últimas 24 horas se conservará intacto independientemente de la retención.",
                    preview,
                    "No hay transcripts fuera de la retención configurada.",
                    $"Se eliminarán {preview.Count} transcripts:"),
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

    private async void ArchiveStaleSessions_Click(object sender, RoutedEventArgs e)
    {
        if (!await _dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var preview = await ViewModel.GetStaleDesktopSessionsPreviewAsync();

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Archivar sesiones antiguas",
                Content = PreviewDialogHelper.BuildPreviewContent(
                    "Esta acción marca las sesiones como archivadas para que salgan de la lista de Claude Desktop. No libera espacio en disco.",
                    preview,
                    "No hay sesiones fuera de la retención configurada.",
                    $"Se archivarán {preview.Count} sesiones:"),
                PrimaryButtonText = "Archivar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.ArchiveStaleSessionsAsync();
            }
        }
        finally
        {
            _dialogLock.Release();
        }
    }
}
