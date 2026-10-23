using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public sealed class WindowUi(Window owner) : IEditorUi
{
    public bool ForceClose { get; set; }

    public async Task<string?> PickFileAsync(string title, string startDir)
    {
        var start = Directory.Exists(startDir) ? await owner.StorageProvider.TryGetFolderFromPathAsync(startDir) : null;
        var r = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
        return r.Count > 0 ? r[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var r = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return r.Count > 0 ? r[0].TryGetLocalPath() : null;
    }

    public Task<bool> ConfirmAsync(string title, string message, string okText) => Show(title, message, okText, true);
    public async Task<string?> PromptAsync(string title, string message, string defaultValue = "")
    {
        string? result = null;
        var txt = new TextBox { Text = defaultValue, Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
        var ok = new Button { Content = "OK", Classes = { "primary" }, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Отмена", Classes = { "soft" }, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(cancel);
        row.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10, Width = 380 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(txt);
        panel.Children.Add(row);
        var w = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        w.Opened += (_, _) => { txt.Focus(); txt.SelectAll(); };
        ok.Click += (_, _) => { result = txt.Text?.Trim(); w.Close(); };
        cancel.Click += (_, _) => w.Close();
        txt.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { result = txt.Text?.Trim(); w.Close(); }
            else if (e.Key == Avalonia.Input.Key.Escape) { w.Close(); }
        };
        await w.ShowDialog(owner);
        return result;
    }
    public async Task MessageAsync(string title, string message) => await Show(title, message, "OK", false);
    public async Task ShowAddObjectDialogAsync(EditorViewModel editor)
    {
        var w = new AddObjectWindow(editor);
        await w.ShowDialog(owner);
    }
    public async Task ShowSettingsDialogAsync(EditorViewModel editor)
    {
        var w = new SettingsWindow(editor);
        await w.ShowDialog(owner);
    }
    public void Close() { owner.Close(); }

    async Task<bool> Show(string title, string message, string okText, bool cancelable)
    {
        var result = false;
        var ok = new Button { Content = okText, Classes = { "primary" } };
        var cancel = new Button { Content = "Отмена", Classes = { "soft" } };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancelable) row.Children.Add(cancel);
        row.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, FontSize = 13 });
        panel.Children.Add(row);
        var w = new Window { Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => { result = true; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        // Enter — подтвердить, Esc — отмена
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { result = true; w.Close(); e.Handled = true; }
            else if (e.Key == Avalonia.Input.Key.Escape) { w.Close(); e.Handled = true; }
        };
        w.Opened += (_, _) => ok.Focus();
        await w.ShowDialog(owner);
        return result;
    }
}
