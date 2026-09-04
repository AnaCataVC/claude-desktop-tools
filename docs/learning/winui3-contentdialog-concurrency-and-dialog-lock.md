# Engineering Learning: WinUI 3 ContentDialog Concurrency & Anti-Collision Semaphore

> **Date:** 2026-09-04  
> **Status:** Implemented in `ClaudeDesktopTools.Views.DashboardView`  
> **Target Framework:** .NET 9 (`net9.0-windows10.0.26100.0`, Unpackaged)  
> **Origin:** Decoupled and elevated from `work-activity-panel`

---

## 1. Context & The WinUI 3 `ContentDialog` Collision Pitfall

In WinUI 3 and the Windows App SDK, the XAML framework enforces a strict modal dialog invariant:
> **Only a single `ContentDialog` can be open per `XamlRoot` at any given time.**

If an application attempts to call `dialog.ShowAsync()` while another dialog is active, or if the user rapidly double-clicks a destructive action button before the layout animation completes, the WinUI 3 runtime crashes with an unhandled fatal COM exception:
```text
System.Exception: Only a single ContentDialog can be open at any time.
(Exception from HRESULT: 0x80000018 or 0x80004005 E_FAIL)
```

---

## 2. Engineered Solution: Zero-Wait Semaphore Guard

In `ClaudeDesktopTools.Views.DashboardView`, the destructive transcript deletion workflow is protected using a non-blocking, zero-wait semaphore:

```csharp
private static readonly SemaphoreSlim _dialogLock = new(1, 1);

private async void DeleteTranscripts_Click(object sender, RoutedEventArgs e)
{
    // 1. Zero-wait non-blocking entry check
    if (!await _dialogLock.WaitAsync(0))
    {
        return; // Dialog is already active; silently ignore redundant click
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
```

### Key Guarantees:
1. `WaitAsync(0)` evaluates synchronously without putting the UI thread to sleep.
2. Rapid clicks return immediately, completely neutralizing double-submit race conditions.
3. The `finally` block guarantees that `_dialogLock.Release()` is executed regardless of user dismissal or unhandled task exceptions.
