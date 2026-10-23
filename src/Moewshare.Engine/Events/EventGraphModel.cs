using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moewshare.Engine.Events;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortType
{
    Exec,
    Boolean,
    Integer,
    Float,
    String,
    Vector3,
    Color,
    ObjectRef,
    Any
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PortDirection
{
    Input,
    Output
}

public sealed class EventPort
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public PortType Type { get; set; } = PortType.Exec;
    public PortDirection Direction { get; set; } = PortDirection.Input;
    public string? DefaultValue { get; set; }

    public EventPort() { }

    public EventPort(string id, string name, PortType type, PortDirection direction, string? defaultValue = null)
    {
        Id = id;
        Name = name;
        Type = type;
        Direction = direction;
        DefaultValue = defaultValue;
    }

    public EventPort Clone() => new(Id, Name, Type, Direction, DefaultValue);
}

public sealed class EventNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string NodeType { get; set; } = "";
    public string Title { get; set; } = "Node";
    public string Category { get; set; } = "Общие";
    public double PosX { get; set; }
    public double PosY { get; set; }
    public List<EventPort> Inputs { get; set; } = [];
    public List<EventPort> Outputs { get; set; } = [];
    public Dictionary<string, string> Properties { get; set; } = [];

    public EventPort? FindInput(string portId)
    {
        var found = Inputs.FirstOrDefault(p => p.Id == portId);
        if (found is null && portId is "in_val" or "in_value" or "in_text" or "in_a")
        {
            return Inputs.FirstOrDefault(p => p.Id is "in_val" or "in_value" or "in_text" or "in_a");
        }
        return found;
    }

    public EventPort? FindOutput(string portId)
    {
        var found = Outputs.FirstOrDefault(p => p.Id == portId);
        if (found is null && portId is "out_val" or "out_result" or "out_str" or "out_res")
        {
            return Outputs.FirstOrDefault(p => p.Id is "out_val" or "out_result" or "out_str" or "out_res");
        }
        return found;
    }

    public EventPort? FindPort(string portId) => FindInput(portId) ?? FindOutput(portId);

    public EventNode Clone()
    {
        return new EventNode
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            NodeType = NodeType,
            Title = Title,
            Category = Category,
            PosX = PosX + 30,
            PosY = PosY + 30,
            Inputs = Inputs.Select(i => i.Clone()).ToList(),
            Outputs = Outputs.Select(o => o.Clone()).ToList(),
            Properties = new Dictionary<string, string>(Properties)
        };
    }
}

public sealed class EventConnection
{
    public string FromNodeId { get; set; } = "";
    public string FromPortId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public string ToPortId { get; set; } = "";

    public EventConnection() { }

    public EventConnection(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        FromNodeId = fromNodeId;
        FromPortId = fromPortId;
        ToNodeId = toNodeId;
        ToPortId = toPortId;
    }

    public override bool Equals(object? obj) =>
        obj is EventConnection other &&
        FromNodeId == other.FromNodeId &&
        FromPortId == other.FromPortId &&
        ToNodeId == other.ToNodeId &&
        ToPortId == other.ToPortId;

    public override int GetHashCode() =>
        HashCode.Combine(FromNodeId, FromPortId, ToNodeId, ToPortId);
}

public sealed class GraphVariable : INotifyPropertyChanged
{
    private string _name = "NewVar";
    private PortType _type = PortType.Integer;
    private string _defaultValue = "0";
    private string _currentValue = "0";
    private string _scope = "Graph";
    private string _description = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public PortType Type
    {
        get => _type;
        set
        {
            if (_type != value)
            {
                _type = value;
                OnPropertyChanged();
            }
        }
    }

