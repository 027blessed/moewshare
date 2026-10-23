using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Editor.Services;
using Moewshare.Engine.Events;

namespace Moewshare.Editor.ViewModels;

/// <summary>
/// Редактор событий: множественное выделение, группы блоков, копирование/вставка,
/// дублирование и массовое удаление.
/// </summary>
public sealed partial class EventGraphVM
{
    readonly List<EventNode> _selection = [];
    bool _syncingSelection;

    /// <summary>Буфер обмена блоков — общий для всех графов (можно копировать между объектами).</summary>
    static string? s_clipboard;

    /// <summary>Выделенная группа (для переименования / смены цвета).</summary>
    [ObservableProperty] EventGroup? _selectedGroup;

    /// <summary>Возвращает центр видимой области полотна в координатах графа (задаёт полотно).</summary>
    public Func<(double X, double Y)>? ViewCenterProvider { get; set; }

    int _spawnCascade;

    public IReadOnlyList<EventNode> SelectedNodes => _selection;
    public int SelectionCount => _selection.Count;
    public bool HasMultipleSelection => _selection.Count > 1;
    public bool HasAnySelection => _selection.Count > 0;
    public bool HasSingleSelection => _selection.Count == 1;
    public bool HasSelectedGroup => SelectedGroup is not null;
    public bool CanPaste => s_clipboard is not null;
    public bool IsNodeSelected(EventNode node) => _selection.Contains(node);
    public string SelectionSummary => _selection.Count switch
    {
        0 => "Ничего не выделено",
        1 => $"Выделен 1 блок",
        _ => $"Выделено блоков: {_selection.Count}"
    };

    /// <summary>Готовые цвета для рамок групп.</summary>
    public string[] GroupColors { get; } =
        ["#E05263", "#F59E0B", "#10B981", "#38BDF8", "#6366F1", "#A855F7", "#EC4899", "#64748B"];

    partial void OnSelectedGroupChanged(EventGroup? value)
    {
        OnPropertyChanged(nameof(HasSelectedGroup));
        OnPropertyChanged(nameof(SelectedGroupTitle));
    }

    public string SelectedGroupTitle
    {
        get => SelectedGroup?.Title ?? "";
        set
        {
            if (SelectedGroup is null || SelectedGroup.Title == value) return;
            SelectedGroup.Title = value;
            OnPropertyChanged();
            Editor.MarkDirty();
            NotifyGraphChanged();
        }
    }

