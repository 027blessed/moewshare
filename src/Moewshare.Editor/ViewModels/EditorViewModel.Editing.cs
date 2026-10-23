using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Editor.Services;
using Moewshare.Engine;

namespace Moewshare.Editor.ViewModels;

/// <summary>
/// Редактирование сцены: история изменений (Undo/Redo), буфер обмена объектов,
/// групповые операции и управление вкладками по горячим клавишам.
/// </summary>
public sealed partial class EditorViewModel
{
    /// <summary>Порядок панелей для Ctrl+1…7 / ⌘1…7.</summary>
    static readonly List<string> PanelHotkeyOrder = ["viewport", "eventgraph", "hierarchy", "inspector", "assets", "console", "game"];

    static readonly JsonSerializerOptions CompactJson = new(EngineJson.Options) { WriteIndented = false };

    /// <summary>Сцена была полностью перезагружена (Undo/Redo, открытие сцены) — ссылки на старые объекты недействительны.</summary>
    public event Action? SceneRestored;

    /// <summary>Короткое сообщение в строке состояния внизу окна.</summary>
    [ObservableProperty] string _statusText = "Готово";

    // ================== ИСТОРИЯ ИЗМЕНЕНИЙ ==================
    const int HistoryLimit = 100;
    static readonly TimeSpan HistoryBurst = TimeSpan.FromMilliseconds(650);
    readonly List<string> _undo = [];
    readonly List<string> _redo = [];
    string? _historyLatest;
    DateTime _lastChangeAt = DateTime.MinValue;
    bool _restoringHistory;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    string SerializeScene()
    {
        _scene.Objects = Objects.Select(o => o.Model).ToList();
        return JsonSerializer.Serialize(_scene, CompactJson);
    }

