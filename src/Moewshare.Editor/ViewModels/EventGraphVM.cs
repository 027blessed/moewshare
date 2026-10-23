using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Engine;
using Moewshare.Engine.Events;

namespace Moewshare.Editor.ViewModels;

public sealed partial class EventGraphVM : ObservableObject
{
    public EditorViewModel Editor { get; }

    [ObservableProperty] EventNode? _selectedNode;
    [ObservableProperty] GraphVariable? _selectedVariable;
    [ObservableProperty] string _newVarName = "Coins";
    [ObservableProperty] PortType _newVarType = PortType.Integer;
    [ObservableProperty] string _newVarDefault = "0";
    [ObservableProperty] string _nodeSearchText = "";
    [ObservableProperty] bool _isPaletteOpen;
    [ObservableProperty] double _zoomLevel = 1.0;
    [ObservableProperty] bool _isObjectGraph;
    [ObservableProperty] ObjectVM? _targetObject;
    [ObservableProperty] int _leftSidebarTabIndex = 1;

    partial void OnSelectedNodeChanged(EventNode? value)
    {
        if (!_syncingSelection)
        {
            _selection.Clear();
            if (value is not null) _selection.Add(value);
            SelectedGroup = null;
            RaiseSelectionChanged();
        }
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(IsUiSetTextSelected));
        OnPropertyChanged(nameof(IsButtonEventSelected));
        OnPropertyChanged(nameof(IsObjectEventSelected));
        OnPropertyChanged(nameof(IsClickEventSelected));
        OnPropertyChanged(nameof(IsVariableNodeSelected));
        OnPropertyChanged(nameof(SelectedNodeTargetName));
        OnPropertyChanged(nameof(SelectedNodePrefix));
        OnPropertyChanged(nameof(SelectedNodeSuffix));
        OnPropertyChanged(nameof(SelectedNodeButtonName));
        OnPropertyChanged(nameof(SelectedNodeObjectName));
        OnPropertyChanged(nameof(SelectedNodeVariableName));
        OnPropertyChanged(nameof(SelectedNodePreviewText));
        OnPropertyChanged(nameof(AvailableUiTexts));
        OnPropertyChanged(nameof(AvailableUiButtons));
        OnPropertyChanged(nameof(AvailableSceneObjects));
        OnPropertyChanged(nameof(AvailableVariableNames));
    }

    public bool HasSelectedNode => SelectedNode != null;
    public bool IsUiSetTextSelected => SelectedNode?.NodeType == "ui_set_text";
    public bool IsButtonEventSelected => SelectedNode?.NodeType == "event_ui_button_clicked";
    public bool IsObjectEventSelected => SelectedNode?.NodeType == "event_object_clicked";
    public bool IsClickEventSelected => IsButtonEventSelected || IsObjectEventSelected;
    public bool IsVariableNodeSelected => SelectedNode?.NodeType is "var_get" or "var_set" or "var_increment" or "var_decrement";

    public ObservableCollection<string> AvailableUiTexts
    {
        get
        {
            var list = new ObservableCollection<string>();
            foreach (var o in Editor.CurrentScene.Objects.Where(o => o.Type == ObjectTypes.UiText))
            {
                list.Add(o.Name);
            }
            if (list.Count == 0) list.Add("Text");
            return list;
        }
    }

    public ObservableCollection<string> AvailableUiButtons
    {
        get
        {
            var list = new ObservableCollection<string>();
            foreach (var o in Editor.CurrentScene.Objects.Where(o => o.Type == ObjectTypes.UiButton))
            {
                list.Add(o.Name);
            }
            return list;
        }
    }

    public ObservableCollection<string> AvailableSceneObjects
    {
        get
        {
            var list = new ObservableCollection<string>();
            foreach (var o in Editor.CurrentScene.Objects)
            {
                list.Add(o.Name);
            }
            return list;
        }
    }

    public ObservableCollection<GraphVariable> SharedSceneVariables { get; } = [];
    public bool HasSharedSceneVariables => SharedSceneVariables.Count > 0;

    public ObservableCollection<string> AvailableVariableNames
    {
        get
        {
            var list = new ObservableCollection<string>();
            // 1. Current graph variables
            foreach (var v in CurrentGraph.Variables)
            {
                if (!list.Contains(v.Name)) list.Add(v.Name);
            }
            // 2. Global scene variables
            if (Editor.CurrentScene.EventGraph?.Variables is not null)
            {
                foreach (var v in Editor.CurrentScene.EventGraph.Variables)
                {
                    if (!list.Contains(v.Name)) list.Add(v.Name);
                }
            }
            // 3. Variables from other scene objects
            foreach (var o in Editor.CurrentScene.Objects)
            {
                if (o.EventGraph?.Variables is not null)
                {
                    foreach (var v in o.EventGraph.Variables)
                    {
                        if (!list.Contains(v.Name)) list.Add(v.Name);
                    }
                }
            }
            return list;
        }
    }

    public string SelectedNodeTargetName
    {
        get => SelectedNode?.Properties.TryGetValue("TargetName", out var tn) == true ? tn : "Text";
        set
        {
            if (SelectedNode is not null)
            {
                SelectedNode.Properties["TargetName"] = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedNodePreviewText));
                Editor.MarkDirty();
                NotifyGraphChanged();
            }
        }
    }

    public string SelectedNodePrefix
    {
        get => SelectedNode?.Properties.TryGetValue("Prefix", out var pfx) == true ? pfx : "";
        set
        {
            if (SelectedNode is not null)
            {
                SelectedNode.Properties["Prefix"] = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedNodePreviewText));
                Editor.MarkDirty();
                NotifyGraphChanged();
            }
        }
    }

    public string SelectedNodeSuffix
    {
        get => SelectedNode?.Properties.TryGetValue("Suffix", out var sfx) == true ? sfx : "";
        set
        {
            if (SelectedNode is not null)
            {
                SelectedNode.Properties["Suffix"] = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedNodePreviewText));
                Editor.MarkDirty();
                NotifyGraphChanged();
            }
        }
    }

    public string SelectedNodePreviewText => $"Предпросмотр: «{SelectedNodePrefix}100{SelectedNodeSuffix}»";

    public string SelectedNodeButtonName
    {
        get => SelectedNode?.Properties.TryGetValue("ButtonName", out var bn) == true ? bn : "";
        set
        {
            if (SelectedNode is not null)
            {
                SelectedNode.Properties["ButtonName"] = value;
                OnPropertyChanged();
                Editor.MarkDirty();
                NotifyGraphChanged();
            }
        }
    }

    public string SelectedNodeObjectName
    {
        get => SelectedNode?.Properties.TryGetValue("ObjectName", out var on) == true ? on : "";
        set
        {
            if (SelectedNode is not null)
            {
                SelectedNode.Properties["ObjectName"] = value;
                OnPropertyChanged();
                Editor.MarkDirty();
                NotifyGraphChanged();
            }
        }
    }

    public string SelectedNodeVariableName
    {
        get => SelectedNode?.Properties.TryGetValue("VariableName", out var vn) == true ? vn : "";
        set
        {
            if (SelectedNode is not null)
            {
                SelectedNode.Properties["VariableName"] = value;
                if (SelectedNode.NodeType == "var_get") SelectedNode.Title = $"🔢 Get {value}";
                else if (SelectedNode.NodeType == "var_set") SelectedNode.Title = $"✏️ Set {value}";
                OnPropertyChanged();
                Editor.MarkDirty();
                NotifyGraphChanged();
            }
        }
    }

    public ObservableCollection<GraphVariable> Variables { get; } = [];
    public ObservableCollection<string> SimulationLogs { get; } = [];
    public ObservableCollection<NodeDescriptor> FilteredLibraryNodes { get; } = [];

    public event Action? GraphChanged;

    public string CurrentTargetTitle => IsObjectGraph && TargetObject != null
        ? $"📦 Объект «{TargetObject.Name}»"
        : "🌐 Глобальные события сцены";

    public EventGraph CurrentGraph
    {
        get
        {
            if (IsObjectGraph && TargetObject is not null)
            {
                return TargetObject.Model.EventGraph ??= new EventGraph
                {
                    Name = $"{TargetObject.Name}_Logic"
                };
            }
            return Editor.CurrentScene.EventGraph ??= new EventGraph { Name = "Scene_Global_Logic" };
        }
    }

    [RelayCommand]
    public void SelectGlobalGraph()
    {
        IsObjectGraph = false;
        TargetObject = null;
        RefreshVariables();
        OnPropertyChanged(nameof(CurrentTargetTitle));
        OnPropertyChanged(nameof(CurrentGraph));
        SelectedNode = null;
        NotifyGraphChanged();
    }

    [RelayCommand]
    public void SelectObjectGraph(ObjectVM? obj)
    {
        obj ??= Editor.Selected;
        if (obj is null)
        {
            SelectGlobalGraph();
            return;
        }

        IsObjectGraph = true;
        TargetObject = obj;
        RefreshVariables();
        OnPropertyChanged(nameof(CurrentTargetTitle));
        OnPropertyChanged(nameof(CurrentGraph));
        SelectedNode = null;
        NotifyGraphChanged();
    }

    public EventGraphVM(EditorViewModel editor)
    {
        Editor = editor;
        Editor.SceneRestored += OnSceneRestored;
        RefreshVariables();
        FilterLibrary();
    }

    /// <summary>После Undo/Redo или открытия другой сцены — перепривязываемся к новым объектам.</summary>
    void OnSceneRestored()
    {
        var targetId = TargetObject?.Id;
        var selectedIds = _selection.Select(n => n.Id).ToList();
        if (IsObjectGraph)
        {
            var obj = targetId is null ? null : Editor.Objects.FirstOrDefault(o => o.Id == targetId);
            if (obj is null) { IsObjectGraph = false; TargetObject = null; }
            else TargetObject = obj;
        }
        _syncingSelection = true;
        _selection.Clear();
        foreach (var id in selectedIds)
            if (CurrentGraph.FindNode(id) is { } n) _selection.Add(n);
        SelectedNode = _selection.LastOrDefault();
        _syncingSelection = false;
        SelectedGroup = null;
        RefreshVariables();
        OnPropertyChanged(nameof(CurrentTargetTitle));
        OnPropertyChanged(nameof(CurrentGraph));
        RaiseSelectionChanged();
        NotifyGraphChanged();
    }

    public void RefreshVariables()
    {
        foreach (var v in Variables)
        {
            v.PropertyChanged -= OnVariablePropertyChanged;
        }
        Variables.Clear();
        foreach (var v in CurrentGraph.Variables)
        {
            v.PropertyChanged += OnVariablePropertyChanged;
            Variables.Add(v);
        }

        SharedSceneVariables.Clear();
        if (IsObjectGraph)
        {
            // 1. Global scene variables
            if (Editor.CurrentScene.EventGraph?.Variables is not null)
            {
                foreach (var v in Editor.CurrentScene.EventGraph.Variables)
                {
                    if (CurrentGraph.Variables.All(cv => !cv.Name.Equals(v.Name, StringComparison.OrdinalIgnoreCase)) &&
                        SharedSceneVariables.All(sv => !sv.Name.Equals(v.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        SharedSceneVariables.Add(v);
                    }
                }
            }
            // 2. Variables declared on other objects in this scene
            foreach (var o in Editor.CurrentScene.Objects)
            {
                if (o.Id == TargetObject?.Id || o.EventGraph?.Variables is null) continue;
                foreach (var v in o.EventGraph.Variables)
                {
                    if (CurrentGraph.Variables.All(cv => !cv.Name.Equals(v.Name, StringComparison.OrdinalIgnoreCase)) &&
                        SharedSceneVariables.All(sv => !sv.Name.Equals(v.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        SharedSceneVariables.Add(v);
                    }
                }
            }
        }
        OnPropertyChanged(nameof(HasSharedSceneVariables));
    }

    private void OnVariablePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    partial void OnNodeSearchTextChanged(string value) => FilterLibrary();

    void FilterLibrary()
    {
        FilteredLibraryNodes.Clear();
        var q = NodeSearchText.Trim();
        foreach (var desc in EventNodeLibrary.All)
        {
            if (string.IsNullOrEmpty(q) ||
                desc.Type.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                desc.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                desc.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                desc.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                MatchesSearchAlias(desc.Type, q))
            {
                FilteredLibraryNodes.Add(desc);
            }
        }
    }

    private static bool MatchesSearchAlias(string type, string query)
    {
        var q = query.ToLowerInvariant();
        return type switch
        {
            "math_add" => q is "add" or "+" or "plus" or "плюс" or "сложить" or "сложение" or "сумма" or "sum",
            "math_subtract" => q is "sub" or "subtract" or "-" or "minus" or "минус" or "вычесть" or "вычитание" or "разность",
            "math_multiply" => q is "mul" or "multiply" or "*" or "умножить" or "умножение" or "произведение",
            "math_divide" => q is "div" or "divide" or "/" or "деление" or "разделить",
            "var_increment" => q is "inc" or "increment" or "++" or "прибавить" or "+1",
            "var_decrement" => q is "dec" or "decrement" or "--" or "отнять" or "-1",
            "flow_branch" => q is "if" or "else" or "branch" or "ветвление" or "если" or "условие",
            "event_ui_button_clicked" => q is "click" or "button" or "btn" or "кнопка" or "клик" or "тап" or "кликер",
            "event_object_clicked" => q is "click" or "object" or "клик" or "объект" or "спрайт" or "тап",
            "event_timer" => q is "timer" or "таймер" or "тик" or "время" or "автоклик",
            "ui_set_text" => q is "text" or "текст" or "ui" or "надпись" or "счет" or "счёт",
            _ => false
        };
    }

    [RelayCommand]
    public void AddVariable()
    {
        var name = string.IsNullOrWhiteSpace(NewVarName) ? "NewVar" : NewVarName.Trim();
        int counter = 1;
        var baseName = name;
        while (CurrentGraph.Variables.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName}_{counter++}";
        }

        var newVar = new GraphVariable(name, NewVarType, NewVarDefault);
        newVar.PropertyChanged += OnVariablePropertyChanged;
        CurrentGraph.Variables.Add(newVar);
        Variables.Add(newVar);
        SelectedVariable = newVar;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    [RelayCommand]
    public void RemoveVariable(GraphVariable? variable)
    {
        var v = variable ?? SelectedVariable;
        if (v is null) return;
        v.PropertyChanged -= OnVariablePropertyChanged;
        CurrentGraph.Variables.Remove(v);
        Variables.Remove(v);
        if (SelectedVariable == v) SelectedVariable = null;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    [RelayCommand]
    public void SpawnGetVariable(GraphVariable? v)
    {
        v ??= SelectedVariable;
        if (v is null) return;

        var node = EventNodeLibrary.CreateNode("var_get");
        node.Title = $"🔢 Get {v.Name}";
        node.Properties["VariableName"] = v.Name;
        (node.PosX, node.PosY) = NextSpawnPosition();
        CurrentGraph.AddNode(node);
        SelectedNode = node;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    [RelayCommand]
    public void SpawnSetVariable(GraphVariable? v)
    {
        v ??= SelectedVariable;
        if (v is null) return;

        var node = EventNodeLibrary.CreateNode("var_set");
        node.Title = $"✏️ Set {v.Name}";
        node.Properties["VariableName"] = v.Name;
        (node.PosX, node.PosY) = NextSpawnPosition();
        CurrentGraph.AddNode(node);
        SelectedNode = node;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    [RelayCommand]
    public void AddNode(NodeDescriptor desc)
    {
        var node = desc.Factory();
        (node.PosX, node.PosY) = NextSpawnPosition();
        if (IsObjectGraph && TargetObject != null)
        {
            if (node.NodeType == "event_ui_button_clicked")
            {
                node.Properties["ButtonName"] = TargetObject.Name;
                node.Title = $"👆 On Click ({TargetObject.Name})";
            }
            else if (node.NodeType == "event_object_clicked")
            {
                node.Properties["ObjectName"] = TargetObject.Name;
                node.Title = $"🖱️ On Click ({TargetObject.Name})";
            }
        }
        CurrentGraph.AddNode(node);
        SelectedNode = node;
        LeftSidebarTabIndex = 0;
        IsPaletteOpen = false;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    public void AddNodeAt(string nodeType, double x, double y)
    {
        var node = EventNodeLibrary.CreateNode(nodeType);
        node.PosX = x;
        node.PosY = y;
        if (IsObjectGraph && TargetObject != null)
        {
            if (node.NodeType == "event_ui_button_clicked")
            {
                node.Properties["ButtonName"] = TargetObject.Name;
                node.Title = $"👆 On Click ({TargetObject.Name})";
            }
            else if (node.NodeType == "event_object_clicked")
            {
                node.Properties["ObjectName"] = TargetObject.Name;
                node.Title = $"🖱️ On Click ({TargetObject.Name})";
            }
        }
        CurrentGraph.AddNode(node);
        SelectedNode = node;
        LeftSidebarTabIndex = 0;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    /// <summary>Удаляет все выделенные блоки (Delete / ⌫).</summary>
    [RelayCommand]
    public void DeleteSelectedNode() => DeleteSelectedNodes();

    [RelayCommand]
    public void ClearGraph()
    {
        CurrentGraph.Nodes.Clear();
        CurrentGraph.Connections.Clear();
        CurrentGraph.Groups.Clear();
        SelectedGroup = null;
        SelectedNode = null;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    [RelayCommand]
    public void LoadPresetClicker()
    {
        Editor.CurrentScene.EventGraph = EventGraph.CreateClickerPreset();
        RefreshVariables();
        SelectedNode = null;
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.Say("Загружен шаблон логики: Кликер (Тапалка + Автоклик)", ConsoleLevel.Info);
    }

    [RelayCommand]
    public void LoadPresetCoinCollector()
    {
        Editor.CurrentScene.EventGraph = EventGraph.CreateCoinCollectorPreset();
        RefreshVariables();
        SelectedNode = null;
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.Say("Загружен шаблон логики: Сбор монет (10 монет + UI)", ConsoleLevel.Info);
    }

    [RelayCommand]
    public void LoadPresetDoorKey()
    {
        Editor.CurrentScene.EventGraph = EventGraph.CreateDoorKeyPreset();
        RefreshVariables();
        SelectedNode = null;
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.Say("Загружен шаблон логики: Дверь и ключ", ConsoleLevel.Info);
    }

    [RelayCommand]
    public void LoadPresetHealth()
    {
        Editor.CurrentScene.EventGraph = EventGraph.CreatePlayerHealthPreset();
        RefreshVariables();
        SelectedNode = null;
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.Say("Загружен шаблон логики: Здоровье игрока", ConsoleLevel.Info);
    }

    [RelayCommand]
    public void Simulate()
    {
        SimulationLogs.Clear();
        SimulationLogs.Add("--- Запуск тестовой симуляции графа событий ---");

        var objects = Editor.Objects.Select(o => o.Model).ToList();
        var runtime = new EventRuntime(CurrentGraph, objects);

        SimulationLogs.Add("1. Срабатывание Begin Play...");
        runtime.TriggerBeginPlay();

        SimulationLogs.Add("2. Имитация касания монеты (On Trigger Enter)...");
        var playerObj = objects.FirstOrDefault(o => o.Name.Contains("Player", StringComparison.OrdinalIgnoreCase))
                     ?? new Engine.SceneObject { Name = "Player" };
        var coinObj = objects.FirstOrDefault(o => o.Name.Contains("Coin", StringComparison.OrdinalIgnoreCase))
                   ?? new Engine.SceneObject { Name = "Coin 1", IsTrigger = true };

        runtime.TriggerEnter(coinObj, playerObj);

        SimulationLogs.Add("3. Срабатывание Update (dt = 0.016)...");
        runtime.TriggerUpdate(0.016);

        foreach (var log in runtime.Context.ExecutionLog)
        {
            SimulationLogs.Add(log);
        }

        SimulationLogs.Add($"Итог: переменных в памяти: {runtime.Context.Variables.Count}, уничтожено объектов: {runtime.Context.DestroyedObjects.Count}");
        if (runtime.Context.IsWon) SimulationLogs.Add("🏆 Условие победы (Game Win) выполнено!");
        if (runtime.Context.IsLost) SimulationLogs.Add("💀 Условие поражения (Game Over) выполнено!");

        Editor.Say($"Симуляция EventGraph завершена ({runtime.Context.ExecutionLog.Count} шагов)", ConsoleLevel.Info);
    }

    public void NotifyGraphChanged()
    {
        GraphChanged?.Invoke();
    }
}
