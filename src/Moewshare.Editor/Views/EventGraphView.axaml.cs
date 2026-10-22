using Avalonia.Controls;
using Avalonia.Interactivity;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine.Events;

namespace Moewshare.Editor.Views;

public partial class EventGraphView : UserControl
{
    EventGraphVM? _vm;

    public EventGraphView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is EventGraphPanelVM panelVm)
        {
            _vm = panelVm.GraphVM;
            CanvasCtrl.Attach(panelVm.GraphVM);
        }
        else if (DataContext is EventGraphVM graphVm)
        {
            _vm = graphVm;
            CanvasCtrl.Attach(graphVm);
        }
    }

    void OnCenterClick(object? sender, RoutedEventArgs e)
    {
        CanvasCtrl.CenterOnNodes();
    }

    void OnAddNodeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is NodeDescriptor desc)
        {
            _vm?.AddNode(desc);
            AddNodeFlyoutButton?.Flyout?.Hide();
        }
    }

    void OnRemoveVariableClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GraphVariable v)
        {
            _vm?.RemoveVariable(v);
        }
    }

    void OnSpawnGetVariableClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GraphVariable v)
        {
            _vm?.SpawnGetVariable(v);
        }
    }

    void OnSpawnSetVariableClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GraphVariable v)
        {
            _vm?.SpawnSetVariable(v);
        }
    }
}