    void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _historyLatest = SerializeScene();
        _lastChangeAt = DateTime.MinValue;
        NotifyHistory();
    }

    /// <summary>
    /// Запоминает состояние сцены. Серия быстрых правок (перетаскивание, ввод текста, слайдер)
    /// объединяется в один шаг истории.
    /// </summary>
    void RecordHistory()
    {
        if (_restoringHistory || !IsLoaded) return;
        var current = SerializeScene();
        if (_historyLatest is null) { _historyLatest = current; return; }
        if (current == _historyLatest) return;

        var now = DateTime.UtcNow;
        if (now - _lastChangeAt > HistoryBurst)
        {
            _undo.Add(_historyLatest);
            if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
            _redo.Clear();
        }
        _historyLatest = current;
        _lastChangeAt = now;
        NotifyHistory();
    }

    /// <summary>Отметить изменение, сделанное не через ObjectVM (граф событий, MCP и т.п.).</summary>
    public void MarkDirty()
    {
        IsDirty = true;
        UpdateTitle();
        RecordHistory();
    }

    [RelayCommand]
    public void Undo()
    {
        if (!IsLoaded) return;
        var current = SerializeScene();
        if (_historyLatest is not null && current != _historyLatest) _undo.Add(_historyLatest);
        if (_undo.Count == 0) { StatusText = "Нечего отменять"; return; }

        var target = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        RestoreSnapshot(target);
        StatusText = $"↶ Отменено · {Hk.Label(Hk.Redo)} — повторить";
    }

    [RelayCommand]
    public void Redo()
    {
        if (!IsLoaded || _redo.Count == 0) { StatusText = "Нечего повторять"; return; }
        var current = SerializeScene();
        var target = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        RestoreSnapshot(target);
        StatusText = "↷ Повторено";
    }

    void RestoreSnapshot(string json)
    {
        SceneDocument? doc;
        try { doc = JsonSerializer.Deserialize<SceneDocument>(json, CompactJson); }
        catch (JsonException) { doc = null; }
        if (doc is null) return;

        _restoringHistory = true;
        try
        {
            var selectedIds = SelectedObjects.Select(o => o.Id).ToHashSet();
            var primaryId = Selected?.Id;

            _scene = doc;
            Objects.Clear();
            foreach (var o in doc.Objects) Objects.Add(CreateVM(o));

            SelectedObjects.Clear();
            foreach (var o in Objects.Where(o => selectedIds.Contains(o.Id))) SelectedObjects.Add(o);
            Selected = Objects.FirstOrDefault(o => o.Id == primaryId) ?? SelectedObjects.LastOrDefault();

            RefreshHierarchyTree();
            SyncSelectionVisuals();
            NotifySceneSettings();

            IsDirty = true;
            UpdateTitle();
            SceneRestored?.Invoke();
            SceneChanged?.Invoke();
        }
        finally
        {
            _restoringHistory = false;
        }
        _historyLatest = SerializeScene();
        _lastChangeAt = DateTime.MinValue;
        NotifyHistory();
    }

    void NotifySceneSettings()
    {
        OnPropertyChanged(nameof(CameraMode));
        OnPropertyChanged(nameof(CameraPitch));
        OnPropertyChanged(nameof(CameraYaw));
        OnPropertyChanged(nameof(CameraOffsetY));
        OnPropertyChanged(nameof(CameraOffsetZ));
        OnPropertyChanged(nameof(CameraMovePlane));
        OnPropertyChanged(nameof(CameraZoom));
        OnPropertyChanged(nameof(CameraFollow));
        OnPropertyChanged(nameof(Background));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionCount));
    }

    // ================== КОПИРОВАНИЕ / ВСТАВКА ОБЪЕКТОВ ==================
    string? _objectClipboard;
    public bool HasObjectClipboard => _objectClipboard is not null;

    List<ObjectVM> CurrentSelection() =>
        SelectedObjects.Count > 0 ? SelectedObjects.ToList() : Selected is not null ? [Selected] : [];

    static SceneObject CloneSceneObject(SceneObject source)
    {
        var json = JsonSerializer.Serialize(source, CompactJson);
        var copy = JsonSerializer.Deserialize<SceneObject>(json, CompactJson) ?? new SceneObject();
        copy.Id = Guid.NewGuid().ToString("N")[..8];
        return copy;
    }

    [RelayCommand]
    public void CopySelected()
    {
        var sel = CurrentSelection();
        if (sel.Count == 0) { StatusText = "Нечего копировать — выберите объект"; return; }
        _objectClipboard = JsonSerializer.Serialize(sel.Select(o => o.Model).ToList(), CompactJson);
        OnPropertyChanged(nameof(HasObjectClipboard));
        StatusText = sel.Count == 1 ? $"📋 Скопирован «{sel[0].Name}»" : $"📋 Скопировано объектов: {sel.Count}";
    }

    [RelayCommand]
    public async Task CutSelected()
    {
        var sel = CurrentSelection();
        if (sel.Count == 0) return;
        CopySelected();
        await DeleteSelected();
        StatusText = $"✂️ Вырезано объектов: {sel.Count}";
    }

    [RelayCommand]
    public void PasteObjects()
    {
        if (!IsLoaded || _objectClipboard is null) { StatusText = "Буфер обмена пуст"; return; }
        List<SceneObject>? items;
        try { items = JsonSerializer.Deserialize<List<SceneObject>>(_objectClipboard, CompactJson); }
        catch (JsonException) { items = null; }
        if (items is null || items.Count == 0) return;

        // Новые Id; связи родитель→ребёнок внутри вставляемой группы сохраняются.
        var idMap = new Dictionary<string, string>();
        foreach (var o in items)
        {
            var newId = Guid.NewGuid().ToString("N")[..8];
            idMap[o.Id] = newId;
            o.Id = newId;
        }

        var created = new List<ObjectVM>();
        foreach (var o in items)
        {
            if (o.ParentId is not null)
            {
                if (idMap.TryGetValue(o.ParentId, out var mapped)) o.ParentId = mapped;
                else if (Objects.All(x => x.Id != o.ParentId)) o.ParentId = null;
            }
            o.Name = Unique(o.Name);
            if (o.Type.StartsWith("ui_")) o.Position = new(o.Position.X + 20, o.Position.Y - 20, o.Position.Z);
            else o.Position = new(o.Position.X + 1, o.Position.Y, o.Position.Z + 1);
            var vm = CreateVM(o);
            Objects.Add(vm);
            created.Add(vm);
        }

        SelectedObjects.Clear();
        foreach (var c in created) SelectedObjects.Add(c);
        Selected = created.LastOrDefault();
        RefreshHierarchyTree();
        SyncSelectionVisuals();
        Changed();
        StatusText = $"📌 Вставлено объектов: {created.Count}";
    }

    // ================== ВЫДЕЛЕНИЕ ==================
    [RelayCommand]
    public void SelectAll()
    {
        if (Objects.Count == 0) return;
        SelectedObjects.Clear();
        foreach (var o in Objects) SelectedObjects.Add(o);
        Selected = Objects.Last();
        SyncSelectionVisuals();
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(SelectionCount));
        SceneChanged?.Invoke();
        StatusText = $"Выделено объектов: {Objects.Count}";
    }

    [RelayCommand]
    public async Task RenameSelected()
    {
        if (Selected is null) return;
        var name = await _ui.PromptAsync("Переименовать объект", "Новое имя объекта:", Selected.Name);
        if (!string.IsNullOrWhiteSpace(name)) Selected.Name = name;
    }

    [RelayCommand]
    public void ToggleSelectedVisibility()
    {
        var sel = CurrentSelection();
        if (sel.Count == 0) return;
        var show = !sel[0].IsVisible;
        foreach (var o in sel) o.IsVisible = show;
        StatusText = show ? "👁 Объекты показаны" : "🚫 Объекты скрыты";
    }

    // ================== ПАНЕЛИ ==================
    /// <summary>Открыть панель по номеру (Ctrl+1…7 / ⌘1…7).</summary>
    public void ActivatePanelByIndex(int index)
    {
        if (index < 0 || index >= PanelHotkeyOrder.Count) return;
        var panel = AllPanels.FirstOrDefault(p => p.Id == PanelHotkeyOrder[index]);
        if (panel is null) return;
        if (panel.Zone == DockZone.Bottom) IsBottomExpanded = true;
        panel.Activate();
    }

    public EventGraphVM? GraphVM => (AllPanels.FirstOrDefault(p => p.Id == "eventgraph") as EventGraphPanelVM)?.GraphVM;

    static void SetActive(EditorPanelVM? oldValue, EditorPanelVM? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null) newValue.IsActive = true;
    }

    partial void OnActiveLeftPanelChanged(EditorPanelVM? oldValue, EditorPanelVM? newValue) => SetActive(oldValue, newValue);
    partial void OnActiveRightPanelChanged(EditorPanelVM? oldValue, EditorPanelVM? newValue) => SetActive(oldValue, newValue);
    partial void OnActiveBottomPanelChanged(EditorPanelVM? oldValue, EditorPanelVM? newValue) => SetActive(oldValue, newValue);
    partial void OnActiveCenterPanelChanged(EditorPanelVM? oldValue, EditorPanelVM? newValue) => SetActive(oldValue, newValue);

    partial void OnCurrentGizmoModeChanged(GizmoMode value)
    {
        OnPropertyChanged(nameof(IsModeSelect));
        OnPropertyChanged(nameof(IsModeTranslate));
        OnPropertyChanged(nameof(IsModeRotate));
        OnPropertyChanged(nameof(IsModeScale));
        OnPropertyChanged(nameof(GizmoModeTitle));
        SceneChanged?.Invoke();
    }

    public bool IsModeSelect => CurrentGizmoMode == GizmoMode.Select;
    public bool IsModeTranslate => CurrentGizmoMode == GizmoMode.Translate;
    public bool IsModeRotate => CurrentGizmoMode == GizmoMode.Rotate;
    public bool IsModeScale => CurrentGizmoMode == GizmoMode.Scale;

    public string GizmoModeTitle => CurrentGizmoMode switch
    {
        GizmoMode.Translate => "↔ Перемещение (W)",
        GizmoMode.Rotate => "🔄 Вращение (E)",
        GizmoMode.Scale => "⤢ Масштаб (R)",
        _ => "✋ Выбор (Q)"
    };

    // Подсказки с горячими клавишами для кнопок интерфейса (с учётом ОС)
    public string TipSave => Hk.Tip("Сохранить сцену", Hk.Save);
    public string TipUndo => Hk.Tip("Отменить", Hk.Undo);
    public string TipRedo => Hk.Tip("Повторить", Hk.Redo);
    public string TipAddObject => Hk.Tip("Добавить объект на сцену", Hk.AddObject);
    public string TipRun => $"Запустить игру в браузере  ({Hk.Label(Hk.Run)} или {Hk.Label(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.P, Hk.Mod))})";
    public string TipStop => Hk.Tip("Остановить игру", Hk.Stop);
    public string TipBuild => Hk.Tip("Собрать игру для Web", Hk.Build);
    public string TipSettings => Hk.Tip("Настройки движка", Hk.Settings);
    public string TipHelp => Hk.Tip("Горячие клавиши", Hk.Help);
    public string TipTheme => Hk.Tip("Переключить светлую / тёмную тему", Hk.ToggleTheme);
    public string TipEvents => Hk.Tip("Редактор событий (Event Graph)", Hk.Panel2);
    public string TipSelect => "Выбор  (Q)";
    public string TipMove => "Перемещение  (W)";
    public string TipRotate => "Вращение  (E)";
    public string TipScale => "Масштабирование  (R)";
    public string TipFocus => "Фокус на объекте  (F)";
    public string TipDuplicate => Hk.Tip("Дублировать", Hk.Duplicate);
    public string TipDelete => Hk.Tip("Удалить", Hk.Delete);
    public string TipToggleBottom => Hk.Tip("Свернуть / развернуть нижнюю панель", Hk.ToggleBottom);
}
