using Avalonia.Controls;
using Avalonia.Input;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public partial class MainWindow : Window
{
    bool _closing;
    public MainWindow() => InitializeComponent();

    public void Attach(EditorViewModel vm)
    {
        DataContext = vm;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Source is TextBox) return;

        if (DataContext is EditorViewModel vm)
        {
            // Delete Hotkeys:
            // macOS: Cmd + Backspace / Cmd + Delete
            // Windows: Delete
            bool isMacDelete = e.KeyModifiers.HasFlag(KeyModifiers.Meta) && (e.Key is Key.Back or Key.Delete);
            bool isWinDelete = !e.KeyModifiers.HasFlag(KeyModifiers.Meta) && (e.Key is Key.Delete);

            if (isMacDelete || isWinDelete)
            {
                if (vm.Selected is not null || vm.SelectedObjects.Count > 0)
                {
                    e.Handled = true;
                    _ = vm.DeleteSelectedCommand.ExecuteAsync(null);
                    return;
                }
            }

            // Duplicate Hotkey: Cmd+D (Mac) / Ctrl+D (Win)
            bool isMacDup = e.KeyModifiers.HasFlag(KeyModifiers.Meta) && e.Key == Key.D;
            bool isWinDup = e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.D;
            if (isMacDup || isWinDup)
            {
                if (vm.Selected is not null || vm.SelectedObjects.Count > 0)
                {
                    e.Handled = true;
                    vm.DuplicateSelectedCommand.Execute(null);
                    return;
                }
            }

            // Save Hotkey: Cmd+S (Mac) / Ctrl+S (Win)
            bool isMacSave = e.KeyModifiers.HasFlag(KeyModifiers.Meta) && e.Key == Key.S;
            bool isWinSave = e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.S;
            if (isMacSave || isWinSave)
            {
                e.Handled = true;
                _ = vm.SaveCommand.ExecuteAsync(null);
                return;
            }
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_closing || DataContext is not EditorViewModel vm) { base.OnClosing(e); return; }
        if (vm.IsDirty)
        {
            e.Cancel = true;
            var ui = new WindowUi(this);
            if (await ui.ConfirmAsync("Несохранённые изменения", "Сохранить сцену перед выходом? («Отмена» — остаться в редакторе)", "Сохранить"))
            {
                await vm.SaveCommand.ExecuteAsync(null);
                if (vm.IsDirty) return; // сохранение не удалось
                _closing = true; vm.Dispose(); Close();
            }
            return;
        }
        _closing = true; vm.Dispose();
        base.OnClosing(e);
    }
}
