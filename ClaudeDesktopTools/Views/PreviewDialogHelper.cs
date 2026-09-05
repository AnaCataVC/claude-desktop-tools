using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using ClaudeDesktopTools.Models;

namespace ClaudeDesktopTools.Views;

/// <summary>Builds the scrollable "here's what this will affect" content for a destructive-action ContentDialog.</summary>
internal static class PreviewDialogHelper
{
    private const int MaxPreviewItems = 20;

    public static StackPanel BuildPreviewContent(
        string introText, List<ClaudeSessionItem> items, string emptyText, string countText)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 480 };
        panel.Children.Add(new TextBlock { Text = introText, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });

        if (items.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = emptyText, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
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
            Content = new TextBlock { Text = namesText, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap }
        });

        return panel;
    }
}
