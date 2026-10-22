using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Moewshare.Launcher.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string okText = "Да");
    Task ShowMessageAsync(string title, string message);
    Task<string?> PromptAsync(string title, string message, string initial);
    Task<string?> PickFolderAsync(string title);
}

public sealed class DialogService : IDialogService
{
    static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<bool> ConfirmAsync(string title, string message, string okText = "Да") =>
        await ShowAsync(title, message, null, okText, true) is not null;

    public Task ShowMessageAsync(string title, string message) => ShowAsync(title, message, null, "OK", false);

    public Task<string?> PromptAsync(string title, string message, string initial) =>
        ShowAsync(title, message, initial, "OK", true);

    public async Task<string?> PickFolderAsync(string title)
    {
        if (Owner is null) return null;
        var r = await Owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return r.Count > 0 ? r[0].TryGetLocalPath() : null;
    }

    static async Task<string?> ShowAsync(string title, string message, string? input, string okText, bool withCancel)
    {
        if (Owner is null) return null;
        string? result = null;
        var tb = input is null ? null : new TextBox { Text = input, MinWidth = 340 };
        var ok = new Button { Content = okText, Classes = { "primary" } };
        var cancel = new Button { Content = "Отмена", Classes = { "soft" } };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (withCancel) buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        if (tb is not null) panel.Children.Add(tb);
        panel.Children.Add(buttons);

        var w = new Window
        {
            Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#FFF4F3"),
            Foreground = Brush.Parse("#482C30")
        };
        ok.Click += (_, _) => { result = tb?.Text ?? "ok"; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        await w.ShowDialog(Owner);
        return result;
    }
}