    void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedNodes));
        OnPropertyChanged(nameof(SelectionCount));
        OnPropertyChanged(nameof(HasMultipleSelection));
        OnPropertyChanged(nameof(HasAnySelection));
        OnPropertyChanged(nameof(HasSingleSelection));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CanPaste));
    }

    /// <summary>Устанавливает выделение целиком; последний блок становится «основным» (его свойства показываются в инспекторе).</summary>
    public void SetSelection(IEnumerable<EventNode> nodes, EventNode? primary = null)
    {
        _syncingSelection = true;
        _selection.Clear();
        foreach (var n in nodes.Distinct()) _selection.Add(n);
        if (primary is not null && !_selection.Contains(primary)) _selection.Add(primary);
        SelectedNode = primary ?? _selection.LastOrDefault();
        _syncingSelection = false;
        RaiseSelectionChanged();
        NotifyGraphChanged();
    }

    public void SelectOnly(EventNode node)
    {
        SelectedGroup = null;
        SetSelection([node], node);
    }

    /// <summary>Shift / Ctrl / ⌘ + клик — добавить или убрать блок из выделения.</summary>
    public void ToggleNodeSelection(EventNode node)
    {
        var list = _selection.ToList();
        if (!list.Remove(node)) list.Add(node);
        SetSelection(list, list.Contains(node) ? node : list.LastOrDefault());
    }

    public void AddToSelection(IEnumerable<EventNode> nodes)
    {
        var list = _selection.ToList();
        list.AddRange(nodes.Where(n => !list.Contains(n)));
        SetSelection(list);
    }

    [RelayCommand]
    public void ClearSelection()
    {
        SelectedGroup = null;
        SetSelection([]);
    }

    [RelayCommand]
    public void SelectAllNodes()
    {
        SetSelection(CurrentGraph.Nodes);
        Editor.StatusText = $"Выделено блоков: {_selection.Count}";
    }

    /// <summary>Выделить все блоки группы (клик по заголовку рамки).</summary>
    public void SelectGroup(EventGroup group, bool additive = false)
    {
        var nodes = group.NodeIds.Select(CurrentGraph.FindNode).OfType<EventNode>().ToList();
        if (additive) AddToSelection(nodes);
        else SetSelection(nodes);
        SelectedGroup = group;
        LeftSidebarTabIndex = 0;
    }

    // ================== УДАЛЕНИЕ ==================
    public void DeleteSelectedNodes()
    {
        if (_selection.Count == 0)
        {
            if (SelectedGroup is not null) { UngroupSelection(); }
            return;
        }
        var count = CurrentGraph.RemoveNodes(_selection.Select(n => n.Id));
        SelectedGroup = null;
        SetSelection([]);
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.StatusText = count == 1
            ? $"Блок удалён · {Hk.Label(Hk.Undo)} — вернуть"
            : $"Удалено блоков: {count} · {Hk.Label(Hk.Undo)} — вернуть";
    }

    // ================== КОПИРОВАНИЕ / ВСТАВКА ==================
    /// <summary>Фрагмент графа для буфера обмена: блоки + связи между ними + группы.</summary>
    sealed class GraphFragment
    {
        public List<EventNode> Nodes { get; set; } = [];
        public List<EventConnection> Connections { get; set; } = [];
        public List<EventGroup> Groups { get; set; } = [];
    }

    string? SerializeSelection()
    {
        if (_selection.Count == 0) return null;
        var ids = _selection.Select(n => n.Id).ToHashSet();
        var fragment = new GraphFragment
        {
            Nodes = _selection.ToList(),
            Connections = CurrentGraph.Connections.Where(c => ids.Contains(c.FromNodeId) && ids.Contains(c.ToNodeId)).ToList(),
            Groups = CurrentGraph.Groups.Where(g => g.NodeIds.Count > 0 && g.NodeIds.All(ids.Contains)).ToList()
        };
        return JsonSerializer.Serialize(fragment);
    }

    [RelayCommand]
    public void CopyNodes()
    {
        var json = SerializeSelection();
        if (json is null) { Editor.StatusText = "Выделите блоки, чтобы скопировать"; return; }
        s_clipboard = json;
        OnPropertyChanged(nameof(CanPaste));
        Editor.StatusText = $"📋 Скопировано блоков: {_selection.Count}";
    }

    [RelayCommand]
    public void CutNodes()
    {
        if (_selection.Count == 0) return;
        CopyNodes();
        DeleteSelectedNodes();
    }

    /// <summary>Вставка блоков. Если задана точка — фрагмент ставится туда (левым верхним углом), иначе со сдвигом.</summary>
    public void PasteNodes(double? atX = null, double? atY = null)
    {
        if (s_clipboard is null) { Editor.StatusText = "Буфер обмена пуст"; return; }
        GraphFragment? fragment;
        try { fragment = JsonSerializer.Deserialize<GraphFragment>(s_clipboard); }
        catch (JsonException) { fragment = null; }
        if (fragment is null || fragment.Nodes.Count == 0) return;
        InsertFragment(fragment, atX, atY);
        Editor.StatusText = $"📌 Вставлено блоков: {fragment.Nodes.Count}";
    }

    [RelayCommand]
    public void PasteNodesHere() => PasteNodes();

    /// <summary>Дублировать выделенные блоки вместе со связями между ними (Ctrl+D / ⌘D).</summary>
    [RelayCommand]
    public void DuplicateNodes()
    {
        var json = SerializeSelection();
        if (json is null) return;
        var fragment = JsonSerializer.Deserialize<GraphFragment>(json);
        if (fragment is null) return;
        InsertFragment(fragment, null, null);
        Editor.StatusText = $"Продублировано блоков: {fragment.Nodes.Count}";
    }

    void InsertFragment(GraphFragment fragment, double? atX, double? atY)
    {
        double minX = fragment.Nodes.Min(n => n.PosX);
        double minY = fragment.Nodes.Min(n => n.PosY);
        double dx = atX.HasValue ? atX.Value - minX : 40;
        double dy = atY.HasValue ? atY.Value - minY : 40;

        var idMap = new Dictionary<string, string>();
        var created = new List<EventNode>();
        foreach (var n in fragment.Nodes)
        {
            var newId = Guid.NewGuid().ToString("N")[..8];
            idMap[n.Id] = newId;
            n.Id = newId;
            n.PosX += dx;
            n.PosY += dy;
            CurrentGraph.AddNode(n);
            created.Add(n);
        }
        foreach (var c in fragment.Connections)
        {
            if (idMap.TryGetValue(c.FromNodeId, out var from) && idMap.TryGetValue(c.ToNodeId, out var to))
                CurrentGraph.Connections.Add(new EventConnection(from, c.FromPortId, to, c.ToPortId));
        }
        foreach (var g in fragment.Groups)
        {
            var ids = g.NodeIds.Where(idMap.ContainsKey).Select(id => idMap[id]).ToList();
            if (ids.Count > 0) CurrentGraph.CreateGroup(ids, g.Title, g.Color);
        }

        SelectedGroup = null;
        SetSelection(created);
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    // ================== ГРУППЫ ==================
    /// <summary>Объединить выделенные блоки в группу (Ctrl+G / ⌘G).</summary>
    [RelayCommand]
    public void GroupSelection()
    {
        if (_selection.Count == 0) { Editor.StatusText = "Выделите блоки, чтобы создать группу"; return; }
        var color = GroupColors[CurrentGraph.Groups.Count % GroupColors.Length];
        var group = CurrentGraph.CreateGroup(_selection.Select(n => n.Id), $"Группа {CurrentGraph.Groups.Count + 1}", color);
        if (group is null) return;
        SelectedGroup = group;
        LeftSidebarTabIndex = 0;
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.StatusText = $"🗂 Создана группа «{group.Title}» ({group.NodeIds.Count} бл.)";
    }

    /// <summary>Расформировать группу (Ctrl+Shift+G / ⇧⌘G). Блоки остаются на месте.</summary>
    [RelayCommand]
    public void UngroupSelection()
    {
        List<EventGroup> groups = SelectedGroup is not null
            ? new List<EventGroup> { SelectedGroup }
            : _selection.Select(n => CurrentGraph.FindGroupOfNode(n.Id)).OfType<EventGroup>().Distinct().ToList();
        if (groups.Count == 0) { Editor.StatusText = "Выделенные блоки не состоят в группе"; return; }
        foreach (var g in groups) CurrentGraph.RemoveGroup(g.Id);
        SelectedGroup = null;
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.StatusText = groups.Count == 1 ? "Группа расформирована" : $"Расформировано групп: {groups.Count}";
    }

    /// <summary>Удалить группу вместе со всеми её блоками.</summary>
    [RelayCommand]
    public void DeleteGroupWithNodes()
    {
        if (SelectedGroup is null) return;
        var ids = SelectedGroup.NodeIds.ToList();
        CurrentGraph.RemoveGroup(SelectedGroup.Id);
        CurrentGraph.RemoveNodes(ids);
        SelectedGroup = null;
        SetSelection([]);
        Editor.MarkDirty();
        NotifyGraphChanged();
        Editor.StatusText = $"Группа и {ids.Count} бл. удалены · {Hk.Label(Hk.Undo)} — вернуть";
    }

    [RelayCommand]
    public void SetGroupColor(string? hex)
    {
        if (SelectedGroup is null || string.IsNullOrWhiteSpace(hex)) return;
        SelectedGroup.Color = hex;
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    // ================== ПЕРЕМЕЩЕНИЕ ==================
    /// <summary>Сдвиг выделенных блоков стрелками.</summary>
    public void NudgeSelection(double dx, double dy)
    {
        if (_selection.Count == 0) return;
        foreach (var n in _selection) { n.PosX += dx; n.PosY += dy; }
        Editor.MarkDirty();
        NotifyGraphChanged();
    }

    /// <summary>Точка появления нового блока: центр видимой области с небольшим каскадом, чтобы блоки не ложились друг на друга.</summary>
    (double X, double Y) NextSpawnPosition()
    {
        var (cx, cy) = ViewCenterProvider?.Invoke() ?? (220, 220);
        var offset = (_spawnCascade++ % 6) * 28;
        return (Math.Round(cx - 100 + offset), Math.Round(cy - 40 + offset));
    }

    // Подсказки с учётом ОС
    public string TipDeleteNodes => Hk.Tip("Удалить выделенные блоки", Hk.Delete);
    public string TipDuplicateNodes => Hk.Tip("Дублировать выделенные", Hk.Duplicate);
    public string TipCopyNodes => Hk.Tip("Копировать", Hk.Copy);
    public string TipPasteNodes => Hk.Tip("Вставить", Hk.Paste);
    public string TipGroup => Hk.Tip("Объединить выделенные в группу", Hk.Group);
    public string TipUngroup => Hk.Tip("Расформировать группу", Hk.Ungroup);
    public string TipSelectAll => Hk.Tip("Выделить все блоки", Hk.SelectAll);
    public string TipFit => "Показать все блоки  (F)";
    public string CanvasHint => Hk.IsMac
        ? "Рамка — тянуть по пустому · ⇧/⌘+клик — добавить · ⌘A всё · ⌘G группа · ⌘C/⌘V · ⌘D дубль · ⌫ удалить · ПКМ/СКМ — панорама · двойной клик — добавить блок"
        : "Рамка — тянуть по пустому · Shift/Ctrl+клик — добавить · Ctrl+A всё · Ctrl+G группа · Ctrl+C/V · Ctrl+D дубль · Del удалить · ПКМ/СКМ — панорама · двойной клик — добавить блок";
}