    public string DefaultValue
    {
        get => _defaultValue;
        set
        {
            if (_defaultValue != value)
            {
                _defaultValue = value;
                _currentValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentValue));
            }
        }
    }

    public string CurrentValue
    {
        get => _currentValue;
        set
        {
            if (_currentValue != value)
            {
                _currentValue = value;
                OnPropertyChanged();
            }
        }
    }

    public string Scope
    {
        get => _scope;
        set
        {
            if (_scope != value)
            {
                _scope = value;
                OnPropertyChanged();
            }
        }
    }

    public string Description
    {
        get => _description;
        set
        {
            if (_description != value)
            {
                _description = value;
                OnPropertyChanged();
            }
        }
    }

    public GraphVariable() { }

    public GraphVariable(string name, PortType type, string defaultValue, string scope = "Graph", string desc = "")
    {
        _name = name;
        _type = type;
        _defaultValue = defaultValue;
        _currentValue = defaultValue;
        _scope = scope;
        _description = desc;
    }

    public GraphVariable Clone() => new(Name, Type, DefaultValue, Scope, Description);
}

/// <summary>
/// Группа (рамка-комментарий) на полотне графа: объединяет несколько блоков,
/// которые перемещаются вместе. Не влияет на выполнение логики.
/// </summary>
public sealed class EventGroup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "Группа";
    /// <summary>Цвет рамки группы в формате #RRGGBB.</summary>
    public string Color { get; set; } = "#E05263";
    public List<string> NodeIds { get; set; } = [];

    public EventGroup Clone() => new()
    {
        Id = Guid.NewGuid().ToString("N")[..8],
        Title = Title,
        Color = Color,
        NodeIds = [.. NodeIds]
    };
}

