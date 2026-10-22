namespace Moewshare.Engine.Events;

public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ValidationIssue(
    IssueSeverity Severity,
    string Message,
    string? NodeId = null,
    string? PortId = null
);

public static class EventValidator
{
    public static List<ValidationIssue> Validate(EventGraph graph)
    {
        var issues = new List<ValidationIssue>();

        if (graph is null)
        {
            issues.Add(new(IssueSeverity.Error, "Граф событий пуст или не инициализирован."));
            return issues;
        }

        var nodeMap = graph.Nodes.ToDictionary(n => n.Id);

        // 1. Check connections integrity
        foreach (var conn in graph.Connections)
        {
            if (!nodeMap.TryGetValue(conn.FromNodeId, out var fromNode))
            {
                issues.Add(new(IssueSeverity.Error, $"Соединение ссылается на отсутствующий узел источника «{conn.FromNodeId}»."));
                continue;
            }

            if (!nodeMap.TryGetValue(conn.ToNodeId, out var toNode))
            {
                issues.Add(new(IssueSeverity.Error, $"Соединение ссылается на отсутствующий целевой узел «{conn.ToNodeId}»."));
                continue;
            }

            var fromPort = fromNode.FindPort(conn.FromPortId);
            var toPort = toNode.FindPort(conn.ToPortId);

            if (fromPort is null)
            {
                issues.Add(new(IssueSeverity.Error, $"Выходной порт «{conn.FromPortId}» не найден в узле «{fromNode.Title}».", fromNode.Id));
                continue;
            }

            if (toPort is null)
            {
                issues.Add(new(IssueSeverity.Error, $"Входной порт «{conn.ToPortId}» не найден в узле «{toNode.Title}».", toNode.Id));
                continue;
            }

            if (!graph.CanConnect(fromNode, fromPort, toNode, toPort, out var reason))
            {
                issues.Add(new(IssueSeverity.Error, $"Недопустимое соединение между «{fromNode.Title}.{fromPort.Name}» и «{toNode.Title}.{toPort.Name}»: {reason}", fromNode.Id, fromPort.Id));
            }
        }

        // 2. Check variable references
        var varNames = new HashSet<string>(graph.Variables.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            if (node.NodeType is "var_get" or "var_set" or "var_increment" or "var_decrement")
            {
                if (node.Properties.TryGetValue("VariableName", out var vName) && !string.IsNullOrWhiteSpace(vName))
                {
                    if (!varNames.Contains(vName))
                    {
                        issues.Add(new(IssueSeverity.Warning, $"Узел «{node.Title}» ссылается на несуществующую переменную «{vName}».", node.Id));
                    }
                }
                else
                {
                    issues.Add(new(IssueSeverity.Error, $"В узле «{node.Title}» не указано имя переменной.", node.Id));
                }
            }

            // Check Branch condition
            if (node.NodeType == "flow_branch")
            {
                var condInput = node.FindInput("in_cond");
                var hasConnection = graph.Connections.Any(c => c.ToNodeId == node.Id && c.ToPortId == "in_cond");
                if (!hasConnection && string.IsNullOrWhiteSpace(condInput?.DefaultValue))
                {
                    issues.Add(new(IssueSeverity.Warning, $"В узле «{node.Title}» не подключено условие и не задано значение по умолчанию.", node.Id));
                }
            }
        }

        // 3. Check for standalone disconnected event nodes
        var eventNodes = graph.Nodes.Where(n => n.NodeType.StartsWith("event_")).ToList();
        if (eventNodes.Count == 0 && graph.Nodes.Count > 0)
        {
            issues.Add(new(IssueSeverity.Info, "В графе нет ни одного начального события (например, Begin Play или On Trigger Enter). Логика не запустится сама."));
        }

        return issues;
    }
}
