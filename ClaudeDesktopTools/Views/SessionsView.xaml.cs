using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools.Views;

public sealed partial class SessionsView : Page
{
    public SessionsViewModel ViewModel { get; }

    // Semaphore prevents WinUI 3 dialog collision crashes
    private static readonly SemaphoreSlim _dialogLock = new(1, 1);

    public SessionsView()
    {
        ViewModel = App.Services.GetRequiredService<SessionsViewModel>();
        this.InitializeComponent();
        this.Loaded += async (s, e) => await ViewModel.LoadSessionsAsync();
    }

    public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    private async void CloseSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ClaudeSessionItem item } || item.ProcessId is null)
        {
            return;
        }

        if (!await _dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Cerrar sesión",
                Content = $"Esto termina de inmediato el proceso de Claude Code (PID {item.ProcessId}) en \"{item.WorkingDirectory}\". " +
                          "Cualquier tarea en curso en esa sesión se corta ahí mismo. No se puede deshacer.",
                PrimaryButtonText = "Cerrar sesión",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.CloseSessionCommand.ExecuteAsync(item);
            }
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private async void DeleteTranscript_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ClaudeSessionItem item })
        {
            return;
        }

        if (!await _dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Liberar espacio",
                Content = $"Esto elimina de forma permanente el transcript de \"{item.WorkingDirectory}\" ({item.FileSizeDisplay}, última modificación {item.LastModified}). " +
                          "No se puede deshacer.",
                PrimaryButtonText = "Eliminar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteTranscriptCommand.ExecuteAsync(item);
            }
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private async void DeleteAllInactive_Click(object sender, RoutedEventArgs e)
    {
        if (!await _dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var preview = ViewModel.GetInactiveSessionsPreview();

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Eliminar todas las inactivas",
                Content = PreviewDialogHelper.BuildPreviewContent(
                    "Esta acción elimina de forma permanente los transcripts de todas las sesiones inactivas listadas.\n\n" +
                    "Nota de seguridad: cualquier archivo modificado en las últimas 24 horas se conservará intacto sin importar esta acción.",
                    preview,
                    "No hay sesiones inactivas para eliminar.",
                    $"Se eliminarán {preview.Count} transcripts:"),
                PrimaryButtonText = "Eliminar todas",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteInactiveSessionsCommand.ExecuteAsync(preview);
            }
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private async void DeleteInactiveOlderThan_Click(object sender, RoutedEventArgs e)
    {
        if (!await _dialogLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            int days = ViewModel.BulkDeleteOlderThanDays;
            var preview = ViewModel.GetInactiveSessionsOlderThanPreview(days);

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = $"Eliminar inactivas de más de {days} días",
                Content = PreviewDialogHelper.BuildPreviewContent(
                    $"Esta acción elimina de forma permanente los transcripts de las sesiones inactivas con más de {days} días desde su última modificación.\n\n" +
                    "Nota de seguridad: cualquier archivo modificado en las últimas 24 horas se conservará intacto sin importar esta acción.",
                    preview,
                    $"No hay sesiones inactivas con más de {days} días.",
                    $"Se eliminarán {preview.Count} transcripts:"),
                PrimaryButtonText = "Eliminar",
                CloseButtonText = "Cancelar",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteInactiveSessionsCommand.ExecuteAsync(preview);
            }
        }
        finally
        {
            _dialogLock.Release();
        }
    }
}