public sealed class EventGraph
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "MainGraph";
    public List<EventNode> Nodes { get; set; } = [];
    public List<EventConnection> Connections { get; set; } = [];
    public List<GraphVariable> Variables { get; set; } = [];
    public List<string> CustomEvents { get; set; } = [];
    /// <summary>Визуальные группы блоков (только для редактора).</summary>
    public List<EventGroup> Groups { get; set; } = [];

    public EventNode? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);
    public GraphVariable? FindVariable(string name) => Variables.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public EventGroup? FindGroup(string id) => Groups.FirstOrDefault(g => g.Id == id);
    public EventGroup? FindGroupOfNode(string nodeId) => Groups.FirstOrDefault(g => g.NodeIds.Contains(nodeId));

    public void AddNode(EventNode node)
    {
        if (!Nodes.Any(n => n.Id == node.Id))
        {
            Nodes.Add(node);
        }
    }

    public void RemoveNode(string nodeId)
    {
        Nodes.RemoveAll(n => n.Id == nodeId);
        Connections.RemoveAll(c => c.FromNodeId == nodeId || c.ToNodeId == nodeId);
        foreach (var g in Groups) g.NodeIds.Remove(nodeId);
        Groups.RemoveAll(g => g.NodeIds.Count == 0);
    }

    /// <summary>Удаляет сразу несколько блоков вместе со всеми их связями.</summary>
    public int RemoveNodes(IEnumerable<string> nodeIds)
    {
        var set = nodeIds.ToHashSet();
        if (set.Count == 0) return 0;
        int removed = Nodes.RemoveAll(n => set.Contains(n.Id));
        Connections.RemoveAll(c => set.Contains(c.FromNodeId) || set.Contains(c.ToNodeId));
        foreach (var g in Groups) g.NodeIds.RemoveAll(set.Contains);
        Groups.RemoveAll(g => g.NodeIds.Count == 0);
        return removed;
    }

    /// <summary>Создаёт группу из указанных блоков (блок может состоять только в одной группе).</summary>
    public EventGroup? CreateGroup(IEnumerable<string> nodeIds, string title = "Группа", string color = "#E05263")
    {
        var ids = nodeIds.Where(id => FindNode(id) is not null).Distinct().ToList();
        if (ids.Count == 0) return null;
        foreach (var g in Groups) g.NodeIds.RemoveAll(ids.Contains);
        Groups.RemoveAll(g => g.NodeIds.Count == 0);
        var group = new EventGroup { Title = title, Color = color, NodeIds = ids };
        Groups.Add(group);
        return group;
    }

    public void RemoveGroup(string groupId) => Groups.RemoveAll(g => g.Id == groupId);

    public bool CanConnect(EventNode fromNode, EventPort fromPort, EventNode toNode, EventPort toPort, out string reason)
    {
        reason = "";
        if (fromNode.Id == toNode.Id)
        {
            reason = "Нельзя соединить узел сам с собой.";
            return false;
        }

        if (fromPort.Direction == toPort.Direction)
        {
            reason = "Нужно соединять Выход с Входом.";
            return false;
        }

        // Must connect Output -> Input
        var outPort = fromPort.Direction == PortDirection.Output ? fromPort : toPort;
        var inPort = fromPort.Direction == PortDirection.Output ? toPort : fromPort;

        if (outPort.Type == PortType.Exec && inPort.Type != PortType.Exec)
        {
            reason = "Поток выполнения (Exec) можно соединять только с Exec.";
            return false;
        }

        if (outPort.Type != PortType.Exec && inPort.Type == PortType.Exec)
        {
            reason = "Данные нельзя подключать к потоку выполнения Exec.";
            return false;
        }

        // Type matching
        if (outPort.Type != PortType.Exec && outPort.Type != PortType.Any && inPort.Type != PortType.Any)
        {
            // Allow numeric float/int conversions
            bool isNumericOut = outPort.Type is PortType.Integer or PortType.Float;
            bool isNumericIn = inPort.Type is PortType.Integer or PortType.Float;
            if (isNumericOut && isNumericIn) return true;

            // Allow anything to String
            if (inPort.Type == PortType.String) return true;

            if (outPort.Type != inPort.Type)
            {
                reason = $"Несовместимые типы: {outPort.Type} и {inPort.Type}.";
                return false;
            }
        }

        return true;
    }

    public bool Connect(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        var fromNode = FindNode(fromNodeId);
        var toNode = FindNode(toNodeId);
        if (fromNode is null || toNode is null) return false;

        var fromPort = fromNode.FindPort(fromPortId);
        var toPort = toNode.FindPort(toPortId);
        if (fromPort is null || toPort is null) return false;

        if (!CanConnect(fromNode, fromPort, toNode, toPort, out _))
            return false;

        // Ensure from is Output, to is Input
        if (fromPort.Direction != PortDirection.Output)
        {
            (fromNodeId, toNodeId) = (toNodeId, fromNodeId);
            (fromPortId, toPortId) = (toPortId, fromPortId);
        }

        // Data input ports can only accept ONE incoming connection (replace existing).
        // Exec input ports can accept multiple incoming branches (converging execution flow).
        if (toPort.Type != PortType.Exec)
        {
            Connections.RemoveAll(c => c.ToNodeId == toNodeId && c.ToPortId == toPortId);
        }

        var conn = new EventConnection(fromNodeId, fromPortId, toNodeId, toPortId);
        if (!Connections.Contains(conn))
        {
            Connections.Add(conn);
        }
        return true;
    }

    public void Disconnect(string fromNodeId, string fromPortId, string toNodeId, string toPortId)
    {
        Connections.RemoveAll(c =>
            c.FromNodeId == fromNodeId && c.FromPortId == fromPortId &&
            c.ToNodeId == toNodeId && c.ToPortId == toPortId);
    }

    public void DisconnectPort(string nodeId, string portId)
    {
        Connections.RemoveAll(c =>
            (c.FromNodeId == nodeId && c.FromPortId == portId) ||
            (c.ToNodeId == nodeId && c.ToPortId == portId));
    }

    public EventConnection? GetConnectedSource(string toNodeId, string toPortId) =>
        Connections.FirstOrDefault(c => c.ToNodeId == toNodeId && c.ToPortId == toPortId);

    public List<EventConnection> GetConnectedTargets(string fromNodeId, string fromPortId) =>
        Connections.Where(c => c.FromNodeId == fromNodeId && c.FromPortId == fromPortId).ToList();

    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = indented });

    public static EventGraph? FromJson(string json) =>
        JsonSerializer.Deserialize<EventGraph>(json);

    public static EventGraph CreateCoinCollectorPreset()
    {
        var graph = new EventGraph { Name = "CoinCollectorLogic" };
        graph.Variables.Add(new GraphVariable("Coins", PortType.Integer, "0", "Graph", "Количество собранных монет"));

        var nTrigger = EventNodeLibrary.Find("event_trigger_enter")!.Factory();
        nTrigger.PosX = 60; nTrigger.PosY = 140;
        graph.AddNode(nTrigger);

        var nEqual = EventNodeLibrary.Find("logic_equal")!.Factory();
        nEqual.PosX = 340; nEqual.PosY = 280;
        nEqual.FindInput("in_b")!.DefaultValue = "Player";
        graph.AddNode(nEqual);

        var nBranch = EventNodeLibrary.Find("flow_branch")!.Factory();
        nBranch.PosX = 340; nBranch.PosY = 120;
        graph.AddNode(nBranch);

        var nInc = EventNodeLibrary.Find("var_increment")!.Factory();
        nInc.PosX = 580; nInc.PosY = 100;
        nInc.Properties["VariableName"] = "Coins";
        nInc.Title = "➕ Добавить монету (Coins++)";
        graph.AddNode(nInc);

        var nGetCoins = EventNodeLibrary.Find("var_get")!.Factory();
        nGetCoins.PosX = 580; nGetCoins.PosY = 280;
        nGetCoins.Properties["VariableName"] = "Coins";
        nGetCoins.Title = "🔢 Получить Coins";
        graph.AddNode(nGetCoins);

        var nFormat = EventNodeLibrary.Find("ui_format_text")!.Factory();
        nFormat.PosX = 820; nFormat.PosY = 220;
        nFormat.FindInput("in_prefix")!.DefaultValue = "Монеты: ";
        nFormat.FindInput("in_suffix")!.DefaultValue = " / 10";
        graph.AddNode(nFormat);

        var nSetText = EventNodeLibrary.Find("ui_set_text")!.Factory();
        nSetText.PosX = 820; nSetText.PosY = 80;
        nSetText.Properties["TargetName"] = "ScoreText";
        graph.AddNode(nSetText);

        var nSound = EventNodeLibrary.Find("audio_play_sound")!.Factory();
        nSound.PosX = 1060; nSound.PosY = 80;
        nSound.Properties["SoundType"] = "coin";
        graph.AddNode(nSound);

        var nDestroy = EventNodeLibrary.Find("object_destroy")!.Factory();
        nDestroy.PosX = 1280; nDestroy.PosY = 80;
        graph.AddNode(nDestroy);

        var nCheckWin = EventNodeLibrary.Find("logic_greater")!.Factory();
        nCheckWin.PosX = 1060; nCheckWin.PosY = 260;
        nCheckWin.FindInput("in_b")!.DefaultValue = "9";
        graph.AddNode(nCheckWin);

        var nBranchWin = EventNodeLibrary.Find("flow_branch")!.Factory();
        nBranchWin.PosX = 1280; nBranchWin.PosY = 240;
        graph.AddNode(nBranchWin);

        var nWin = EventNodeLibrary.Find("game_win")!.Factory();
        nWin.PosX = 1500; nWin.PosY = 220;
        graph.AddNode(nWin);

        // Connections
        graph.Connect(nTrigger.Id, "out_exec", nBranch.Id, "in_exec");
        graph.Connect(nTrigger.Id, "out_other_name", nEqual.Id, "in_a");
        graph.Connect(nEqual.Id, "out_res", nBranch.Id, "in_cond");

        graph.Connect(nBranch.Id, "out_true", nInc.Id, "in_exec");
        graph.Connect(nInc.Id, "out_exec", nSetText.Id, "in_exec");

        graph.Connect(nGetCoins.Id, "out_val", nFormat.Id, "in_val");
        graph.Connect(nFormat.Id, "out_result", nSetText.Id, "in_text");

        graph.Connect(nSetText.Id, "out_exec", nSound.Id, "in_exec");
        graph.Connect(nSound.Id, "out_exec", nDestroy.Id, "in_exec");
        graph.Connect(nTrigger.Id, "out_self", nDestroy.Id, "in_target");

        graph.Connect(nDestroy.Id, "out_exec", nBranchWin.Id, "in_exec");
        graph.Connect(nGetCoins.Id, "out_val", nCheckWin.Id, "in_a");
        graph.Connect(nCheckWin.Id, "out_res", nBranchWin.Id, "in_cond");
        graph.Connect(nBranchWin.Id, "out_true", nWin.Id, "in_exec");

        return graph;
    }

    public static EventGraph CreateDoorKeyPreset()
    {
        var graph = new EventGraph { Name = "DoorKeyLogic" };
        graph.Variables.Add(new GraphVariable("HasKey", PortType.Boolean, "false", "Graph", "Есть ли у игрока ключ"));

        var nKeyTrigger = EventNodeLibrary.Find("event_trigger_enter")!.Factory();
        nKeyTrigger.PosX = 60; nKeyTrigger.PosY = 100;
        nKeyTrigger.Title = "⚡ Подбор ключа";
        graph.AddNode(nKeyTrigger);

        var nSetKey = EventNodeLibrary.Find("var_set")!.Factory();
        nSetKey.PosX = 340; nSetKey.PosY = 100;
        nSetKey.Properties["VariableName"] = "HasKey";
        nSetKey.FindInput("in_val")!.DefaultValue = "true";
        graph.AddNode(nSetKey);

        var nSoundKey = EventNodeLibrary.Find("audio_play_sound")!.Factory();
        nSoundKey.PosX = 560; nSoundKey.PosY = 100;
        nSoundKey.Properties["SoundType"] = "door";
        graph.AddNode(nSoundKey);

        var nDestroyKey = EventNodeLibrary.Find("object_destroy")!.Factory();
        nDestroyKey.PosX = 780; nDestroyKey.PosY = 100;
        graph.AddNode(nDestroyKey);

        var nDoorTrigger = EventNodeLibrary.Find("event_trigger_enter")!.Factory();
        nDoorTrigger.PosX = 60; nDoorTrigger.PosY = 320;
        nDoorTrigger.Title = "⚡ Вход в дверь";
        graph.AddNode(nDoorTrigger);

        var nGetKey = EventNodeLibrary.Find("var_get")!.Factory();
        nGetKey.PosX = 340; nGetKey.PosY = 440;
        nGetKey.Properties["VariableName"] = "HasKey";
        graph.AddNode(nGetKey);

        var nBranch = EventNodeLibrary.Find("flow_branch")!.Factory();
        nBranch.PosX = 340; nBranch.PosY = 320;
        graph.AddNode(nBranch);

        var nOpenDoor = EventNodeLibrary.Find("object_destroy")!.Factory();
        nOpenDoor.PosX = 580; nOpenDoor.PosY = 300;
        nOpenDoor.Title = "🚪 Открыть / Убрать дверь";
        graph.AddNode(nOpenDoor);

        var nWin = EventNodeLibrary.Find("game_win")!.Factory();
        nWin.PosX = 800; nWin.PosY = 300;
        graph.AddNode(nWin);

        graph.Connect(nKeyTrigger.Id, "out_exec", nSetKey.Id, "in_exec");
        graph.Connect(nSetKey.Id, "out_exec", nSoundKey.Id, "in_exec");
        graph.Connect(nSoundKey.Id, "out_exec", nDestroyKey.Id, "in_exec");
        graph.Connect(nKeyTrigger.Id, "out_self", nDestroyKey.Id, "in_target");

        graph.Connect(nDoorTrigger.Id, "out_exec", nBranch.Id, "in_exec");
        graph.Connect(nGetKey.Id, "out_val", nBranch.Id, "in_cond");
        graph.Connect(nBranch.Id, "out_true", nOpenDoor.Id, "in_exec");
        graph.Connect(nOpenDoor.Id, "out_exec", nWin.Id, "in_exec");

        return graph;
    }

    public static EventGraph CreatePlayerHealthPreset()
    {
        var graph = new EventGraph { Name = "PlayerHealthLogic" };
        graph.Variables.Add(new GraphVariable("Health", PortType.Integer, "100", "Graph", "Текущее здоровье игрока"));

        var nHazard = EventNodeLibrary.Find("event_trigger_enter")!.Factory();
        nHazard.PosX = 60; nHazard.PosY = 140;
        nHazard.Title = "⚡ Опасность / Шипы";
        graph.AddNode(nHazard);

        var nDamage = EventNodeLibrary.Find("health_damage")!.Factory();
        nDamage.PosX = 340; nDamage.PosY = 120;
        nDamage.FindInput("in_dmg")!.DefaultValue = "25";
        graph.AddNode(nDamage);

        var nSound = EventNodeLibrary.Find("audio_play_sound")!.Factory();
        nSound.PosX = 580; nSound.PosY = 100;
        nSound.Properties["SoundType"] = "damage";
        graph.AddNode(nSound);

        var nFormat = EventNodeLibrary.Find("ui_format_text")!.Factory();
        nFormat.PosX = 580; nFormat.PosY = 260;
        nFormat.FindInput("in_prefix")!.DefaultValue = "Здоровье: ";
        nFormat.FindInput("in_suffix")!.DefaultValue = " %";
        graph.AddNode(nFormat);

        var nSetText = EventNodeLibrary.Find("ui_set_text")!.Factory();
        nSetText.PosX = 820; nSetText.PosY = 100;
        nSetText.Properties["TargetName"] = "HealthText";
        graph.AddNode(nSetText);

        var nBranch = EventNodeLibrary.Find("flow_branch")!.Factory();
        nBranch.PosX = 1040; nBranch.PosY = 100;
        graph.AddNode(nBranch);

        var nGameOver = EventNodeLibrary.Find("game_lose")!.Factory();
        nGameOver.PosX = 1260; nGameOver.PosY = 80;
        graph.AddNode(nGameOver);

        graph.Connect(nHazard.Id, "out_exec", nDamage.Id, "in_exec");
        graph.Connect(nHazard.Id, "out_other", nDamage.Id, "in_target");
        graph.Connect(nDamage.Id, "out_exec", nSound.Id, "in_exec");
        graph.Connect(nSound.Id, "out_exec", nSetText.Id, "in_exec");
        graph.Connect(nDamage.Id, "out_hp", nFormat.Id, "in_val");
        graph.Connect(nFormat.Id, "out_result", nSetText.Id, "in_text");
        graph.Connect(nSetText.Id, "out_exec", nBranch.Id, "in_exec");
        graph.Connect(nDamage.Id, "out_dead", nBranch.Id, "in_cond");
        graph.Connect(nBranch.Id, "out_true", nGameOver.Id, "in_exec");

        return graph;
    }

    public static EventGraph CreateClickerPreset()
    {
        var graph = new EventGraph { Name = "ClickerLogic" };
        graph.Variables.Add(new GraphVariable("Coins", PortType.Integer, "0", "Graph", "Количество монет / тапов"));
        graph.Variables.Add(new GraphVariable("AutoClickers", PortType.Integer, "0", "Graph", "Купленные автокликеры"));

        // 1. Click button event
        var nClick = EventNodeLibrary.Find("event_ui_button_clicked")!.Factory();
        nClick.PosX = 60; nClick.PosY = 100;
        nClick.Title = "👆 Тап по котику (Клик)";
        graph.AddNode(nClick);

        var nIncCoins = EventNodeLibrary.Find("var_increment")!.Factory();
        nIncCoins.PosX = 320; nIncCoins.PosY = 100;
        nIncCoins.Properties["VariableName"] = "Coins";
        graph.AddNode(nIncCoins);

        var nGetCoins = EventNodeLibrary.Find("var_get")!.Factory();
        nGetCoins.PosX = 540; nGetCoins.PosY = 240;
        nGetCoins.Properties["VariableName"] = "Coins";
        graph.AddNode(nGetCoins);

        var nFormat = EventNodeLibrary.Find("ui_format_text")!.Factory();
        nFormat.PosX = 540; nFormat.PosY = 100;
        nFormat.FindInput("in_prefix")!.DefaultValue = "Монеты: ";
        nFormat.FindInput("in_suffix")!.DefaultValue = " 🐾";
        graph.AddNode(nFormat);

        var nSetText = EventNodeLibrary.Find("ui_set_text")!.Factory();
        nSetText.PosX = 780; nSetText.PosY = 100;
        nSetText.Properties["TargetName"] = "ScoreText";
        graph.AddNode(nSetText);

        var nSound = EventNodeLibrary.Find("audio_play_sound")!.Factory();
        nSound.PosX = 1020; nSound.PosY = 100;
        nSound.Properties["SoundType"] = "coin";
        graph.AddNode(nSound);

        graph.Connect(nClick.Id, "out_exec", nIncCoins.Id, "in_exec");
        graph.Connect(nIncCoins.Id, "out_exec", nSetText.Id, "in_exec");
        graph.Connect(nGetCoins.Id, "out_val", nFormat.Id, "in_val");
        graph.Connect(nFormat.Id, "out_result", nSetText.Id, "in_text");
        graph.Connect(nSetText.Id, "out_exec", nSound.Id, "in_exec");

        // 2. Passive Income / Timer
        var nTimer = EventNodeLibrary.Find("event_timer")!.Factory();
        nTimer.PosX = 60; nTimer.PosY = 380;
        nTimer.Title = "⏲ Пассивный доход (1 сек)";
        graph.AddNode(nTimer);

        var nGetAutos = EventNodeLibrary.Find("var_get")!.Factory();
        nGetAutos.PosX = 320; nGetAutos.PosY = 480;
        nGetAutos.Properties["VariableName"] = "AutoClickers";
        graph.AddNode(nGetAutos);

        var nCheckAutos = EventNodeLibrary.Find("logic_greater")!.Factory();
        nCheckAutos.PosX = 320; nCheckAutos.PosY = 380;
        nCheckAutos.FindInput("in_b")!.DefaultValue = "0";
        graph.AddNode(nCheckAutos);

        var nBranchTimer = EventNodeLibrary.Find("flow_branch")!.Factory();
        nBranchTimer.PosX = 540; nBranchTimer.PosY = 380;
        graph.AddNode(nBranchTimer);

        var nAddIncome = EventNodeLibrary.Find("math_add")!.Factory();
        nAddIncome.PosX = 760; nAddIncome.PosY = 460;
        graph.AddNode(nAddIncome);

        var nSetCoins = EventNodeLibrary.Find("var_set")!.Factory();
        nSetCoins.PosX = 760; nSetCoins.PosY = 360;
        nSetCoins.Properties["VariableName"] = "Coins";
        graph.AddNode(nSetCoins);

        var nSetTextTimer = EventNodeLibrary.Find("ui_set_text")!.Factory();
        nSetTextTimer.PosX = 1000; nSetTextTimer.PosY = 360;
        nSetTextTimer.Properties["TargetName"] = "ScoreText";
        graph.AddNode(nSetTextTimer);

        graph.Connect(nTimer.Id, "out_exec", nBranchTimer.Id, "in_exec");
        graph.Connect(nGetAutos.Id, "out_val", nCheckAutos.Id, "in_a");
        graph.Connect(nCheckAutos.Id, "out_res", nBranchTimer.Id, "in_cond");
        graph.Connect(nBranchTimer.Id, "out_true", nSetCoins.Id, "in_exec");
        graph.Connect(nGetCoins.Id, "out_val", nAddIncome.Id, "in_a");
        graph.Connect(nGetAutos.Id, "out_val", nAddIncome.Id, "in_b");
        graph.Connect(nAddIncome.Id, "out_result", nSetCoins.Id, "in_val");
        graph.Connect(nSetCoins.Id, "out_exec", nSetTextTimer.Id, "in_exec");
        graph.Connect(nFormat.Id, "out_result", nSetTextTimer.Id, "in_text");

        return graph;
    }
}
