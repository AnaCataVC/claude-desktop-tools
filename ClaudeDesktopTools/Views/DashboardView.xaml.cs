using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.ViewModels;

namespace ClaudeDesktopTools.Views;

public sealed partial class DashboardView : Page
{
    private const int MaxPreviewItems = 20;

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
                Content = BuildPreviewContent(
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
                Content = BuildPreviewContent(
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

    private static StackPanel BuildPreviewContent(
        string introText, List<ClaudeSessionItem> items, string emptyText, string countText)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 480 };
        panel.Children.Add(new TextBlock { Text = introText, TextWrapping = TextWrapping.Wrap });

        if (items.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = emptyText, TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        panel.Children.Add(new TextBlock { Text = countText, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var names = items
            .Take(MaxPreviewItems)
            .Select(i => $"• {i.WorkingDirectory}  ({i.FileSizeDisplay})");
        var namesText = string.Join("\n", names);
        if (items.Count > MaxPreviewItems)
        {
            namesText += $"\n… y {items.Count - MaxPreviewItems} más.";
        }

        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 240,
            Content = new TextBlock { Text = namesText, TextWrapping = TextWrapping.Wrap }
        });

        return panel;
    }
}
