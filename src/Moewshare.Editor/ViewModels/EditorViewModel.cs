using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Editor.Configuration;
using Moewshare.Editor.Services;
using Moewshare.Engine;

namespace Moewshare.Editor.ViewModels;

public interface IEditorUi
{
    Task<string?> PickFileAsync(string title, string startDir);
    Task<string?> PickFolderAsync(string title);
    Task<bool> ConfirmAsync(string title, string message, string okText);
    Task<string?> PromptAsync(string title, string message, string defaultValue = "");
    Task MessageAsync(string title, string message);
    Task ShowAddObjectDialogAsync(EditorViewModel editor);
    Task ShowSettingsDialogAsync(EditorViewModel editor);
    void Close();
}

public enum GizmoMode { Select, Translate, Rotate, Scale }

public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    readonly IEditorUi _ui;
    readonly GameSession _session = new();
    ProjectConfig? _project;
    SceneDocument _scene = new();
    string _folder = "", _scenePath = "";

    public SceneDocument CurrentScene => _scene;

    public ObservableCollection<ObjectVM> Objects { get; } = [];
    public ObservableCollection<ObjectVM> DisplayedHierarchyObjects { get; } = [];
    public ObservableCollection<string> Log { get; } = [];
    public ObservableCollection<ConsoleEntry> ConsoleEntries { get; } = [];
    public ObservableCollection<string> Assets { get; } = [];
    public ObservableCollection<AssetItemVM> CurrentFolderAssets { get; } = [];
    public ObservableCollection<string> AvailableTextures { get; } = [];
    public ObservableCollection<string> AvailableModels { get; } = [];

    string _currentAssetDirectory = "";
    public string CurrentAssetDirectory
    {
        get => string.IsNullOrEmpty(_currentAssetDirectory) && !string.IsNullOrEmpty(_folder) ? Path.Combine(_folder, "Assets") : _currentAssetDirectory;
        set
        {
            if (SetProperty(ref _currentAssetDirectory, value))
            {
                OnPropertyChanged(nameof(CurrentAssetRelativePath));
                OnPropertyChanged(nameof(CanNavigateUp));
            }
        }
    }

    public string CurrentAssetRelativePath
    {
        get
        {
            if (string.IsNullOrEmpty(_folder)) return "Assets";
            var cur = string.IsNullOrEmpty(_currentAssetDirectory) ? Path.Combine(_folder, "Assets") : _currentAssetDirectory;
            var rel = Path.GetRelativePath(_folder, cur).Replace('\\', '/');
            return string.IsNullOrEmpty(rel) || rel == "." ? "Assets" : rel;
        }
    }

    public bool CanNavigateUp
    {
        get
        {
            if (string.IsNullOrEmpty(_folder)) return false;
            var rootAssets = Path.GetFullPath(Path.Combine(_folder, "Assets"));
            var cur = Path.GetFullPath(CurrentAssetDirectory);
            return !cur.Equals(rootAssets, StringComparison.OrdinalIgnoreCase) && cur.StartsWith(rootAssets, StringComparison.OrdinalIgnoreCase);
        }
    }

    AssetItemVM? _draggedAsset;
    public AssetItemVM? DraggedAsset
    {
        get => _draggedAsset;
        set => SetProperty(ref _draggedAsset, value);
    }

    public string[] BehaviorList => Behaviors.All;
    public string[] CameraList => CameraModes.All;
    public string[] ColorPresets { get; } =
    [
        "#FFB6C1", "#FF69B4", "#F7CDD5", "#E9A9B8",
        "#AEC6CF", "#77DD77", "#FDFD96", "#B39DDB",
        "#FFFFFF", "#C99A7A", "#4A2929", "#2C3E50"
    ];

    public event Action? SceneChanged;
    public event Action? RequestFocus;

    public string ProjectFolder => _folder;

    [ObservableProperty] bool _isEyeDropperActive;
    [ObservableProperty] bool _isBottomExpanded = true;
    [ObservableProperty] bool _isColorPaletteOpen = true;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelection)), NotifyPropertyChangedFor(nameof(HasMultipleSelection)), NotifyPropertyChangedFor(nameof(SelectionCount))] ObjectVM? _selected;
    public ObservableCollection<ObjectVM> SelectedObjects { get; } = [];
    public bool HasMultipleSelection => SelectedObjects.Count > 1;
    public int SelectionCount => SelectedObjects.Count;
    [ObservableProperty] int _inspectorTabIndex; // 0 = Объект, 1 = Настройки сцены

    [ObservableProperty] string _title = "Moewshare Editor";
    [ObservableProperty] bool _isDirty;
    [ObservableProperty] bool _isRunning;
    [ObservableProperty] bool _isLoaded;
    [ObservableProperty] GizmoMode _currentGizmoMode = GizmoMode.Translate;
    [ObservableProperty] string _consoleFilter = "All";
    [ObservableProperty] string _consoleSearch = "";
    [ObservableProperty] int _errorCount;
    [ObservableProperty] int _warningCount;
    [ObservableProperty] int _infoCount;

    // Docking layout panels
    public ObservableCollection<EditorPanelVM> AllPanels { get; } = [];
    public ObservableCollection<EditorPanelVM> LeftPanels { get; } = [];
    public ObservableCollection<EditorPanelVM> RightPanels { get; } = [];
    public ObservableCollection<EditorPanelVM> BottomPanels { get; } = [];
    public ObservableCollection<EditorPanelVM> CenterPanels { get; } = [];

    [ObservableProperty] EditorPanelVM? _activeLeftPanel;
    [ObservableProperty] EditorPanelVM? _activeRightPanel;
    [ObservableProperty] EditorPanelVM? _activeBottomPanel;
    [ObservableProperty] EditorPanelVM? _activeCenterPanel;

    public bool HasSelection => Selected is not null;
    public MoewshareMcpServer McpServer { get; }

    public EditorViewModel(IEditorUi ui)
    {
        _ui = ui;
        McpServer = new MoewshareMcpServer(this);
        InitPanels();

        var settings = EditorSettingsService.Load();
        if (settings.McpEnabled && settings.AutoStartMcp)
        {
            _ = McpServer.StartAsync(settings.McpPort);
        }
    }

    [RelayCommand]
    public async Task OpenSettingsAsync() => await _ui.ShowSettingsDialogAsync(this);

    void InitPanels()
    {
        AllPanels.Clear();
        AllPanels.Add(new HierarchyPanelVM(this, MovePanel));
        AllPanels.Add(new ViewportPanelVM(this, MovePanel));
        AllPanels.Add(new EventGraphPanelVM(this, MovePanel));
        AllPanels.Add(new InspectorPanelVM(this, MovePanel));
        AllPanels.Add(new AssetsPanelVM(this, MovePanel));
        AllPanels.Add(new ConsolePanelVM(this, MovePanel));
        AllPanels.Add(new GamePanelVM(this, MovePanel));
        foreach (var p in AllPanels)
        {
            var idx = PanelHotkeyOrder.IndexOf(p.Id);
            if (idx >= 0) p.HotkeyHint = $"{p.Title}  ({Hk.Label(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D1 + idx, Hk.Mod))})";
            else p.HotkeyHint = p.Title;
        }
        RefreshZoneCollections();
    }

    [RelayCommand] void SelectLeftPanel(EditorPanelVM p) => ActiveLeftPanel = p;
    [RelayCommand] void SelectRightPanel(EditorPanelVM p) => ActiveRightPanel = p;
    [RelayCommand] void SelectBottomPanel(EditorPanelVM p) => ActiveBottomPanel = p;
    [RelayCommand] void SelectCenterPanel(EditorPanelVM p) => ActiveCenterPanel = p;

    [RelayCommand]
    public void OpenEventGraph()
    {
        var panel = AllPanels.FirstOrDefault(p => p.Id == "eventgraph");
        if (panel is not null)
        {
            panel.Activate();
        }
    }

    [RelayCommand]
    public void OpenSelectedObjectEventGraph()
    {
        if (Selected is null) return;
        var panel = AllPanels.FirstOrDefault(p => p.Id == "eventgraph") as EventGraphPanelVM;
        if (panel is not null)
        {
            panel.GraphVM.SelectObjectGraph(Selected);
            panel.Activate();
        }
    }

    public void MovePanel(EditorPanelVM panel, DockZone targetZone)
    {
        panel.Zone = targetZone;
        RefreshZoneCollections();
        switch (targetZone)
        {
            case DockZone.Left: ActiveLeftPanel = panel; break;
            case DockZone.Right: ActiveRightPanel = panel; break;
            case DockZone.Bottom: ActiveBottomPanel = panel; break;
            case DockZone.Center: ActiveCenterPanel = panel; break;
        }
    }

    [RelayCommand]
    public void ResetLayout()
    {
        foreach (var p in AllPanels)
        {
            p.Zone = p.Id switch
            {
                "hierarchy" => DockZone.Left,
                "inspector" => DockZone.Right,
                "viewport" => DockZone.Center,
                _ => DockZone.Bottom
            };
            p.IsVisible = true;
        }
        RefreshZoneCollections();
        Say("Расположение панелей сброшено по умолчанию.");
    }

    void RefreshZoneCollections()
    {
        LeftPanels.Clear();
        RightPanels.Clear();
        BottomPanels.Clear();
        CenterPanels.Clear();

        foreach (var p in AllPanels.Where(p => p.IsVisible).OrderBy(p => p.Order))
        {
            switch (p.Zone)
            {
                case DockZone.Left: LeftPanels.Add(p); break;
                case DockZone.Right: RightPanels.Add(p); break;
                case DockZone.Bottom: BottomPanels.Add(p); break;
                case DockZone.Center: CenterPanels.Add(p); break;
            }
        }

        if (ActiveLeftPanel == null || !LeftPanels.Contains(ActiveLeftPanel)) ActiveLeftPanel = LeftPanels.FirstOrDefault();
        if (ActiveRightPanel == null || !RightPanels.Contains(ActiveRightPanel)) ActiveRightPanel = RightPanels.FirstOrDefault();
        if (ActiveBottomPanel == null || !BottomPanels.Contains(ActiveBottomPanel)) ActiveBottomPanel = BottomPanels.FirstOrDefault();
        if (ActiveCenterPanel == null || !CenterPanels.Contains(ActiveCenterPanel)) ActiveCenterPanel = CenterPanels.FirstOrDefault();
    }

    public string CameraMode
    {
        get => _scene.Camera.Mode;
        set
        {
            if (value is not null && value != _scene.Camera.Mode)
            {
                _scene.Camera.Mode = value;
                if (value == CameraModes.Side2D && _scene.Camera.Pitch > 10) _scene.Camera.Pitch = 0;
                else if (value == CameraModes.TopDown && _scene.Camera.Pitch < 80) _scene.Camera.Pitch = 90;
                else if (value == CameraModes.Isometric) _scene.Camera.Pitch = 45;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CameraPitch));
                Changed();
            }
        }
    }

    public double CameraPitch
    {
        get => _scene.Camera.Pitch;
        set { _scene.Camera.Pitch = Math.Clamp(Math.Round(value, 1), 0, 90); OnPropertyChanged(); Changed(); }
    }

    public double CameraYaw
    {
        get => _scene.Camera.Yaw;
        set { _scene.Camera.Yaw = Math.Round(value, 1); OnPropertyChanged(); Changed(); }
    }

    public double CameraOffsetY
    {
        get => _scene.Camera.OffsetY;
        set { _scene.Camera.OffsetY = Math.Round(value, 1); OnPropertyChanged(); Changed(); }
    }

    public double CameraOffsetZ
    {
        get => _scene.Camera.OffsetZ;
        set { _scene.Camera.OffsetZ = Math.Round(value, 1); OnPropertyChanged(); Changed(); }
    }

    public string CameraMovePlane
    {
        get => string.IsNullOrEmpty(_scene.Camera.MovePlane) ? MovePlanes.Auto : _scene.Camera.MovePlane;
        set { if (value is not null && value != _scene.Camera.MovePlane) { _scene.Camera.MovePlane = value; OnPropertyChanged(); Changed(); } }
    }

    public string[] MovePlaneList => MovePlanes.All;

    public void ApplyCameraPreset(string preset)
    {
        switch (preset?.ToLowerInvariant())
        {
            case "side2d":
            case "2d":
                _scene.Camera.Mode = CameraModes.Side2D;
                _scene.Camera.Pitch = 0;
                _scene.Camera.Yaw = 0;
                _scene.Camera.MovePlane = MovePlanes.Auto;
                break;
            case "isometric":
            case "iso":
                _scene.Camera.Mode = CameraModes.Isometric;
                _scene.Camera.Pitch = 45;
                _scene.Camera.Yaw = 0;
                _scene.Camera.MovePlane = MovePlanes.Auto;
                break;
            case "topdown":
            case "top":
                _scene.Camera.Mode = CameraModes.TopDown;
                _scene.Camera.Pitch = 90;
                _scene.Camera.Yaw = 0;
                _scene.Camera.MovePlane = MovePlanes.Auto;
                break;
            case "follow3d":
            case "3d":
                _scene.Camera.Mode = CameraModes.Follow3D;
                _scene.Camera.Pitch = 35;
                _scene.Camera.Yaw = 0;
                _scene.Camera.MovePlane = MovePlanes.Auto;
                break;
        }
        OnPropertyChanged(nameof(CameraMode));
        OnPropertyChanged(nameof(CameraPitch));
        OnPropertyChanged(nameof(CameraYaw));
        OnPropertyChanged(nameof(CameraOffsetY));
        OnPropertyChanged(nameof(CameraOffsetZ));
        OnPropertyChanged(nameof(CameraMovePlane));
        Changed();
    }

    [RelayCommand] public void SetPresetSide2D() => ApplyCameraPreset("side2d");
    [RelayCommand] public void SetPresetIsometric() => ApplyCameraPreset("isometric");
    [RelayCommand] public void SetPresetTopDown() => ApplyCameraPreset("topdown");
    [RelayCommand] public void SetPresetFollow3D() => ApplyCameraPreset("follow3d");

    public bool CameraFollow
    {
        get => _scene.Camera.Follow;
        set { _scene.Camera.Follow = value; OnPropertyChanged(); Changed(); }
    }

    public string? CameraFollowTargetId
    {
        get => _scene.Camera.FollowTargetId;
        set { _scene.Camera.FollowTargetId = value; OnPropertyChanged(); OnPropertyChanged(nameof(CameraFollowTarget)); Changed(); }
    }

    public ObjectVM? CameraFollowTarget
    {
        get => Objects.FirstOrDefault(o => o.Id == _scene.Camera.FollowTargetId);
        set { CameraFollowTargetId = value?.Id; }
    }

    public double CameraZoom
    {
        get => _scene.Camera.Zoom <= 0 ? 14 : _scene.Camera.Zoom;
        set { _scene.Camera.Zoom = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewZoom)); Changed(); }
    }

    public bool CanAttach(ObjectVM child, ObjectVM? parent)
    {
        if (child.IsLocked) return false;
        if (child.IsCanvas) return false;
        if (parent is not null && parent.IsLocked) return false;
        if (parent is null) return true;
        if (child == parent) return false;
        var cur = parent;
        int depth = 0;
        while (!string.IsNullOrEmpty(cur?.ParentId) && depth++ < 50)
        {
            if (cur.ParentId == child.Id) return false;
            cur = Objects.FirstOrDefault(o => o.Id == cur.ParentId);
        }
        return true;
    }

    bool _isSyncingParentOption;
    public ObservableCollection<ParentOption> CurrentParentOptions { get; } = [];

    public void RefreshParentOptions()
    {
        _isSyncingParentOption = true;
        try
        {
            CurrentParentOptions.Clear();
            CurrentParentOptions.Add(new(null, "— Корень сцены (Без родителя) —"));
            if (Selected is not null)
            {
                foreach (var obj in Objects)
                {
                    if (CanAttach(Selected, obj))
                    {
                        CurrentParentOptions.Add(new(obj.Id, $"{obj.HierarchyPrefix}{obj.Name}"));
                    }
                }
            }
            OnPropertyChanged(nameof(SelectedParentOption));
        }
        finally
        {
            _isSyncingParentOption = false;
        }
    }

    public ParentOption? SelectedParentOption
    {
        get
        {
            if (Selected is null) return null;
            if (string.IsNullOrEmpty(Selected.ParentId))
                return CurrentParentOptions.FirstOrDefault(p => p.Id == null);
            return CurrentParentOptions.FirstOrDefault(p => p.Id == Selected.ParentId);
        }
        set
        {
            if (_isSyncingParentOption || Selected is null || value is null) return;
            if (Selected.ParentId != value.Id)
            {
                AttachTo(Selected, Objects.FirstOrDefault(o => o.Id == value.Id));
            }
        }
    }

    public Vec3 GetWorldPosition(ObjectVM vm)
    {
        var chain = new List<ObjectVM>();
        var cur = vm;
        int depth = 0;
        while (cur is not null && depth++ < 30)
        {
            chain.Add(cur);
            if (string.IsNullOrEmpty(cur.ParentId)) break;
            var parent = Objects.FirstOrDefault(o => o.Id == cur.ParentId);
            if (parent is null || parent == cur || chain.Contains(parent)) break;
            cur = parent;
        }
        chain.Reverse();

        var worldPos = new Vec3(0, 0, 0);
        var accumRot = new Vec3(0, 0, 0);

        for (int i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            if (i == 0)
            {
                worldPos = new Vec3(node.PosX, node.PosY, node.PosZ);
                accumRot = new Vec3(node.RotX, node.RotY, node.RotZ);
            }
            else
            {
                var localOffset = new Vec3(node.PosX, node.PosY, node.PosZ);
                var rotatedOffset = RotateVec3(localOffset, accumRot);
                worldPos.X += rotatedOffset.X;
                worldPos.Y += rotatedOffset.Y;
                worldPos.Z += rotatedOffset.Z;
                accumRot.X = (accumRot.X + node.RotX) % 360;
                accumRot.Y = (accumRot.Y + node.RotY) % 360;
                accumRot.Z = (accumRot.Z + node.RotZ) % 360;
            }
        }
        return worldPos;
    }

    public static Vec3 RotateVec3(Vec3 pt, Vec3 rotDeg)
    {
        var radX = rotDeg.X * Math.PI / 180.0;
        var radY = rotDeg.Y * Math.PI / 180.0;
        var radZ = rotDeg.Z * Math.PI / 180.0;

        var y1 = pt.Y * Math.Cos(radX) - pt.Z * Math.Sin(radX);
        var z1 = pt.Y * Math.Sin(radX) + pt.Z * Math.Cos(radX);
        var x2 = pt.X * Math.Cos(radY) + z1 * Math.Sin(radY);
        var z2 = -pt.X * Math.Sin(radY) + z1 * Math.Cos(radY);
        var x3 = x2 * Math.Cos(radZ) - y1 * Math.Sin(radZ);
        var y3 = x2 * Math.Sin(radZ) + y1 * Math.Cos(radZ);

        return new Vec3(x3, y3, z2);
    }

    public IEnumerable<ObjectVM> AvailableParents => Selected is null ? [] : Objects.Where(o => o != Selected && CanAttach(Selected, o));

    public ObjectVM? SelectedParent
    {
        get => Objects.FirstOrDefault(o => o.Id == Selected?.ParentId);
        set
        {
            if (Selected is not null && Selected.ParentId != value?.Id)
            {
                AttachTo(Selected, value);
            }
        }
    }

    public void SyncSelectionVisuals()
    {
        foreach (var o in Objects)
        {
            o.IsSelected = SelectedObjects.Contains(o);
        }
    }

    public void RefreshHierarchyTree()
    {
        var childrenLookup = Objects
            .Where(o => !string.IsNullOrEmpty(o.ParentId))
            .ToLookup(o => o.ParentId!);

        var objectMap = Objects.ToDictionary(o => o.Id);

        foreach (var o in Objects)
        {
            o.HasChildren = childrenLookup.Contains(o.Id);
        }

        var newTree = new List<ObjectVM>();

        void AddBranch(ObjectVM item, int depth)
        {
            item.HierarchyDepth = depth;
            newTree.Add(item);

            if (item.IsExpanded && childrenLookup.Contains(item.Id))
            {
                foreach (var child in childrenLookup[item.Id])
                {
                    if (!newTree.Contains(child))
                    {
                        AddBranch(child, depth + 1);
                    }
                }
            }
        }

        var roots = Objects.Where(o => string.IsNullOrEmpty(o.ParentId) || !objectMap.ContainsKey(o.ParentId)).ToList();
        foreach (var r in roots)
        {
            AddBranch(r, 0);
        }

        if (newTree.Count < Objects.Count)
        {
            foreach (var o in Objects)
            {
                if (!newTree.Contains(o))
                {
                    bool isHiddenUnderCollapsed = false;
                    var cur = o;
                    int guard = 0;
                    while (!string.IsNullOrEmpty(cur?.ParentId) && guard++ < 30)
                    {
                        if (objectMap.TryGetValue(cur.ParentId, out var parent))
                        {
                            if (!parent.IsExpanded)
                            {
                                isHiddenUnderCollapsed = true;
                                break;
                            }
                            cur = parent;
                        }
                        else break;
                    }
                    if (!isHiddenUnderCollapsed)
                    {
                        AddBranch(o, 0);
                    }
                }
            }
        }

        if (!DisplayedHierarchyObjects.SequenceEqual(newTree))
        {
            DisplayedHierarchyObjects.Clear();
            foreach (var item in newTree)
            {
                DisplayedHierarchyObjects.Add(item);
            }
        }
    }

    public void AttachTo(ObjectVM child, ObjectVM? parent)
    {
        if (!CanAttach(child, parent))
        {
            Say("Нельзя привязать объект: циклическая зависимость или объект заблокирован.", ConsoleLevel.Warning);
            return;
        }

        var childWorldPos = GetWorldPosition(child);

        child.ParentId = parent?.Id;
        if (parent is not null)
        {
            parent.IsExpanded = true;
            var parentWorldPos = GetWorldPosition(parent);
            child.PosX = Math.Round(childWorldPos.X - parentWorldPos.X, 3);
            child.PosY = Math.Round(childWorldPos.Y - parentWorldPos.Y, 3);
            child.PosZ = Math.Round(childWorldPos.Z - parentWorldPos.Z, 3);
        }
        else
        {
            child.PosX = Math.Round(childWorldPos.X, 3);
            child.PosY = Math.Round(childWorldPos.Y, 3);
            child.PosZ = Math.Round(childWorldPos.Z, 3);
        }

        RefreshHierarchyTree();
        RefreshParentOptions();
        Changed();
        OnPropertyChanged(nameof(SelectedParent));
        OnPropertyChanged(nameof(AvailableParents));
        OnPropertyChanged(nameof(SelectedCanvasChildren));
        if (parent is not null)
            Say($"Объект «{child.Name}» теперь привязан к «{parent.Name}».", ConsoleLevel.Success);
        else
            Say($"Объект «{child.Name}» отвязан (в корне сцены).", ConsoleLevel.Info);
    }

    [RelayCommand]
    public void AttachSelectedTo(ObjectVM? parent)
    {
        if (Selected is null) return;
        AttachTo(Selected, parent);
    }

    [RelayCommand]
    public void UnparentSelected()
    {
        if (Selected is null) return;
        AttachTo(Selected, null);
    }

    [RelayCommand]
    public void SetCanvasPreset(string preset)
    {
        if (Selected?.IsCanvas == true)
        {
            Selected.UiDevicePreset = preset;
            Changed();
            Say($"Пресет устройства Canvas изменён на: {preset}", ConsoleLevel.Info);
        }
    }

    public string[] UiScaleModeList => UiScaleModes.All;

    public List<ObjectVM> SelectedCanvasChildren =>
        Selected?.IsCanvas == true
            ? Objects.Where(o => o.ParentId == Selected.Id).ToList()
            : [];

    [RelayCommand]
    public void AddTextToSelectedCanvas()
    {
        var targetCanvas = Selected?.IsCanvas == true ? Selected : GetOrCreateCanvas();
        var txt = new SceneObject
        {
            Type = ObjectTypes.UiText,
            Name = Unique("Text"),
            ParentId = targetCanvas.Id,
            UiText = "Новая надпись",
            UiFontSize = 24,
            Color = "#4A2929",
            Position = new(0, 0, 0),
            Scale = new(240, 48, 1)
        };
        Add(txt);
        OnPropertyChanged(nameof(SelectedCanvasChildren));
    }

    [RelayCommand]
    public void AddButtonToSelectedCanvas()
    {
        var targetCanvas = Selected?.IsCanvas == true ? Selected : GetOrCreateCanvas();
        var btn = new SceneObject
        {
            Type = ObjectTypes.UiButton,
            Name = Unique("Button"),
            ParentId = targetCanvas.Id,
            UiText = "Кнопка",
            UiFontSize = 16,
            Color = "#F7CDD5",
            UiAction = "jump",
            Position = new(0, -60, 0),
            Scale = new(180, 52, 1)
        };
        Add(btn);
        OnPropertyChanged(nameof(SelectedCanvasChildren));
    }

    [RelayCommand]
    public void AddImageToSelectedCanvas()
    {
        var targetCanvas = Selected?.IsCanvas == true ? Selected : GetOrCreateCanvas();
        var img = new SceneObject
        {
            Type = ObjectTypes.UiImage,
            Name = Unique("Image"),
            ParentId = targetCanvas.Id,
            Color = "#FFFFFF",
            Position = new(0, 60, 0),
            Scale = new(120, 120, 1)
        };
        Add(img);
        OnPropertyChanged(nameof(SelectedCanvasChildren));
    }

    public string Background
    {
        get => _scene.Background;
        set { if (System.Text.RegularExpressions.Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$")) { _scene.Background = value!; OnPropertyChanged(); Changed(); } }
    }

    public void Say(string text, ConsoleLevel level = ConsoleLevel.Info)
    {
        var entry = new ConsoleEntry { Time = DateTime.Now, Level = level, Message = text };
        ConsoleEntries.Add(entry);
        Log.Add(entry.DisplayText);

        if (level == ConsoleLevel.Error) ErrorCount++;
        else if (level == ConsoleLevel.Warning) WarningCount++;
        else InfoCount++;
    }

    public void Changed() { IsDirty = true; UpdateTitle(); RecordHistory(); SceneChanged?.Invoke(); }
    void UpdateTitle() => Title = $"{(IsDirty ? "● " : "")}{_project?.Name ?? "—"} · {Path.GetFileName(_scenePath)} — Moewshare Engine Editor";

    partial void OnSelectedChanged(ObjectVM? value)
    {
        if (value is null)
        {
            SelectedObjects.Clear();
            InspectorTabIndex = 1; // Автоматически показываем настройки сцены!
        }
        else
        {
            if (!SelectedObjects.Contains(value))
            {
                SelectedObjects.Clear();
                SelectedObjects.Add(value);
            }
            InspectorTabIndex = 0; // Показываем свойства объекта
        }
        SyncSelectionVisuals();
        OnPropertyChanged(nameof(SelectedParent));
        OnPropertyChanged(nameof(AvailableParents));
        RefreshParentOptions();
        OnPropertyChanged(nameof(SelectedCanvasChildren));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionCount));
        SceneChanged?.Invoke();
    }

    [RelayCommand]
    public void SelectObject(ObjectVM obj)
    {
        SelectedObjects.Clear();
        SelectedObjects.Add(obj);
        Selected = obj;
        SyncSelectionVisuals();
    }

    public void ToggleSelection(ObjectVM obj, bool multiSelect)
    {
        if (multiSelect)
        {
            if (SelectedObjects.Contains(obj))
            {
                SelectedObjects.Remove(obj);
                Selected = SelectedObjects.LastOrDefault();
            }
            else
            {
                SelectedObjects.Add(obj);
                Selected = obj;
            }
        }
        else
        {
            SelectedObjects.Clear();
            SelectedObjects.Add(obj);
            Selected = obj;
        }
        SyncSelectionVisuals();
        OnPropertyChanged(nameof(SelectedParent));
        OnPropertyChanged(nameof(AvailableParents));
        RefreshParentOptions();
        OnPropertyChanged(nameof(SelectedCanvasChildren));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionCount));
        SceneChanged?.Invoke();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        Selected = null;
        SelectedObjects.Clear();
        SyncSelectionVisuals();
        InspectorTabIndex = 1; // Клик по пустому месту переключает на Настройки сцены!
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionCount));
        SceneChanged?.Invoke();
    }

    public IReadOnlyList<ObjectVM> ObjectList => Objects;
    public double PreviewZoom => _scene.Camera.Zoom <= 0 ? 14 : _scene.Camera.Zoom;

    public async Task StartAsync(string? folder)
    {
        folder ??= await _ui.PickFolderAsync("Откройте папку проекта Moewshare");
        if (folder is null) { Say("Проект не выбран. Запустите редактор из лаунчера.", ConsoleLevel.Warning); return; }
        await LoadProjectAsync(folder);
    }

    async Task LoadProjectAsync(string folder)
    {
        var cfg = await EngineJson.TryReadAsync<ProjectConfig>(Path.Combine(folder, EngineInfo.ProjectFile));
        if (cfg is null) { await _ui.MessageAsync("Не удалось открыть проект", $"В папке «{folder}» нет корректного project.json."); return; }
        _folder = Path.GetFullPath(folder); _project = cfg;
        var path = ProjectValidator.ResolveInside(_folder, cfg.StartScene);
        if (path is null) { await _ui.MessageAsync("Не удалось открыть проект", "Стартовая сцена указывает за пределы проекта."); return; }
        if (!await LoadSceneAsync(path)) return;
        IsLoaded = true;
        RefreshAssets();
        Say($"Проект «{cfg.Name}» успешно открыт ({cfg.Kind}, шаблон {cfg.TemplateId}).", ConsoleLevel.Success);
    }

    async Task<bool> LoadSceneAsync(string path)
    {
        var scene = await EngineJson.TryReadAsync<SceneDocument>(path);
        if (scene is null) { await _ui.MessageAsync("Не удалось открыть сцену", $"Файл «{path}» не найден или повреждён."); return false; }
        _scene = scene; _scenePath = path;
        Objects.Clear();
        foreach (var o in scene.Objects) Objects.Add(CreateVM(o));
        Selected = Objects.FirstOrDefault();
        RefreshHierarchyTree();
        SyncSelectionVisuals();
        OnPropertyChanged(nameof(CameraMode));
        OnPropertyChanged(nameof(CameraPitch));
        OnPropertyChanged(nameof(CameraYaw));
        OnPropertyChanged(nameof(CameraOffsetY));
        OnPropertyChanged(nameof(CameraOffsetZ));
        OnPropertyChanged(nameof(CameraMovePlane));
        OnPropertyChanged(nameof(CameraZoom));
        OnPropertyChanged(nameof(CameraFollow));
        OnPropertyChanged(nameof(Background));
        IsDirty = false; UpdateTitle();
        ResetHistory();
        SceneRestored?.Invoke();
        SceneChanged?.Invoke();
        Say($"Сцена загружена: {Path.GetRelativePath(_folder, path)}", ConsoleLevel.Info);
        return true;
    }

    void OnObjectChanged() { foreach (var o in Objects) o.NameChangedRefresh(); Changed(); }

    SceneObject NewObject(string type, string name, string color) => new()
    {
        Type = type, Name = Unique(name), Color = color, Position = new(0, 1, 0),
        Scale = new(1, 1, 1)
    };

    public string Unique(string baseName)
    {
        var n = baseName; var i = 2;
        while (Objects.Any(o => o.Name == n)) n = $"{baseName} {i++}";
        return n;
    }

    ObjectVM CreateVM(SceneObject o)
    {
        var vm = new ObjectVM(o, OnObjectChanged);
        vm.SetActions(
            select: obj => SelectObject(obj),
            dup: obj => DuplicateSelected(),
            del: obj => { var t = DeleteSelected(); },
            openComp: OpenAddComponent,
            treeRefresh: RefreshHierarchyTree
        );
        return vm;
    }

    [RelayCommand]
    public async Task OpenAddObjectDialogAsync()
    {
        await _ui.ShowAddObjectDialogAsync(this);
    }

    public void Add(SceneObject o, bool asChild = false)
    {
        if (!IsLoaded) return;
        if (asChild && Selected is not null && !o.Type.StartsWith("ui_"))
        {
            o.ParentId = Selected.Id;
        }
        var vm = CreateVM(o);
        Objects.Add(vm);
        Selected = vm;
        RefreshHierarchyTree();
        SyncSelectionVisuals();
        Changed();
        Say($"Создан объект «{o.Name}».", ConsoleLevel.Success);
    }

    public void AddFromTemplate(string templateId, bool asChild = false)
    {
        switch (templateId.ToLowerInvariant())
        {
            case "box":
                Add(NewObject(ObjectTypes.Box, "Box", "#F7CDD5"), asChild);
                break;
            case "sphere":
                Add(NewObject(ObjectTypes.Sphere, "Sphere", "#E9A9B8"), asChild);
                break;
            case "cylinder":
                Add(NewObject(ObjectTypes.Cylinder, "Cylinder", "#AEC6CF"), asChild);
                break;
            case "plane":
                Add(new SceneObject { Type = ObjectTypes.Plane, Name = Unique("Plane"), Color = "#E8F8F5", Position = new(0, 0, 0), Scale = new(10, 1, 10) }, asChild);
                break;
            case "model":
                var mdl = new SceneObject
                {
                    Type = ObjectTypes.Model,
                    Name = Unique("3DModel"),
                    Color = "#DCDDE1",
                    Position = new(0, 0, 0),
                    Scale = new(1, 1, 1),
                    Solid = true
                };
                Add(mdl, asChild);
                Say("Создан объект 3D модели. Выберите файл .obj, .fbx, .stl или .glb в инспекторе или перетащите файл из ресурсов.", ConsoleLevel.Success);
                break;
            case "light":
                Add(NewObject(ObjectTypes.Light, "Light", "#FFFFFF"), asChild);
                break;
            case "sprite":
                var spr = new SceneObject
                {
                    Type = ObjectTypes.Sprite,
                    Name = Unique("Sprite"),
                    Color = "#FFFFFF",
                    Position = new(0, 1, 0),
                    Scale = new(2, 2, 0.05),
                    Solid = true,
                    Billboard = true
                };
                spr.Components.Add(ComponentTypes.SpriteRenderer);
                Add(spr, asChild);
                Say("Создан 2D спрайт. Выберите PNG в свойствах объекта.", ConsoleLevel.Success);
                break;
            case "platform":
                Add(new SceneObject
                {
                    Type = ObjectTypes.Box,
                    Name = Unique("Platform"),
                    Color = "#E9A9B8",
                    Position = new(0, 0, 0),
                    Scale = new(4, 0.6, 1),
                    Solid = true
                }, asChild);
                break;
            case "coin":
                var coin = new SceneObject
                {
                    Type = ObjectTypes.Sphere,
                    Name = Unique("Coin"),
                    Color = "#F2B84B",
                    Position = new(0, 1, 0),
                    Scale = new(0.6, 0.6, 0.6),
                    Behavior = Behaviors.Spin,
                    SpinSpeed = 90
                };
                coin.Components.Add(ComponentTypes.Spin);
                Add(coin, asChild);
                break;
            case "player_platformer":
                var pl = new SceneObject
                {
                    Type = ObjectTypes.Sprite,
                    Name = Unique("Player"),
                    Color = "#4A2929",
                    Position = new(0, 2, 0),
                    Scale = new(1, 1, 0.05),
                    Solid = false,
                    Billboard = true,
                    Behavior = Behaviors.Platformer,
                    Speed = 8,
                    JumpForce = 11,
                    Gravity = 25
                };
                pl.Components.Add(ComponentTypes.PlatformerController);
                pl.Components.Add(ComponentTypes.BoxCollider);
                Add(pl, asChild);
                Say("Создан персонаж Платформера (Platformer).", ConsoleLevel.Success);
                break;
            case "player_topdown":
                var td = new SceneObject
                {
                    Type = ObjectTypes.Sphere,
                    Name = Unique("PlayerTopDown"),
                    Color = "#4A2929",
                    Position = new(0, 0.5, 0),
                    Scale = new(0.9, 0.9, 0.9),
                    Solid = false,
                    Behavior = Behaviors.TopDown,
                    Speed = 7
                };
                td.Components.Add(ComponentTypes.TopDownController);
                td.Components.Add(ComponentTypes.BoxCollider);
                Add(td, asChild);
                Say("Создан персонаж Top-Down (ходит во все 4 стороны).", ConsoleLevel.Success);
                break;
            case "canvas":
                AddCanvas();
                break;
            case "ui_text":
                AddUiText();
                break;
            case "ui_image":
                AddUiImage();
                break;
            case "ui_button":
                AddUiButton();
                break;
        }
    }

    [RelayCommand] void AddBox() => Add(NewObject(ObjectTypes.Box, "Box", "#F7CDD5"));
    [RelayCommand] void AddSphere() => Add(NewObject(ObjectTypes.Sphere, "Sphere", "#E9A9B8"));
    [RelayCommand] void AddCylinder() => Add(NewObject(ObjectTypes.Cylinder, "Cylinder", "#AEC6CF"));
    [RelayCommand] void AddPlane() => Add(new SceneObject { Type = ObjectTypes.Plane, Name = Unique("Plane"), Color = "#E8F8F5", Position = new(0, 0, 0), Scale = new(10, 1, 10) });
    [RelayCommand] void AddLight() => Add(NewObject(ObjectTypes.Light, "Light", "#FFFFFF"));
    [RelayCommand] void AddSprite() => AddFromTemplate("sprite");

    [ObservableProperty] bool _isAddComponentOpen;
    [ObservableProperty] string _componentSearch = "";
    [ObservableProperty] string _selectedComponentCategory = "Все";

    public string[] ComponentCategories { get; } = ["Все", "Физика", "Геймплей", "Графика"];

    public ObservableCollection<ComponentItemVM> FilteredComponents { get; } = [];

    public void RefreshFilteredComponents()
    {
        FilteredComponents.Clear();
        var q = (ComponentSearch ?? "").Trim().ToLowerInvariant();
        var cat = SelectedComponentCategory ?? "Все";
        foreach (var c in ComponentCatalog.All)
        {
            if (cat != "Все" && c.Category != cat) continue;
            if (!string.IsNullOrEmpty(q) &&
                !c.Name.ToLowerInvariant().Contains(q) &&
                !c.Description.ToLowerInvariant().Contains(q) &&
                !c.Category.ToLowerInvariant().Contains(q)) continue;

            FilteredComponents.Add(new ComponentItemVM(c, AttachComponentToSelected));
        }
    }

    partial void OnComponentSearchChanged(string value) => RefreshFilteredComponents();
    partial void OnSelectedComponentCategoryChanged(string value) => RefreshFilteredComponents();

    [RelayCommand]
    public void OpenAddComponent()
    {
        if (Selected is null && Objects.Count > 0)
        {
            SelectObject(Objects[0]);
        }
        InspectorTabIndex = 0;
        ComponentSearch = "";
        SelectedComponentCategory = "Все";
        RefreshFilteredComponents();
        IsAddComponentOpen = true;
    }

    [RelayCommand]
    public void CloseAddComponent()
    {
        IsAddComponentOpen = false;
    }

    [RelayCommand]
    public void SelectComponentCategory(string cat)
    {
        SelectedComponentCategory = cat;
        RefreshFilteredComponents();
    }

    [RelayCommand]
    public void AttachComponentToSelected(string componentType)
    {
        if (Selected is null) return;
        Selected.AddComponent(componentType);
        IsAddComponentOpen = false;
        Changed();
        Say($"На объект «{Selected.Name}» добавлен компонент: {componentType}.", ConsoleLevel.Success);
    }

    [RelayCommand]
    public async Task ChooseSpriteForSelected()
    {
        if (Selected is null || !IsLoaded) return;
        var f = await _ui.PickFileAsync("Выбрать PNG спрайт", Path.Combine(_folder, "Assets"));
        if (f is null) return;
        try
        {
            var texturesDir = Path.Combine(_folder, "Assets", "Textures");
            Directory.CreateDirectory(texturesDir);
            var dest = Path.Combine(texturesDir, Path.GetFileName(f));
            if (!Path.GetFullPath(f).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(f, dest, true);
            }
            RefreshAssets();
            var rel = Path.GetRelativePath(_folder, dest).Replace('\\', '/');
            Selected.Texture = rel;
            Selected.Color = "#FFFFFF"; // Чистый белый, чтобы не искажать цвета PNG
            Changed();
            Say($"Спрайт для «{Selected.Name}» установлен: {rel}", ConsoleLevel.Success);
        }
        catch (Exception ex) { Say("Не удалось применить спрайт: " + ex.Message, ConsoleLevel.Error); }
    }

    public string[] DevicePresetList => DevicePresets.All;

    ObjectVM GetOrCreateCanvas()
    {
        var canvas = Objects.FirstOrDefault(o => o.IsCanvas);
        if (canvas is not null) return canvas;
        var canvasObj = new SceneObject
        {
            Type = ObjectTypes.Canvas,
            Name = Unique("Canvas"),
            UiDevicePreset = DevicePresets.Desktop,
            Scale = new(1920, 1080, 1)
        };
        var vm = CreateVM(canvasObj);
        Objects.Add(vm);
        return vm;
    }

    [RelayCommand]
    public void AddCanvas()
    {
        var c = GetOrCreateCanvas();
        Selected = c;
        Changed();
        Say("Создан интерфейс Canvas. Добавляйте в него UI-текст, картинки и кнопки.", ConsoleLevel.Success);
    }

    [RelayCommand]
    public void AddUiText()
    {
        var canvas = GetOrCreateCanvas();
        var txt = new SceneObject
        {
            Type = ObjectTypes.UiText,
            Name = Unique("Text"),
            ParentId = canvas.Id,
            UiText = "Moewshare Engine",
            UiFontSize = 24,
            Color = "#4A2929",
            Position = new(0, 120, 0),
            Scale = new(300, 50, 1)
        };
        Add(txt);
        Say("Создан UI-текст внутри Canvas.", ConsoleLevel.Success);
    }

    [RelayCommand]
    public void AddUiImage()
    {
        var canvas = GetOrCreateCanvas();
        var img = new SceneObject
        {
            Type = ObjectTypes.UiImage,
            Name = Unique("Image"),
            ParentId = canvas.Id,
            Color = "#FFFFFF",
            Position = new(0, 0, 0),
            Scale = new(100, 100, 1)
        };
        Add(img);
        Say("Создано UI-изображение внутри Canvas.", ConsoleLevel.Success);
    }

    [RelayCommand]
    public void AddUiButton()
    {
        var canvas = GetOrCreateCanvas();
        var btn = new SceneObject
        {
            Type = ObjectTypes.UiButton,
            Name = Unique("Button"),
            ParentId = canvas.Id,
            UiText = "Прыжок (Jump)",
            UiFontSize = 16,
            Color = "#F7CDD5",
            UiAction = "jump",
            Position = new(0, -120, 0),
            Scale = new(180, 52, 1)
        };
        Add(btn);
        Say("Создана UI-кнопка внутри Canvas.", ConsoleLevel.Success);
    }

    [RelayCommand]
    public void DuplicateSelected()
    {
        if (Selected is null || !IsLoaded) return;
        var toDup = SelectedObjects.Count > 0 ? SelectedObjects.ToList() : [Selected];
        var created = new List<ObjectVM>();

        foreach (var orig in toDup)
        {
            var m = orig.Model;
            // Полная копия объекта (компоненты, UI-настройки, граф логики), но с новым Id и именем.
            var clone = CloneSceneObject(m);
            clone.Name = Unique(m.Name);
            if (!clone.Type.StartsWith("ui_") && clone.Type != ObjectTypes.Canvas)
                clone.Position = new(m.Position.X + 1, m.Position.Y, m.Position.Z + 1);
            else
                clone.Position = new(m.Position.X + 20, m.Position.Y - 20, m.Position.Z);
            var vm = CreateVM(clone);
            Objects.Add(vm);
            created.Add(vm);
        }

        SelectedObjects.Clear();
        foreach (var c in created) SelectedObjects.Add(c);
        Selected = created.LastOrDefault();
        RefreshHierarchyTree();
        SyncSelectionVisuals();
        Changed();
        Say(toDup.Count == 1 ? $"Объект продублирован: «{created[0].Name}»." : $"Дублировано объектов: {created.Count}.", ConsoleLevel.Success);
    }

    [RelayCommand]
    public async Task DeleteSelected()
    {
        if (Selected is null && SelectedObjects.Count == 0) return;
        var toDel = SelectedObjects.Count > 1 ? SelectedObjects.ToList() : [Selected!];
        // Подтверждение не требуется: удаление можно отменить через Ctrl+Z / ⌘Z.
        await Task.CompletedTask;

        foreach (var obj in toDel)
        {
            foreach (var c in Objects.Where(o => o.ParentId == obj.Id)) c.ParentId = null;
            Objects.Remove(obj);
        }

        Selected = Objects.FirstOrDefault();
        SelectedObjects.Clear();
        if (Selected is not null) SelectedObjects.Add(Selected);
        else InspectorTabIndex = 1;

        RefreshHierarchyTree();
        SyncSelectionVisuals();
        Changed();
        Say(toDel.Count == 1 ? $"Объект «{toDel[0].Name}» удалён." : $"Удалено объектов: {toDel.Count}.", ConsoleLevel.Info);
        StatusText = $"Удалено: {toDel.Count} · {Hk.Label(Hk.Undo)} — вернуть";
    }

    [RelayCommand]
    public async Task Save()
    {
        if (!IsLoaded) return;
        try
        {
            _scene.Objects = Objects.Select(o => o.Model).ToList();
            await EngineJson.SaveAtomicAsync(_scenePath, _scene);
            IsDirty = false; UpdateTitle(); Say("Сцена успешно сохранена.", ConsoleLevel.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Say("Ошибка сохранения: " + ex.Message, ConsoleLevel.Error); await _ui.MessageAsync("Не удалось сохранить сцену", ex.Message); }
    }

    [RelayCommand]
    async Task OpenScene()
    {
        if (!IsLoaded) return;
        if (IsDirty && !await _ui.ConfirmAsync("Несохранённые изменения", "Открыть другую сцену без сохранения текущей?", "Открыть")) return;
        var dir = Path.Combine(_folder, "Scenes"); Directory.CreateDirectory(dir);
        var f = await _ui.PickFileAsync("Открыть сцену", dir);
        if (f is null) return;
        var rel = Path.GetRelativePath(_folder, f);
        if (rel.StartsWith("..") || Path.IsPathRooted(rel))
        { await _ui.MessageAsync("Открыть сцену", "Сцена должна находиться внутри папки проекта."); return; }
        await LoadSceneAsync(f);
    }

    [RelayCommand]
    async Task SetStartScene()
    {
        if (_project is null) return;
        _project.StartScene = Path.GetRelativePath(_folder, _scenePath).Replace('\\', '/');
        await EngineJson.SaveAtomicAsync(Path.Combine(_folder, EngineInfo.ProjectFile), _project);
        Say($"Стартовая сцена установлена: {_project.StartScene}", ConsoleLevel.Success);
    }

    [RelayCommand]
    public async Task Run()
    {
        if (!IsLoaded || IsRunning) return;
        if (IsDirty) { Say("Сцена изменена — сохраняю перед запуском..."); await Save(); }
        if (_project is not null && ProjectValidator.ResolveInside(_folder, _project.StartScene) != _scenePath)
            Say($"Внимание: запускается стартовая сцена «{_project.StartScene}», а не открытая. Используйте «Сделать стартовой».", ConsoleLevel.Warning);
        Say("Проверка проекта и запуск локального веб-сервера...", ConsoleLevel.Info);
        var r = await _session.StartAsync(_folder);
        if (!r.Ok) { Say("Ошибка запуска: " + r.Error, ConsoleLevel.Error); await _ui.MessageAsync("Не удалось запустить игру", r.Error!); return; }
        IsRunning = true;
        Say($"Игра запущена на {r.Value}. Откройте в браузере или используйте предпросмотр.", ConsoleLevel.Success);
        if (_session.Warning is not null) Say(_session.Warning, ConsoleLevel.Warning);
    }

    [RelayCommand]
    public void Stop()
    {
        if (!IsRunning) return;
        _session.Stop(); IsRunning = false; Say("Локальный сервер игры остановлен.", ConsoleLevel.Info);
    }

    [RelayCommand]
    public async Task Build()
    {
        if (!IsLoaded) return;
        if (IsDirty) await Save();
        Say("Сборка Web-версии проекта...", ConsoleLevel.Info);
        var r = await WebExporter.ExportAsync(_folder);
        if (!r.Ok) { Say("Ошибка сборки: " + r.Error, ConsoleLevel.Error); await _ui.MessageAsync("Ошибка сборки", r.Error!); return; }
        Say($"Сборка игры готова в папке: {r.Value}", ConsoleLevel.Success);
        TryOpenFolder(r.Value!);
    }

    [RelayCommand]
    public async Task ImportAsset()
    {
        if (!IsLoaded) return;
        var startFolder = Directory.Exists(CurrentAssetDirectory) ? CurrentAssetDirectory : Path.Combine(_folder, "Assets");
        var f = await _ui.PickFileAsync("Импорт ресурса (3D модели, текстуры, аудио)", startFolder);
        if (f is null) return;
        try
        {
            var assetsDir = Path.Combine(_folder, "Assets");
            var ext = Path.GetExtension(f).ToLowerInvariant();

            // If user is currently in a subfolder, put it directly there; otherwise categorize by extension
            var targetFolder = CurrentAssetDirectory;
            if (string.IsNullOrEmpty(targetFolder) || Path.GetFullPath(targetFolder).Equals(Path.GetFullPath(assetsDir), StringComparison.OrdinalIgnoreCase))
            {
                if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg") targetFolder = Path.Combine(assetsDir, "Textures");
                else if (ext is ".obj" or ".fbx" or ".stl" or ".glb" or ".gltf") targetFolder = Path.Combine(assetsDir, "Models");
                else if (ext is ".mp3" or ".wav" or ".ogg") targetFolder = Path.Combine(assetsDir, "Audio");
                else targetFolder = assetsDir;
            }

            Directory.CreateDirectory(targetFolder);
            var dest = Path.Combine(targetFolder, Path.GetFileName(f));
            if (File.Exists(dest) && !await _ui.ConfirmAsync("Файл уже существует", $"Заменить «{Path.GetFileName(f)}»?", "Заменить")) return;
            await Task.Run(() => File.Copy(f, dest, true));
            RefreshAssets();
            Say($"Ресурс успешно импортирован: {Path.GetRelativePath(_folder, dest)}", ConsoleLevel.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Say("Ошибка импорта: " + ex.Message, ConsoleLevel.Error); }
    }

    [RelayCommand]
    public async Task CreateFolder()
    {
        if (!IsLoaded) return;
        var dirName = await _ui.PromptAsync("Создать новую папку", "Введите название новой папки:", "Новая папка");
        if (string.IsNullOrWhiteSpace(dirName)) return;

        try
        {
            var target = Path.Combine(CurrentAssetDirectory, dirName.Trim());
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
                RefreshAssets();
                Say($"Папка «{dirName}» успешно создана.", ConsoleLevel.Success);
            }
            else
            {
                Say($"Папка «{dirName}» уже существует.", ConsoleLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            Say($"Ошибка создания папки: {ex.Message}", ConsoleLevel.Error);
        }
    }

    [RelayCommand]
    public async Task DeleteAsset(AssetItemVM? item)
    {
        if (!IsLoaded || item is null) return;
        var isDir = item.IsDirectory;
        var confirmed = await _ui.ConfirmAsync("Удаление ресурса", $"Удалить {(isDir ? "папку со всем содержимым" : "файл")} «{item.Name}»?", "Удалить");
        if (!confirmed) return;

        try
        {
            if (isDir)
            {
                if (Directory.Exists(item.FullPath)) Directory.Delete(item.FullPath, true);
            }
            else
            {
                if (File.Exists(item.FullPath)) File.Delete(item.FullPath);
            }
            RefreshAssets();
            Say($"«{item.Name}» удалён.", ConsoleLevel.Info);
        }
        catch (Exception ex)
        {
            Say($"Не удалось удалить: {ex.Message}", ConsoleLevel.Error);
        }
    }

    [RelayCommand]
    public void NavigateIntoFolder(AssetItemVM? item)
    {
        if (item is null || !item.IsDirectory) return;
        CurrentAssetDirectory = item.FullPath;
        RefreshAssets();
    }

    [RelayCommand]
    public void NavigateUp()
    {
        if (!CanNavigateUp) return;
        var parent = Directory.GetParent(CurrentAssetDirectory);
        if (parent is not null)
        {
            CurrentAssetDirectory = parent.FullName;
            RefreshAssets();
        }
    }

    public void MoveAssetToFolder(AssetItemVM source, AssetItemVM targetFolder)
    {
        if (source is null || targetFolder is null || !targetFolder.IsDirectory) return;
        if (source.FullPath.Equals(targetFolder.FullPath, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            var dest = Path.Combine(targetFolder.FullPath, source.Name);
            if (source.IsDirectory)
            {
                if (dest.StartsWith(source.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    Say("Нельзя переместить папку саму в себя.", ConsoleLevel.Warning);
                    return;
                }
                Directory.Move(source.FullPath, dest);
            }
            else
            {
                File.Move(source.FullPath, dest, overwrite: true);
            }
            RefreshAssets();
            Say($"«{source.Name}» перемещён в «{targetFolder.Name}».", ConsoleLevel.Success);
        }
        catch (Exception ex)
        {
            Say($"Ошибка перемещения файла: {ex.Message}", ConsoleLevel.Error);
        }
    }

    [RelayCommand]
    public void PutAssetOnScene(AssetItemVM? item)
    {
        if (item is null) return;
        SpawnAssetOnScene(item, null);
    }

    public void SpawnAssetOnScene(AssetItemVM item, Vec3? spawnPos)
    {
        if (!IsLoaded || item is null) return;
        var name = Path.GetFileNameWithoutExtension(item.Name);
        var pos = spawnPos ?? new Vec3(0, 0, 0);

        if (item.IsModel)
        {
            var obj = new SceneObject
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = string.IsNullOrWhiteSpace(name) ? "Model" : name,
                Type = ObjectTypes.Model,
                ModelPath = item.RelativePath,
                Position = pos,
                Rotation = new Vec3(0, 0, 0),
                Scale = new Vec3(1, 1, 1),
                Color = "#DCDDE1",
                Solid = true
            };
            Add(obj);
            Say($"3D модель «{obj.Name}» добавлена на сцену в ({pos.X}, {pos.Y}, {pos.Z})!", ConsoleLevel.Success);
        }
        else if (item.IsTexture)
        {
            var obj = new SceneObject
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = string.IsNullOrWhiteSpace(name) ? "Sprite" : name,
                Type = ObjectTypes.Sprite,
                Texture = item.RelativePath,
                Position = pos,
                Rotation = new Vec3(0, 0, 0),
                Scale = new Vec3(1, 1, 1),
                Color = "#FFFFFF",
                Solid = false
            };
            Add(obj);
            Say($"2D спрайт «{obj.Name}» добавлен на сцену в ({pos.X}, {pos.Y}, {pos.Z})!", ConsoleLevel.Success);
        }
    }

    [RelayCommand]
    public async Task ChooseModelForSelected()
    {
        if (Selected is null || !IsLoaded) return;
        var modelsDir = Path.Combine(_folder, "Assets", "Models");
        Directory.CreateDirectory(modelsDir);
        var f = await _ui.PickFileAsync("Выбрать файл 3D модели (OBJ, FBX, STL, GLB, GLTF)", modelsDir);
        if (f is null) return;
        try
        {
            var dest = Path.Combine(modelsDir, Path.GetFileName(f));
            if (!Path.GetFullPath(f).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(f, dest, true);
            }
            RefreshAssets();
            var rel = Path.GetRelativePath(_folder, dest).Replace('\\', '/');
            Selected.ModelPath = rel;
            Say($"3D модель для «{Selected.Name}» установлена: {rel}", ConsoleLevel.Success);
        }
        catch (Exception ex) { Say("Не удалось применить 3D модель: " + ex.Message, ConsoleLevel.Error); }
    }

    [RelayCommand]
    public void ClearModelForSelected()
    {
        if (Selected is null) return;
        Selected.ModelPath = null;
        Say($"3D модель с «{Selected.Name}» удалена.");
    }

    [RelayCommand]
    public async Task ChooseTextureForSelected()
    {
        if (Selected is null || !IsLoaded) return;
        var f = await _ui.PickFileAsync("Выбрать изображение текстуры", Path.Combine(_folder, "Assets"));
        if (f is null) return;
        try
        {
            var texturesDir = Path.Combine(_folder, "Assets", "Textures");
            Directory.CreateDirectory(texturesDir);
            var dest = Path.Combine(texturesDir, Path.GetFileName(f));
            if (!Path.GetFullPath(f).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(f, dest, true);
            }
            RefreshAssets();
            var rel = Path.GetRelativePath(_folder, dest).Replace('\\', '/');
            Selected.Texture = rel;
            Say($"Текстура для «{Selected.Name}» установлена: {rel}", ConsoleLevel.Success);
        }
        catch (Exception ex) { Say("Не удалось применить текстуру: " + ex.Message, ConsoleLevel.Error); }
    }

    [RelayCommand]
    public void ClearTextureForSelected()
    {
        if (Selected is null) return;
        Selected.Texture = null;
        Say($"Текстура с «{Selected.Name}» удалена.");
    }

    [RelayCommand]
    public void ToggleEyeDropper()
    {
        IsEyeDropperActive = !IsEyeDropperActive;
        Say(IsEyeDropperActive
            ? "Режим пипетки активен: кликните по объекту на сцене для взятия его цвета."
            : "Режим пипетки отключен.");
    }

    [RelayCommand] public void ToggleBottomPanel() => IsBottomExpanded = !IsBottomExpanded;
    [RelayCommand] public void ToggleColorPalette() => IsColorPaletteOpen = !IsColorPaletteOpen;

    [RelayCommand]
    public void RotateSelectedX(string degrees)
    {
        if (Selected is not null && double.TryParse(degrees, out var d)) Selected.RotateBy(d, 0, 0);
    }

    [RelayCommand]
    public void RotateSelectedY(string degrees)
    {
        if (Selected is not null && double.TryParse(degrees, out var d)) Selected.RotateBy(0, d, 0);
    }

    [RelayCommand]
    public void RotateSelectedZ(string degrees)
    {
        if (Selected is not null && double.TryParse(degrees, out var d)) Selected.RotateBy(0, 0, d);
    }

    [RelayCommand]
    public void ResetSelectedRotation() => Selected?.ResetRotation();

    [RelayCommand]
    public void SetColorPreset(string hex)
    {
        if (Selected is null) return;
        Selected.Color = hex;
        Say($"Цвет для «{Selected.Name}» изменён на {hex}.");
    }

    [RelayCommand]
    public void RefreshAssets()
    {
        Assets.Clear();
        CurrentFolderAssets.Clear();
        AvailableTextures.Clear();
        AvailableTextures.Add("(Без текстуры)");
        AvailableModels.Clear();
        AvailableModels.Add("(Без 3D модели)");

        var assetsRoot = Path.Combine(_folder, "Assets");
        if (!Directory.Exists(assetsRoot))
        {
            Directory.CreateDirectory(assetsRoot);
        }

        // 1. Scan all files for textures & models collections
        foreach (var f in Directory.EnumerateFiles(assetsRoot, "*", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var rel = Path.GetRelativePath(_folder, f).Replace('\\', '/');
            Assets.Add(rel);
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg")
            {
                AvailableTextures.Add(rel);
            }
            else if (ext is ".obj" or ".fbx" or ".stl" or ".glb" or ".gltf")
            {
                AvailableModels.Add(rel);
            }
        }

        // 2. Scan current folder for folder navigation and file listing
        var curDir = CurrentAssetDirectory;
        if (!Directory.Exists(curDir))
        {
            curDir = assetsRoot;
            CurrentAssetDirectory = curDir;
        }

        try
        {
            // First directories
            foreach (var d in Directory.EnumerateDirectories(curDir).OrderBy(x => Path.GetFileName(x)))
            {
                var dirName = Path.GetFileName(d);
                if (dirName.StartsWith(".")) continue;
                var rel = Path.GetRelativePath(_folder, d).Replace('\\', '/');
                int fileCount = 0;
                try { fileCount = Directory.EnumerateFileSystemEntries(d).Count(); } catch { }
                CurrentFolderAssets.Add(new AssetItemVM
                {
                    Editor = this,
                    Name = dirName,
                    FullPath = d,
                    RelativePath = rel,
                    IsDirectory = true,
                    Kind = AssetKind.Folder,
                    Extension = "",
                    Icon = "📁",
                    DisplaySize = $"{fileCount} объектов"
                });
            }

            // Next files
            foreach (var f in Directory.EnumerateFiles(curDir).OrderBy(x => Path.GetFileName(x)))
            {
                var fileName = Path.GetFileName(f);
                if (fileName.StartsWith(".")) continue;
                var rel = Path.GetRelativePath(_folder, f).Replace('\\', '/');
                var ext = Path.GetExtension(f).ToLowerInvariant();
                var (kind, icon) = ext switch
                {
                    ".obj" or ".fbx" or ".stl" or ".glb" or ".gltf" => (AssetKind.Model3D, "🧊"),
                    ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg" => (AssetKind.Texture, "🖼️"),
                    ".mp3" or ".wav" or ".ogg" => (AssetKind.Audio, "🎵"),
                    ".json" when fileName.EndsWith(".scene.json") => (AssetKind.Scene, "🎬"),
                    _ => (AssetKind.Other, "📄")
                };

                string sizeStr = "";
                try
                {
                    var len = new FileInfo(f).Length;
                    sizeStr = len < 1024 ? $"{len} Б" : len < 1024 * 1024 ? $"{len / 1024.0:F1} КБ" : $"{len / (1024.0 * 1024.0):F1} МБ";
                }
                catch { }

                CurrentFolderAssets.Add(new AssetItemVM
                {
                    Editor = this,
                    Name = fileName,
                    FullPath = f,
                    RelativePath = rel,
                    IsDirectory = false,
                    Kind = kind,
                    Extension = ext,
                    Icon = icon,
                    DisplaySize = sizeStr
                });
            }
        }
        catch { }

        OnPropertyChanged(nameof(CurrentAssetRelativePath));
        OnPropertyChanged(nameof(CanNavigateUp));
    }

    [RelayCommand] public void OpenProjectFolder() { if (IsLoaded) TryOpenFolder(_folder); }
    [RelayCommand] public void OpenAssetsFolder() { if (IsLoaded) TryOpenFolder(Path.Combine(_folder, "Assets")); }

    [RelayCommand] public void ClearConsole()
    {
        ConsoleEntries.Clear();
        Log.Clear();
        ErrorCount = 0;
        WarningCount = 0;
        InfoCount = 0;
    }

    [RelayCommand] public void SetGizmoMode(string mode)
    {
        CurrentGizmoMode = mode.ToLowerInvariant() switch
        {
            "select" or "q" => GizmoMode.Select,
            "rotate" or "e" => GizmoMode.Rotate,
            "scale" or "r" => GizmoMode.Scale,
            _ => GizmoMode.Translate
        };
        StatusText = $"Режим: {GizmoModeTitle}";
    }

    [RelayCommand] public void FocusOnSelected()
    {
        RequestFocus?.Invoke();
    }

    void TryOpenFolder(string path)
    {
        try
        {
            var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo("explorer.exe") : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open") : new ProcessStartInfo("xdg-open");
            psi.UseShellExecute = false; psi.ArgumentList.Add(path); Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) { Say("Не удалось открыть папку: " + ex.Message, ConsoleLevel.Warning); }
    }

    [RelayCommand]
    async Task DeleteCurrentProject()
    {
        if (string.IsNullOrWhiteSpace(_folder) || !Directory.Exists(_folder))
        {
            await _ui.MessageAsync("Удаление проекта", "Папка текущего проекта не найдена.");
            BackToLauncher();
            return;
        }

        var projectName = _project?.Name ?? Path.GetFileName(_folder);
        if (!await _ui.ConfirmAsync("Удаление проекта",
                $"Вы уверены, что хотите удалить проект «{projectName}»?\nПапка «{_folder}» и все её файлы будут безвозвратно удалены с диска.",
                "Удалить проект"))
        {
            return;
        }

        try
        {
            Stop();
            _session.Dispose();
            Directory.Delete(_folder, true);
        }
        catch (Exception ex)
        {
            await _ui.MessageAsync("Ошибка удаления", "Не удалось полностью удалить папку: " + ex.Message);
        }

        BackToLauncher();
    }

    [RelayCommand]
    void BackToLauncher()
    {
        try
        {
            if (OperatingSystem.IsWindows() && Process.GetProcessesByName("Moewshare.Launcher").Length == 0)
            {
                var exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Moewshare.Launcher.exe"));
                if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })?.Dispose();
            }
            else if (OperatingSystem.IsMacOS() && Process.GetProcessesByName("Moewshare.Launcher").Length == 0)
            {
                var app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Moewshare.Launcher.app"));
                if (Directory.Exists(app))
                {
                    Process.Start("open", ["-n", app])?.Dispose();
                }
                else
                {
                    Process.Start("open", ["-a", "Moewshare.Launcher"])?.Dispose();
                }
            }
        }
        catch (Exception ex) { Say("Не удалось запустить лаунчер: " + ex.Message); }
        _ui.Close();
    }

    public void Dispose()
    {
        McpServer.Dispose();
        _session.Dispose();
    }
}

public sealed record ParentOption(string? Id, string Title)
{
    public override string ToString() => Title;
}

public sealed partial class ComponentItemVM(ComponentDescriptor descriptor, Action<string> onAdd) : ObservableObject
{
    public string Type => descriptor.Type;
    public string Name => descriptor.Name;
    public string Category => descriptor.Category;
    public string Icon => descriptor.Icon;
    public string Description => descriptor.Description;

    [RelayCommand]
    public void AddThis() => onAdd(Type);
}
