using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine.Events;

namespace Moewshare.Editor.Views;

public partial class EventGraphView : UserControl
{
    EventGraphVM? _vm;
    /// <summary>Точка графа, куда поставить блок из каталога (двойной клик / контекстное меню).</summary>
    (double X, double Y)? _pendingSpawn;

    public EventGraphView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        CanvasCtrl.RequestNodePalette += OnRequestNodePalette;
        // Горячие клавиши, когда фокус в боковой панели (но не в поле ввода)
        AddHandler(KeyDownEvent, OnViewKeyDown, RoutingStrategies.Bubble);
    }

    void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is EventGraphPanelVM panelVm) Attach(panelVm.GraphVM);
        else if (DataContext is EventGraphVM graphVm) Attach(graphVm);
    }

    void Attach(EventGraphVM vm)
    {
        _vm = vm;
        CanvasCtrl.Attach(vm);
    }

    void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _vm is null) return;
        if (e.Source is TextBox || e.Source is ComboBox) return;
        if (EventGraphCanvas.HandleGraphKey(_vm, e, null)) e.Handled = true;
    }

    // ---------- Каталог блоков ----------
    void OnRequestNodePalette(double x, double y)
    {
        _pendingSpawn = (x, y);
        (AddNodeFlyoutButton.Flyout as PopupFlyoutBase)?.ShowAt(CanvasCtrl, true);
    }

    void OnPaletteOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PaletteSearch.Focus();
            PaletteSearch.SelectAll();
        }, DispatcherPriority.Input);
    }

    void OnPaletteClosed(object? sender, EventArgs e)
    {
        _pendingSpawn = null;
        CanvasCtrl.Focus();
    }

    void OnPaletteSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Enter && _vm.FilteredLibraryNodes.Count > 0)
        {
            Spawn(_vm.FilteredLibraryNodes[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AddNodeFlyoutButton.Flyout?.Hide();
            e.Handled = true;
        }
    }

    void OnAddNodeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is NodeDescriptor desc) Spawn(desc);
    }

    void Spawn(NodeDescriptor desc)
    {
        if (_vm is null) return;
        if (_pendingSpawn is { } at) _vm.AddNodeAt(desc.Type, at.X, at.Y);
        else _vm.AddNode(desc);
        _pendingSpawn = null;
        AddNodeFlyoutButton.Flyout?.Hide();
    }

    // ---------- Вид ----------
    void OnCenterClick(object? sender, RoutedEventArgs e) => CanvasCtrl.CenterOnNodes();
    void OnZoomInClick(object? sender, RoutedEventArgs e) => CanvasCtrl.ZoomIn();
    void OnZoomOutClick(object? sender, RoutedEventArgs e) => CanvasCtrl.ZoomOut();
    void OnZoomResetClick(object? sender, RoutedEventArgs e) => CanvasCtrl.ZoomReset();

    // ---------- Группы ----------
    void OnGroupColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) _vm?.SetGroupColor(hex);
    }

    // ---------- Переменные ----------
    void OnRemoveVariableClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GraphVariable v) _vm?.RemoveVariable(v);
    }

    void OnSpawnGetVariableClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GraphVariable v) _vm?.SpawnGetVariable(v);
    }

    void OnSpawnSetVariableClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GraphVariable v) _vm?.SpawnSetVariable(v);
    }
}
