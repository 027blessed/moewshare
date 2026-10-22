using System.Globalization;

namespace Moewshare.Engine.Events;

public sealed class RuntimeExecutionContext
{
    public EventGraph Graph { get; }
    public List<SceneObject> ObjectList { get; } = [];
    public Dictionary<string, object> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SceneObject> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Inventory { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> HealthMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> DestroyedObjects { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> UiTextMap { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsWon { get; set; }
    public bool IsLost { get; set; }
    public bool IsRestartRequested { get; set; }

    public List<string> ExecutionLog { get; } = [];
    public List<string> ExecutedNodeIds { get; } = [];

    public int MaxStepsPerTrigger { get; set; } = 5000;
    public int CurrentStepCount { get; set; }

    public SceneObject? CurrentSelf { get; set; }
    public SceneObject? CurrentOther { get; set; }

    readonly HashSet<string> _doOnceFired = [];

    public RuntimeExecutionContext(EventGraph graph, IEnumerable<SceneObject>? sceneObjects = null)
    {
        Graph = graph;

        // Initialize variables
        foreach (var v in graph.Variables)
        {
            Variables[v.Name] = ParseInitialValue(v.Type, v.DefaultValue);
        }

        // Initialize objects
        if (sceneObjects is not null)
        {
            foreach (var o in sceneObjects)
            {
                ObjectList.Add(o);
                Objects[o.Name] = o;
                Objects[o.Id] = o;
                HealthMap[o.Id] = 100;
                HealthMap[o.Name] = 100;
                if (o.UiText is not null) UiTextMap[o.Name] = o.UiText;

                // Also initialize any per-object variables into context
                if (o.EventGraph is not null)
                {
                    foreach (var v in o.EventGraph.Variables)
                    {
                        if (!Variables.ContainsKey(v.Name))
                        {
                            Variables[v.Name] = ParseInitialValue(v.Type, v.DefaultValue);
                        }
                    }
                }
            }
        }
    }

    static object ParseInitialValue(PortType type, string? val)
    {
        val = val?.Trim() ?? "";
        return type switch
        {
            PortType.Boolean => bool.TryParse(val, out var b) && b,
            PortType.Integer => int.TryParse(val, out var i) ? i : 0,
            PortType.Float => double.TryParse(val, CultureInfo.InvariantCulture, out var d) ? d : 0.0,
            _ => val
        };
    }

    public void Log(string msg) => ExecutionLog.Add(msg);

    public bool CheckDoOnce(string nodeId, bool reset)
    {
        if (reset)
        {
            _doOnceFired.Remove(nodeId);
            return false;
        }
        if (_doOnceFired.Contains(nodeId)) return false;
        _doOnceFired.Add(nodeId);
        return true;
    }
}

public sealed class EventRuntime(EventGraph graph, IEnumerable<SceneObject>? sceneObjects = null)
{
    public RuntimeExecutionContext Context { get; } = new(graph, sceneObjects);

    public void TriggerBeginPlay()
    {
        RunEvent(Context.Graph, n => n.NodeType == "event_begin_play");

        foreach (var o in Context.ObjectList)
        {
            if (o.EventGraph is not null)
            {
                Context.CurrentSelf = o;
                RunEvent(o.EventGraph, n => n.NodeType == "event_begin_play");
            }
        }
    }

    public void TriggerUpdate(double dt)
    {
        RunEvent(Context.Graph, n => n.NodeType == "event_update");

        foreach (var o in Context.ObjectList)
        {
            if (o.EventGraph is not null)
            {
                Context.CurrentSelf = o;
                RunEvent(o.EventGraph, n => n.NodeType == "event_update");
            }
        }
    }

    public void TriggerEnter(SceneObject? self, SceneObject? other)
    {
        Context.CurrentSelf = self;
        Context.CurrentOther = other;
        RunEvent(Context.Graph, n => n.NodeType == "event_trigger_enter");

        if (self?.EventGraph is not null)
        {
            Context.CurrentSelf = self;
            Context.CurrentOther = other;
            RunEvent(self.EventGraph, n => n.NodeType == "event_trigger_enter");
        }

        if (other?.EventGraph is not null)
        {
            Context.CurrentSelf = other;
            Context.CurrentOther = self;
            RunEvent(other.EventGraph, n => n.NodeType == "event_trigger_enter");
        }
    }

    public void TriggerCollision(SceneObject? self, SceneObject? other)
    {
        Context.CurrentSelf = self;
        Context.CurrentOther = other;
        RunEvent(Context.Graph, n => n.NodeType == "event_collision_enter");

        if (self?.EventGraph is not null)
        {
            Context.CurrentSelf = self;
            Context.CurrentOther = other;
            RunEvent(self.EventGraph, n => n.NodeType == "event_collision_enter");
        }
    }

    public void TriggerInput(string key)
    {
        RunEvent(Context.Graph, n =>
            n.NodeType == "event_input_pressed" &&
            n.Properties.TryGetValue("Key", out var targetKey) &&
            targetKey.Equals(key, StringComparison.OrdinalIgnoreCase));

        foreach (var o in Context.ObjectList)
        {
            if (o.EventGraph is not null)
            {
                Context.CurrentSelf = o;
                RunEvent(o.EventGraph, n =>
                    n.NodeType == "event_input_pressed" &&
                    n.Properties.TryGetValue("Key", out var targetKey) &&
                    targetKey.Equals(key, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public void TriggerButtonClick(SceneObject? button)
    {
        Context.CurrentSelf = button;
        RunEvent(Context.Graph, n =>
            (n.NodeType == "event_ui_button_clicked" || n.NodeType == "event_object_clicked") &&
            (!n.Properties.TryGetValue("ButtonName", out var targetBtn) ||
             string.IsNullOrEmpty(targetBtn) ||
             targetBtn.Equals(button?.Name, StringComparison.OrdinalIgnoreCase) ||
             targetBtn.Equals(button?.Id, StringComparison.OrdinalIgnoreCase)));

        if (button?.EventGraph is not null)
        {
            Context.CurrentSelf = button;
            RunEvent(button.EventGraph, n => n.NodeType == "event_ui_button_clicked" || n.NodeType == "event_object_clicked");
        }
    }

    public void TriggerObjectClick(SceneObject? clickedObj)
    {
        Context.CurrentSelf = clickedObj;
        RunEvent(Context.Graph, n =>
            n.NodeType == "event_object_clicked" &&
            (!n.Properties.TryGetValue("ObjectName", out var targetName) ||
             string.IsNullOrEmpty(targetName) ||
             targetName.Equals(clickedObj?.Name, StringComparison.OrdinalIgnoreCase) ||
             targetName.Equals(clickedObj?.Id, StringComparison.OrdinalIgnoreCase)));

        if (clickedObj?.EventGraph is not null)
        {
            Context.CurrentSelf = clickedObj;
            RunEvent(clickedObj.EventGraph, n => n.NodeType == "event_object_clicked" || n.NodeType == "event_ui_button_clicked");
        }
    }

    public void TriggerTimer()
    {
        RunEvent(Context.Graph, n => n.NodeType == "event_timer");

        foreach (var o in Context.ObjectList)
        {
            if (o.EventGraph is not null)
            {
                Context.CurrentSelf = o;
                RunEvent(o.EventGraph, n => n.NodeType == "event_timer");
            }
        }
    }

    public void TriggerCustom(string eventName)
    {
        RunEvent(Context.Graph, n =>
            n.NodeType == "event_custom" &&
            n.Properties.TryGetValue("EventName", out var targetName) &&
            targetName.Equals(eventName, StringComparison.OrdinalIgnoreCase));

        foreach (var o in Context.ObjectList)
        {
            if (o.EventGraph is not null)
            {
                Context.CurrentSelf = o;
                RunEvent(o.EventGraph, n =>
                    n.NodeType == "event_custom" &&
                    n.Properties.TryGetValue("EventName", out var targetName) &&
                    targetName.Equals(eventName, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    EventGraph _activeGraph = graph;

    void RunEvent(EventGraph graph, Func<EventNode, bool> predicate)
    {
        _activeGraph = graph;
        var nodes = graph.Nodes.Where(predicate).ToList();
        foreach (var node in nodes)
        {
            Context.CurrentStepCount = 0;
            ExecuteDownstream(node, "out_exec");
        }
    }

    void ExecuteDownstream(EventNode fromNode, string fromPortId)
    {
        var conns = _activeGraph.GetConnectedTargets(fromNode.Id, fromPortId);
        foreach (var conn in conns)
        {
            var targetNode = _activeGraph.FindNode(conn.ToNodeId);
            if (targetNode is not null)
            {
                ExecuteNode(targetNode, conn.ToPortId);
            }
        }
    }

    void ExecuteNode(EventNode node, string inPortId)
    {
        if (++Context.CurrentStepCount > Context.MaxStepsPerTrigger)
        {
            Context.Log($"[ОШИБКА] Превышен лимит шагов выполнения ({Context.MaxStepsPerTrigger}). Возможен бесконечный цикл в графе!");
            return;
        }

        Context.ExecutedNodeIds.Add(node.Id);

        switch (node.NodeType)
        {
            case "flow_branch":
                bool condition = EvaluateBool(node, "in_cond");
                Context.Log($"Branch: {condition}");
                ExecuteDownstream(node, condition ? "out_true" : "out_false");
                break;

            case "flow_sequence":
                Context.Log("Sequence: then 0");
                ExecuteDownstream(node, "out_0");
                Context.Log("Sequence: then 1");
                ExecuteDownstream(node, "out_1");
                Context.Log("Sequence: then 2");
                ExecuteDownstream(node, "out_2");
                break;

            case "flow_for_loop":
                int first = EvaluateInt(node, "in_first");
                int last = EvaluateInt(node, "in_last");
                Context.Log($"ForLoop from {first} to {last}");
                for (int i = first; i <= last; i++)
                {
                    if (Context.CurrentStepCount > Context.MaxStepsPerTrigger) break;
                    // Temporary store index in properties for evaluation
                    node.Properties["_current_index"] = i.ToString();
                    ExecuteDownstream(node, "out_body");
                }
                ExecuteDownstream(node, "out_done");
                break;

            case "flow_do_once":
                if (inPortId == "in_reset")
                {
                    Context.CheckDoOnce(node.Id, reset: true);
                }
                else
                {
                    if (Context.CheckDoOnce(node.Id, reset: false))
                    {
                        ExecuteDownstream(node, "out_exec");
                    }
                }
                break;

            case "flow_delay":
                // In synchronous simulation, delay fires immediately; in web runtime it uses setTimeout
                Context.Log($"Delay {node.FindInput("in_duration")?.DefaultValue ?? "1"}s");
                ExecuteDownstream(node, "out_exec");
                break;

            case "var_set":
                if (node.Properties.TryGetValue("VariableName", out var setVarName))
                {
                    var val = EvaluateValue(node, "in_val");
                    var targetVar = Context.CurrentSelf?.EventGraph?.FindVariable(setVarName) ?? Context.Graph.FindVariable(setVarName);
                    if ((targetVar?.Type == PortType.Integer || (Context.Variables.TryGetValue(setVarName, out var exVal) && exVal is int)) &&
                        double.TryParse(val?.ToString(), CultureInfo.InvariantCulture, out var num))
                    {
                        val = (int)num;
                    }
                    Context.Variables[setVarName] = val;
                    Context.Log($"Set Variable [{setVarName}] = {val}");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "var_increment":
                if (node.Properties.TryGetValue("VariableName", out var incVarName))
                {
                    int cur = 0;
                    if (Context.Variables.TryGetValue(incVarName, out var cv))
                    {
                        if (cv is int ci) cur = ci;
                        else if (int.TryParse(cv.ToString(), out var parsed)) cur = parsed;
                    }
                    cur++;
                    Context.Variables[incVarName] = cur;
                    Context.Log($"Increment Variable [{incVarName}] -> {cur}");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "var_decrement":
                if (node.Properties.TryGetValue("VariableName", out var decVarName))
                {
                    int cur = 0;
                    if (Context.Variables.TryGetValue(decVarName, out var cv))
                    {
                        if (cv is int ci) cur = ci;
                        else if (int.TryParse(cv.ToString(), out var parsed)) cur = parsed;
                    }
                    cur--;
                    Context.Variables[decVarName] = cur;
                    Context.Log($"Decrement Variable [{decVarName}] -> {cur}");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "object_destroy":
                var targetToDestroy = EvaluateObject(node, "in_target") ?? Context.CurrentOther ?? Context.CurrentSelf;
                if (targetToDestroy is not null)
                {
                    Context.DestroyedObjects.Add(targetToDestroy.Id);
                    Context.DestroyedObjects.Add(targetToDestroy.Name);
                    Context.Log($"Destroyed object: {targetToDestroy.Name}");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "object_set_active":
                var targetActive = EvaluateObject(node, "in_target") ?? Context.CurrentSelf;
                bool active = EvaluateBool(node, "in_active");
                if (targetActive is not null)
                {
                    targetActive.IsVisible = active;
                    if (!active) Context.DestroyedObjects.Add(targetActive.Id);
                    else Context.DestroyedObjects.Remove(targetActive.Id);
                    Context.Log($"Set object [{targetActive.Name}] active = {active}");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "object_set_position":
                var targetMove = EvaluateObject(node, "in_target") ?? Context.CurrentSelf;
                if (targetMove is not null)
                {
                    targetMove.Position.X = EvaluateDouble(node, "in_x");
                    targetMove.Position.Y = EvaluateDouble(node, "in_y");
                    targetMove.Position.Z = EvaluateDouble(node, "in_z");
                    Context.Log($"Moved object [{targetMove.Name}] to ({targetMove.Position.X}, {targetMove.Position.Y}, {targetMove.Position.Z})");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "health_damage":
                var targetDmg = EvaluateObject(node, "in_target") ?? Context.CurrentOther ?? Context.CurrentSelf;
                int dmg = EvaluateInt(node, "in_dmg");
                if (targetDmg is not null)
                {
                    int curHp = Context.HealthMap.TryGetValue(targetDmg.Id, out var h) ? h : 100;
                    curHp = Math.Max(0, curHp - dmg);
                    Context.HealthMap[targetDmg.Id] = curHp;
                    Context.HealthMap[targetDmg.Name] = curHp;
                    node.Properties["_current_hp"] = curHp.ToString();
                    node.Properties["_is_dead"] = (curHp <= 0).ToString();
                    Context.Log($"Damaged [{targetDmg.Name}] by {dmg} -> HP: {curHp}");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "health_heal":
                var targetHeal = EvaluateObject(node, "in_target") ?? Context.CurrentSelf;
                int heal = EvaluateInt(node, "in_amount");
                if (targetHeal is not null)
                {
                    int curHp = Context.HealthMap.TryGetValue(targetHeal.Id, out var h) ? h : 100;
                    curHp = Math.Min(100, curHp + heal);
                    Context.HealthMap[targetHeal.Id] = curHp;
                    Context.HealthMap[targetHeal.Name] = curHp;
                    Context.Log($"Healed [{targetHeal.Name}] by {heal} -> HP: {curHp}");
                }
                ExecuteDownstream(node, "out_exec");
                break;

            case "game_win":
                Context.IsWon = true;
                Context.Log("🏆 Game Won!");
                ExecuteDownstream(node, "out_exec");
                break;

            case "game_lose":
                Context.IsLost = true;
                Context.Log("💀 Game Over!");
                ExecuteDownstream(node, "out_exec");
                break;

            case "game_restart":
                Context.IsRestartRequested = true;
                Context.Log("🔄 Restart Requested");
                ExecuteDownstream(node, "out_exec");
                break;

            case "ui_set_text":
                string txtTarget = node.Properties.TryGetValue("TargetName", out var tn) && !string.IsNullOrEmpty(tn) ? tn : "Text";
                string prefix = node.Properties.TryGetValue("Prefix", out var pfx) ? pfx : "";
                string suffix = node.Properties.TryGetValue("Suffix", out var sfx) ? sfx : "";
                string rawText = EvaluateString(node, "in_text");
                string fullText = prefix + rawText + suffix;
                Context.UiTextMap[txtTarget] = fullText;
                var targetObj = Context.ObjectList.FirstOrDefault(o => o.Type == ObjectTypes.UiText && (o.Name.Equals(txtTarget, StringComparison.OrdinalIgnoreCase) || o.Id.Equals(txtTarget, StringComparison.OrdinalIgnoreCase)))
                             ?? (txtTarget == "Text" ? Context.ObjectList.FirstOrDefault(o => o.Type == ObjectTypes.UiText) : null);
                if (targetObj != null) targetObj.UiText = fullText;
                Context.Log($"Set UI Text [{txtTarget}] = «{fullText}»");
                ExecuteDownstream(node, "out_exec");
                break;

            case "inventory_add_item":
                string addItemId = node.Properties.TryGetValue("ItemId", out var aId) ? aId : "Key";
                Context.Inventory.Add(addItemId);
                Context.Log($"Added to inventory: {addItemId}");
                ExecuteDownstream(node, "out_exec");
                break;

            case "inventory_remove_item":
                string remItemId = node.Properties.TryGetValue("ItemId", out var rId) ? rId : "Key";
                Context.Inventory.Remove(remItemId);
                Context.Log($"Removed from inventory: {remItemId}");
                ExecuteDownstream(node, "out_exec");
                break;

            case "audio_play_sound":
                string sound = node.Properties.TryGetValue("SoundType", out var st) ? st : "coin";
                Context.Log($"Play Sound: {sound}");
                ExecuteDownstream(node, "out_exec");
                break;

            default:
                ExecuteDownstream(node, "out_exec");
                break;
        }
    }

    public object EvaluateValue(EventNode node, string portId)
    {
        var inputPort = node.FindInput(portId);
        var sourceConn = _activeGraph.GetConnectedSource(node.Id, portId);

        if (sourceConn is null)
        {
            return inputPort?.DefaultValue ?? "";
        }

        var srcNode = _activeGraph.FindNode(sourceConn.FromNodeId);
        if (srcNode is null) return inputPort?.DefaultValue ?? "";

        return EvaluateOutput(srcNode, sourceConn.FromPortId);
    }

    public object EvaluateOutput(EventNode node, string portId)
    {
        switch (node.NodeType)
        {
            case "event_trigger_enter":
                if (portId == "out_other") return Context.CurrentOther ?? new SceneObject { Name = "Unknown" };
                if (portId == "out_other_name") return Context.CurrentOther?.Name ?? "";
                if (portId == "out_self") return Context.CurrentSelf ?? new SceneObject { Name = "Self" };
                return "";

            case "event_ui_button_clicked":
                if (portId == "out_name") return Context.CurrentSelf?.Name ?? "";
                return "";

            case "event_object_clicked":
                if (portId == "out_self") return Context.CurrentSelf ?? new SceneObject { Name = "Self" };
                if (portId == "out_name") return Context.CurrentSelf?.Name ?? "";
                return "";

            case "var_get":
                if (node.Properties.TryGetValue("VariableName", out var vName) &&
                    Context.Variables.TryGetValue(vName, out var vVal))
                {
                    return vVal;
                }
                return 0;

            case "var_set":
                if (node.Properties.TryGetValue("VariableName", out var sName) &&
                    Context.Variables.TryGetValue(sName, out var sVal))
                {
                    return sVal;
                }
                return EvaluateValue(node, "in_val") ?? 0;

            case "var_increment":
            case "var_decrement":
                if (node.Properties.TryGetValue("VariableName", out var incName) &&
                    Context.Variables.TryGetValue(incName, out var incVal))
                {
                    return incVal;
                }
                return 0;

            case "math_add":
                return EvaluateDouble(node, "in_a") + EvaluateDouble(node, "in_b");

            case "math_subtract":
                return EvaluateDouble(node, "in_a") - EvaluateDouble(node, "in_b");

            case "math_multiply":
                return EvaluateDouble(node, "in_a") * EvaluateDouble(node, "in_b");

            case "math_divide":
                double b = EvaluateDouble(node, "in_b");
                return Math.Abs(b) > 0.000001 ? EvaluateDouble(node, "in_a") / b : 0.0;

            case "math_clamp":
                double val = EvaluateDouble(node, "in_val");
                double min = EvaluateDouble(node, "in_min");
                double max = EvaluateDouble(node, "in_max");
                return Math.Clamp(val, min, max);

            case "math_random_int":
                int rmin = EvaluateInt(node, "in_min");
                int rmax = EvaluateInt(node, "in_max");
                return Random.Shared.Next(rmin, rmax + 1);

            case "logic_equal":
                var eqA = EvaluateValue(node, "in_a");
                var eqB = EvaluateValue(node, "in_b");
                return CompareValues(eqA, eqB) == 0;

            case "logic_not_equal":
                var neqA = EvaluateValue(node, "in_a");
                var neqB = EvaluateValue(node, "in_b");
                return CompareValues(neqA, neqB) != 0;

            case "logic_greater":
                return EvaluateDouble(node, "in_a") > EvaluateDouble(node, "in_b");

            case "logic_greater_equal":
                return EvaluateDouble(node, "in_a") >= EvaluateDouble(node, "in_b");

            case "logic_less":
                return EvaluateDouble(node, "in_a") < EvaluateDouble(node, "in_b");

            case "logic_less_equal":
                return EvaluateDouble(node, "in_a") <= EvaluateDouble(node, "in_b");

            case "logic_and":
                return EvaluateBool(node, "in_a") && EvaluateBool(node, "in_b");

            case "logic_or":
                return EvaluateBool(node, "in_a") || EvaluateBool(node, "in_b");

            case "logic_not":
                return !EvaluateBool(node, "in_a");

            case "object_get_player":
                var player = Context.Objects.Values.FirstOrDefault(o =>
                    o.Behavior is Behaviors.Platformer or Behaviors.TopDown or Behaviors.ThirdPerson ||
                    o.Name.Contains("Player", StringComparison.OrdinalIgnoreCase));
                if (portId == "out_name") return player?.Name ?? "Player";
                return player ?? new SceneObject { Name = "Player" };

            case "object_get_self":
                return Context.CurrentSelf ?? new SceneObject { Name = "Self" };

            case "object_get_by_name":
                string name = node.Properties.TryGetValue("ObjectName", out var objN) ? objN : "";
                Context.Objects.TryGetValue(name, out var foundObj);
                if (portId == "out_found") return foundObj is not null;
                return foundObj ?? new SceneObject { Name = name };

            case "ui_format_text":
                string prefix = EvaluateString(node, "in_prefix");
                string vStr = EvaluateValue(node, "in_val")?.ToString() ?? "";
                string suffix = EvaluateString(node, "in_suffix");
                return $"{prefix}{vStr}{suffix}";

            case "inventory_has_item":
                string itemKey = node.Properties.TryGetValue("ItemId", out var k) ? k : "Key";
                return Context.Inventory.Contains(itemKey);

            case "flow_for_loop":
                if (portId == "out_index" && node.Properties.TryGetValue("_current_index", out var idxStr))
                {
                    return int.TryParse(idxStr, out var idx) ? idx : 0;
                }
                return 0;

            case "health_damage":
                if (portId == "out_hp") return int.TryParse(node.Properties.GetValueOrDefault("_current_hp", "100"), out var hp) ? hp : 0;
                if (portId == "out_dead") return bool.TryParse(node.Properties.GetValueOrDefault("_is_dead", "false"), out var dead) && dead;
                return 0;

            default:
                return 0;
        }
    }

    public bool EvaluateBool(EventNode node, string portId)
    {
        var val = EvaluateValue(node, portId);
        if (val is bool b) return b;
        if (bool.TryParse(val?.ToString(), out var parsedB)) return parsedB;
        if (double.TryParse(val?.ToString(), CultureInfo.InvariantCulture, out var d)) return Math.Abs(d) > 0.0001;
        return false;
    }

    public int EvaluateInt(EventNode node, string portId)
    {
        var val = EvaluateValue(node, portId);
        if (val is int i) return i;
        if (val is double d) return (int)d;
        if (int.TryParse(val?.ToString(), out var parsedI)) return parsedI;
        return 0;
    }

    public double EvaluateDouble(EventNode node, string portId)
    {
        var val = EvaluateValue(node, portId);
        if (val is double d) return d;
        if (val is int i) return i;
        if (double.TryParse(val?.ToString(), CultureInfo.InvariantCulture, out var parsedD)) return parsedD;
        return 0.0;
    }

    public string EvaluateString(EventNode node, string portId)
    {
        var val = EvaluateValue(node, portId);
        return val?.ToString() ?? "";
    }

    public SceneObject? EvaluateObject(EventNode node, string portId)
    {
        var val = EvaluateValue(node, portId);
        if (val is SceneObject obj) return obj;
        if (val is string nameOrId && Context.Objects.TryGetValue(nameOrId, out var found)) return found;
        return null;
    }

    static int CompareValues(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        if (double.TryParse(a.ToString(), CultureInfo.InvariantCulture, out var da) &&
            double.TryParse(b.ToString(), CultureInfo.InvariantCulture, out var db))
        {
            return da.CompareTo(db);
        }

        return string.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
