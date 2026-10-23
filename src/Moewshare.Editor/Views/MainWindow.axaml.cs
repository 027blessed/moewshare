using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Moewshare.Editor.Services;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine;

namespace Moewshare.Editor.Views;

public partial class MainWindow : Window
{
    bool _closing;
    EditorViewModel? _vm;
    GridLength _bottomHeight = new(230);
    GridLength _leftWidth = new(270);
    GridLength _rightWidth = new(340);

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.Changed += UpdateThemeButton;
        ActualThemeVariantChanged += (_, _) => UpdateThemeButton();
        UpdateThemeButton();
    }

    public void Attach(EditorViewModel vm)
    {
        DataContext = vm;
        if (_vm == vm) return;
        _vm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.LeftPanels.CollectionChanged += (_, _) => UpdateLayoutZones();
        vm.RightPanels.CollectionChanged += (_, _) => UpdateLayoutZones();
        vm.BottomPanels.CollectionChanged += (_, _) => UpdateLayoutZones();
        vm.CenterPanels.CollectionChanged += (_, _) => UpdateLayoutZones();
        UpdateLayoutZones();
    }

    void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.IsBottomExpanded)) UpdateLayoutZones();
    }

    void UpdateThemeButton()
    {
        if (ThemeButton is null) return;
        ThemeButton.Content = ThemeManager.IsDark ? "☀️" : "🌙";
    }

    // ================== Раскладка зон ==================
    /// <summary>Пустые зоны схлопываются, нижняя панель сворачивается и запоминает свою высоту.</summary>
    void UpdateLayoutZones()
    {
        if (_vm is null) return;
        var cols = WorkspaceGrid.ColumnDefinitions;
        SetSide(cols[0], cols[1], _vm.LeftPanels.Count > 0, ref _leftWidth, 200);
        SetSide(cols[4], cols[3], _vm.RightPanels.Count > 0, ref _rightWidth, 260);

        var rows = CenterGrid.RowDefinitions;
        bool hasBottom = _vm.BottomPanels.Count > 0;
        bool hasCenter = _vm.CenterPanels.Count > 0;

        if (rows[2].Height.IsAbsolute && rows[2].ActualHeight > 40) _bottomHeight = new GridLength(rows[2].ActualHeight);

        if (!hasBottom)
        {
            rows[0].Height = new GridLength(1, GridUnitType.Star);
            rows[0].MinHeight = 160;
            rows[1].Height = new GridLength(0);
            rows[2].MinHeight = 0;
            rows[2].Height = new GridLength(0);
        }
        else if (!_vm.IsBottomExpanded)
        {
            rows[0].Height = new GridLength(1, GridUnitType.Star);
            rows[0].MinHeight = 160;
            rows[1].Height = new GridLength(0);
            rows[2].MinHeight = 0;
            rows[2].Height = GridLength.Auto;
        }
        else if (!hasCenter)
        {
            // Центр пуст — нижняя зона занимает всю высоту
            rows[0].MinHeight = 0;
            rows[0].Height = new GridLength(0);
            rows[1].Height = new GridLength(0);
            rows[2].MinHeight = 120;
            rows[2].Height = new GridLength(1, GridUnitType.Star);
        }
        else
        {
            rows[0].Height = new GridLength(1, GridUnitType.Star);
            rows[0].MinHeight = 160;
            rows[1].Height = new GridLength(4);
            rows[2].MinHeight = 120;
            rows[2].Height = _bottomHeight;
        }
    }

    static void SetSide(ColumnDefinition col, ColumnDefinition splitter, bool visible, ref GridLength remembered, double minWidth)
    {
        if (visible)
        {
            if (col.Width.Value <= 0) col.Width = remembered;
            col.MinWidth = minWidth;
            splitter.Width = new GridLength(4);
        }
        else
        {
            if (col.ActualWidth > 0) remembered = new GridLength(col.ActualWidth);
            col.MinWidth = 0;
            col.Width = new GridLength(0);
            splitter.Width = new GridLength(0);
        }
    }

    // ================== Горячие клавиши ==================
    /// <summary>
    /// Единый обработчик горячих клавиш редактора. На Windows основной модификатор — Ctrl,
    /// на macOS — ⌘. Полный список: Справка → Горячие клавиши (F1).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not EditorViewModel vm) return;
        if (HandleGlobalKey(vm, e)) { e.Handled = true; return; }

        var source = e.Source as Visual;
        bool inText = e.Source is TextBox || source?.FindAncestorOfType<TextBox>() is not null;
        if (inText) return;

        // Отмена / повтор — для сцены и для графа событий (общая история)
        if (Hk.IsRedo(e)) { vm.Redo(); e.Handled = true; return; }
        if (Hk.IsUndo(e)) { vm.Undo(); e.Handled = true; return; }

        // Внутри редактора событий остальные клавиши обрабатывает сам граф
        if (source?.FindAncestorOfType<EventGraphView>(includeSelf: true) is not null) return;
        if (e.Source is ComboBox || source?.FindAncestorOfType<ComboBox>() is not null) return;

        if (HandleSceneKey(vm, e)) e.Handled = true;
    }

    /// <summary>Команды, которые работают из любого места окна (даже из поля ввода).</summary>
    bool HandleGlobalKey(EditorViewModel vm, KeyEventArgs e)
    {
        if (Hk.Cmd(e, Key.S)) { _ = vm.SaveCommand.ExecuteAsync(null); return true; }
        if (Hk.Cmd(e, Key.OemComma)) { _ = vm.OpenSettingsCommand.ExecuteAsync(null); return true; }
        if (Hk.Plain(e, Key.F1) || Hk.Cmd(e, Key.OemQuestion)) { ShowHotkeys(); return true; }
        if (Hk.Is(e, Key.F5, KeyModifiers.Shift)) { vm.StopCommand.Execute(null); return true; }
        if (Hk.Plain(e, Key.F5) || Hk.Cmd(e, Key.P))
        {
            if (vm.IsRunning) vm.StopCommand.Execute(null);
            else _ = vm.RunCommand.ExecuteAsync(null);
            return true;
        }
        if (Hk.Cmd(e, Key.B)) { _ = vm.BuildCommand.ExecuteAsync(null); return true; }
        if (Hk.CmdShift(e, Key.L)) { ThemeManager.Toggle(); vm.StatusText = $"Тема: {ThemeManager.Title(ThemeManager.Mode)}"; return true; }
        if (Hk.CmdShift(e, Key.A)) { _ = vm.OpenAddObjectDialogCommand.ExecuteAsync(null); return true; }
        if (Hk.CmdShift(e, Key.E)) { vm.OpenSelectedObjectEventGraph(); return true; }
        if (Hk.Cmd(e, Key.O)) { vm.OpenSceneCommand.Execute(null); return true; }
        if (Hk.Cmd(e, Key.J)) { vm.ToggleBottomPanel(); return true; }

        // Ctrl+1…7 / ⌘1…7 — переключение панелей
        if (Hk.Mods(e) == Hk.Mod)
        {
            int idx = e.Key switch
            {
                >= Key.D1 and <= Key.D7 => e.Key - Key.D1,
                >= Key.NumPad1 and <= Key.NumPad7 => e.Key - Key.NumPad1,
                _ => -1
            };
            if (idx >= 0) { vm.ActivatePanelByIndex(idx); return true; }
        }
        return false;
    }

    /// <summary>Клавиши для объектов сцены (иерархия, вьюпорт, инспектор).</summary>
    static bool HandleSceneKey(EditorViewModel vm, KeyEventArgs e)
    {
        if (Hk.IsDelete(e))
        {
            if (vm.Selected is null && vm.SelectedObjects.Count == 0) return false;
            _ = vm.DeleteSelectedCommand.ExecuteAsync(null);
            return true;
        }
        if (Hk.Cmd(e, Key.A)) { vm.SelectAll(); return true; }
        if (Hk.Cmd(e, Key.C)) { vm.CopySelected(); return true; }
        if (Hk.Cmd(e, Key.X)) { _ = vm.CutSelectedCommand.ExecuteAsync(null); return true; }
        if (Hk.Cmd(e, Key.V)) { vm.PasteObjects(); return true; }
        if (Hk.Cmd(e, Key.D)) { vm.DuplicateSelected(); return true; }
        if (Hk.Plain(e, Key.F2)) { _ = vm.RenameSelectedCommand.ExecuteAsync(null); return true; }
        if (Hk.Plain(e, Key.Escape)) { vm.DeselectAll(); return true; }
        if (Hk.Plain(e, Key.H)) { vm.ToggleSelectedVisibility(); return true; }
        if (Hk.Plain(e, Key.Q)) { vm.SetGizmoMode("select"); return true; }
        if (Hk.Plain(e, Key.W)) { vm.SetGizmoMode("translate"); return true; }
        if (Hk.Plain(e, Key.E)) { vm.SetGizmoMode("rotate"); return true; }
        if (Hk.Plain(e, Key.R)) { vm.SetGizmoMode("scale"); return true; }
        if (Hk.Plain(e, Key.F)) { vm.FocusOnSelected(); return true; }
        return false;
    }

    // ================== Меню ==================
    void OnHotkeysClick(object? sender, RoutedEventArgs e) => ShowHotkeys();

    void ShowHotkeys()
    {
        var w = new HotkeysWindow();
        _ = w.ShowDialog(this);
    }

    void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        if (_vm is not null) _vm.StatusText = $"Тема: {ThemeManager.Title(ThemeManager.Mode)}";
    }

    void OnThemeMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag })
        {
            var mode = UserPreferences.ParseTheme(tag);
            ThemeManager.Apply(mode);
            if (_vm is not null) _vm.StatusText = $"Тема: {ThemeManager.Title(mode)}";
        }
    }

    void OnPanelMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out var idx))
            _vm?.ActivatePanelByIndex(idx);
    }

    // ================== Закрытие ==================
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
        ThemeManager.Changed -= UpdateThemeButton;
        base.OnClosing(e);
    }
}
