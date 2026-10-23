using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public partial class HierarchyView : UserControl
{
    ObjectVM? _draggedItem;
    ObjectVM? _currentDropTarget;
    Point _dragStartPoint;
    bool _isDragging;

    public HierarchyView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnHierarchyPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnHierarchyPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnHierarchyPointerReleased, RoutingStrategies.Tunnel);
    }

    ObjectVM? GetItemUnderPointer(Point posInListBox)
    {
        var lb = this.FindControl<ListBox>("ObjectsListBox");
        if (lb is null) return null;

        foreach (var lbi in lb.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (lbi.IsVisible && lbi.DataContext is ObjectVM vm)
            {
                var transform = lbi.TransformToVisual(lb);
                if (transform.HasValue)
                {
                    var rect = new Rect(transform.Value.Transform(new Point(0, 0)), lbi.Bounds.Size);
                    if (rect.Contains(posInListBox))
                    {
                        return vm;
                    }
                }
            }
        }
        return null;
    }

    void OnHierarchyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Don't intercept if clicking an interactive button (e.g. foldout arrow, unparent button)
        if ((e.Source as Visual)?.FindAncestorOfType<Button>() is not null)
            return;

        if (DataContext is not HierarchyPanelVM panelVm)
            return;

        var prop = e.GetCurrentPoint(this).Properties;
        if (!prop.IsLeftButtonPressed)
            return;

        var lb = this.FindControl<ListBox>("ObjectsListBox");
        var posInListBox = lb is not null ? e.GetPosition(lb) : e.GetPosition(this);
        var vm = GetItemUnderPointer(posInListBox);

        if (vm is not null)
        {
            _draggedItem = vm;
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;

            bool isMulti = e.KeyModifiers.HasFlag(KeyModifiers.Meta) ||
                           e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                           e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            if (isMulti)
            {
                panelVm.Editor.ToggleSelection(vm, multiSelect: true);
            }
            else
            {
                if (!vm.IsSelected)
                {
                    panelVm.Editor.SelectObject(vm);
                }
            }
        }
        else
        {
            // Clicked in empty space inside hierarchy panel
            _draggedItem = null;
            _isDragging = false;
            panelVm.Editor.DeselectAll();
        }
    }

    void OnHierarchyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedItem is null || _draggedItem.IsLocked || DataContext is not HierarchyPanelVM panelVm)
            return;

        var prop = e.GetCurrentPoint(this).Properties;
        if (!prop.IsLeftButtonPressed)
            return;

        var cur = e.GetPosition(this);
        // Robust threshold (6px) so mouse or trackpad dragging triggers smoothly
        if (!_isDragging && (Math.Abs(cur.X - _dragStartPoint.X) > 6 || Math.Abs(cur.Y - _dragStartPoint.Y) > 6))
        {
            _isDragging = true;
        }

        if (_isDragging)
        {
            var lb = this.FindControl<ListBox>("ObjectsListBox");
            var posInListBox = lb is not null ? e.GetPosition(lb) : cur;
            var targetVm = GetItemUnderPointer(posInListBox);

            if (targetVm is not null && targetVm != _draggedItem && !targetVm.IsLocked && panelVm.Editor.CanAttach(_draggedItem, targetVm))
            {
                if (_currentDropTarget != targetVm)
                {
                    if (_currentDropTarget is not null) _currentDropTarget.IsDropTarget = false;
                    _currentDropTarget = targetVm;
                    _currentDropTarget.IsDropTarget = true;
                }
            }
            else
            {
                if (_currentDropTarget is not null)
                {
                    _currentDropTarget.IsDropTarget = false;
                    _currentDropTarget = null;
                }
            }
        }
    }

    void OnHierarchyPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var targetCandidate = _currentDropTarget;
        if (_currentDropTarget is not null)
        {
            _currentDropTarget.IsDropTarget = false;
            _currentDropTarget = null;
        }

        if (DataContext is HierarchyPanelVM panelVm)
        {
            if (_isDragging && _draggedItem is not null)
            {
                var lb = this.FindControl<ListBox>("ObjectsListBox");
                var posInListBox = lb is not null ? e.GetPosition(lb) : e.GetPosition(this);
                var targetVm = targetCandidate ?? GetItemUnderPointer(posInListBox);

                var itemsToMove = (panelVm.Editor.SelectedObjects.Contains(_draggedItem) && panelVm.Editor.SelectedObjects.Count > 1)
                    ? panelVm.Editor.SelectedObjects.ToList()
                    : [_draggedItem];

                if (targetVm is not null && targetVm != _draggedItem)
                {
                    foreach (var obj in itemsToMove)
                    {
                        if (panelVm.Editor.CanAttach(obj, targetVm))
                        {
                            panelVm.Editor.AttachTo(obj, targetVm);
                        }
                    }
                    targetVm.IsExpanded = true;
                    panelVm.Editor.RefreshHierarchyTree();
                    panelVm.Editor.SelectObject(_draggedItem);
                    e.Handled = true;
                }
            }
            else if (!_isDragging && _draggedItem is not null)
            {
                bool isMulti = e.KeyModifiers.HasFlag(KeyModifiers.Meta) ||
                               e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                               e.KeyModifiers.HasFlag(KeyModifiers.Shift);

                if (!isMulti)
                {
                    panelVm.Editor.SelectObject(_draggedItem);
                }
            }
        }

        _draggedItem = null;
        _isDragging = false;
    }
}
