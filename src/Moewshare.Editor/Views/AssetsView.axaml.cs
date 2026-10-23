using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public partial class AssetsView : UserControl
{
    AssetItemVM? _draggedAsset;
    AssetItemVM? _hoveredDropTarget;
    Point _dragStartPoint;
    bool _isDragging;

    public AssetsView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnAssetsPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnAssetsPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnAssetsPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(DoubleTappedEvent, OnAssetsDoubleTapped, RoutingStrategies.Bubble);
    }

    AssetItemVM? GetItemUnderPointer(Point posInListBox)
    {
        var lb = this.FindControl<ListBox>("AssetsListBox");
        if (lb is null) return null;

        foreach (var lbi in lb.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (lbi.IsVisible && lbi.DataContext is AssetItemVM vm)
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

    void OnAssetsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<Button>() is not null)
            return;

        if (DataContext is not AssetsPanelVM panelVm)
            return;

        var prop = e.GetCurrentPoint(this).Properties;
        if (!prop.IsLeftButtonPressed)
            return;

        var lb = this.FindControl<ListBox>("AssetsListBox");
        var posInListBox = lb is not null ? e.GetPosition(lb) : e.GetPosition(this);
        var item = GetItemUnderPointer(posInListBox);

        if (item is not null)
        {
            _draggedAsset = item;
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;
        }
        else
        {
            _draggedAsset = null;
            _isDragging = false;
        }
    }

    void OnAssetsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedAsset is null || DataContext is not AssetsPanelVM panelVm)
            return;

        var prop = e.GetCurrentPoint(this).Properties;
        if (!prop.IsLeftButtonPressed)
            return;

        var cur = e.GetPosition(this);
        if (!_isDragging && (Math.Abs(cur.X - _dragStartPoint.X) > 6 || Math.Abs(cur.Y - _dragStartPoint.Y) > 6))
        {
            _isDragging = true;
            panelVm.Editor.DraggedAsset = _draggedAsset;
        }

        if (_isDragging)
        {
            var lb = this.FindControl<ListBox>("AssetsListBox");
            var posInListBox = lb is not null ? e.GetPosition(lb) : cur;
            var targetItem = GetItemUnderPointer(posInListBox);

            if (targetItem is not null && targetItem != _draggedAsset && targetItem.IsDirectory)
            {
                if (_hoveredDropTarget != targetItem)
                {
                    if (_hoveredDropTarget is not null) _hoveredDropTarget.IsDropTarget = false;
                    _hoveredDropTarget = targetItem;
                    _hoveredDropTarget.IsDropTarget = true;
                }
            }
            else
            {
                if (_hoveredDropTarget is not null)
                {
                    _hoveredDropTarget.IsDropTarget = false;
                    _hoveredDropTarget = null;
                }
            }
        }
    }

    void OnAssetsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var targetCandidate = _hoveredDropTarget;
        if (_hoveredDropTarget is not null)
        {
            _hoveredDropTarget.IsDropTarget = false;
            _hoveredDropTarget = null;
        }

        if (DataContext is AssetsPanelVM panelVm)
        {
            if (_isDragging && _draggedAsset is not null)
            {
                var lb = this.FindControl<ListBox>("AssetsListBox");
                var posInListBox = lb is not null ? e.GetPosition(lb) : e.GetPosition(this);
                var targetItem = targetCandidate ?? GetItemUnderPointer(posInListBox);

                // If dropped onto a folder in AssetsView:
                if (targetItem is not null && targetItem != _draggedAsset && targetItem.IsDirectory)
                {
                    panelVm.Editor.MoveAssetToFolder(_draggedAsset, targetItem);
                    panelVm.Editor.DraggedAsset = null;
                    e.Handled = true;
                }
                // If dropped outside AssetsView or elsewhere: DraggedAsset remains set in Editor
                // so ScenePreview (Viewport) can catch it on its own pointer release!
            }
        }

        _isDragging = false;
    }

    void OnAssetsDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AssetsPanelVM panelVm) return;

        var lb = this.FindControl<ListBox>("AssetsListBox");
        if (lb?.SelectedItem is AssetItemVM item)
        {
            if (item.IsDirectory)
            {
                panelVm.Editor.NavigateIntoFolder(item);
                e.Handled = true;
            }
            else if (item.IsModel || item.IsTexture)
            {
                panelVm.Editor.SpawnAssetOnScene(item, null);
                e.Handled = true;
            }
        }
    }
}
