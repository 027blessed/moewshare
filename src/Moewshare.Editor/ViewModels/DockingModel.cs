using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Moewshare.Editor.ViewModels;

public enum DockZone { Left, Right, Bottom, Center }

public abstract partial class EditorPanelVM : ObservableObject
{
    public string Id { get; init; }
    [ObservableProperty] string _title = "";
    [ObservableProperty] string _icon = "";
    [ObservableProperty] DockZone _zone;
    [ObservableProperty] int _order;
    [ObservableProperty] bool _isVisible = true;

    public EditorViewModel Editor { get; }
    readonly Action<EditorPanelVM, DockZone> _onMove;

    protected EditorPanelVM(string id, string title, string icon, DockZone defaultZone, int order, EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
    {
        Id = id;
        Title = title;
        Icon = icon;
        Zone = defaultZone;
        Order = order;
        Editor = editor;
        _onMove = onMove;
    }

    [RelayCommand] public void MoveToLeft() => _onMove(this, DockZone.Left);
    [RelayCommand] public void MoveToRight() => _onMove(this, DockZone.Right);
    [RelayCommand] public void MoveToBottom() => _onMove(this, DockZone.Bottom);
    [RelayCommand] public void MoveToCenter() => _onMove(this, DockZone.Center);

    [RelayCommand] public void Activate()
    {
        switch (Zone)
        {
            case DockZone.Left: Editor.ActiveLeftPanel = this; break;
            case DockZone.Right: Editor.ActiveRightPanel = this; break;
            case DockZone.Bottom: Editor.ActiveBottomPanel = this; break;
            case DockZone.Center: Editor.ActiveCenterPanel = this; break;
        }
    }
}

public sealed class HierarchyPanelVM(EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
    : EditorPanelVM("hierarchy", "Иерархия", "🐾", DockZone.Left, 0, editor, onMove);

public sealed class ViewportPanelVM(EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
    : EditorPanelVM("viewport", "Сцена 3D", "🎬", DockZone.Center, 0, editor, onMove);

public sealed class EventGraphPanelVM : EditorPanelVM
{
    public EventGraphVM GraphVM { get; }

    public EventGraphPanelVM(EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
        : base("eventgraph", "События (Event Graph)", "⚡", DockZone.Center, 1, editor, onMove)
    {
        GraphVM = new EventGraphVM(editor);
    }
}

public sealed partial class InspectorPanelVM(EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
    : EditorPanelVM("inspector", "Инспектор", "🎨", DockZone.Right, 0, editor, onMove)
{
    [RelayCommand] public void SetTab(string tab) => Editor.InspectorTabIndex = int.Parse(tab);
}

public sealed class AssetsPanelVM(EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
    : EditorPanelVM("assets", "Ресурсы", "📁", DockZone.Bottom, 0, editor, onMove);

public sealed class ConsolePanelVM(EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
    : EditorPanelVM("console", "Консоль", "📜", DockZone.Bottom, 1, editor, onMove);

public sealed class GamePanelVM(EditorViewModel editor, Action<EditorPanelVM, DockZone> onMove)
    : EditorPanelVM("game", "Запуск & Web", "🚀", DockZone.Bottom, 2, editor, onMove);
