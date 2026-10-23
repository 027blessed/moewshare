using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Moewshare.Editor.Services;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine.Events;

namespace Moewshare.Editor.Views;

/// <summary>
/// Интерактивное полотно визуальной системы событий.
/// Поддерживает: рамку выделения, множественный выбор (Shift / Ctrl / ⌘), перетаскивание группы блоков,
/// группы-рамки, копирование / вставку / дублирование, массовое удаление, панораму и масштаб.
/// Цвета берутся из палитры темы (светлая / тёмная).
/// </summary>
public sealed class EventGraphCanvas : Control
{
    EventGraphVM? _vm;
    EventGraph? _lastGraph;
    bool _needsFit = true;

    // Вид
    Vector _pan = new(100, 80);
    double _zoom = 1.0;

    // Состояния взаимодействия
    enum DragMode { None, Pan, Nodes, Marquee, Wire }
    DragMode _mode;
    Point _pressPoint;
    Point _lastPointer;
    bool _movedSincePress;
    bool _rightButtonPan;
    bool _spaceHeld;
    bool _detachedWire;

    // Перетаскивание блоков
    List<EventNode> _dragNodes = [];

    // Рамка выделения
    Rect _marquee;
    bool _marqueeAdditive;
    List<EventNode> _marqueeBase = [];

    // Провод
    EventNode? _wireStartNode;
    EventPort? _wireStartPort;
    Point _currentWireEnd;

    // Наведение
    EventNode? _hoverNode;
    EventPort? _hoverPort;

    /// <summary>Просьба открыть каталог блоков в точке (двойной клик / контекстное меню).</summary>
    public event Action<double, double>? RequestNodePalette;

    // Геометрия
    public const double NodeWidth = 210;
    const double NodeHeaderHeight = 32;
    const double PortRowHeight = 24;
    const double PinRadius = 6;
    const double GroupPadding = 18;
    const double GroupHeader = 28;

    static readonly Typeface Font = new("Inter,Segoe UI,-apple-system,sans-serif");
    static readonly Typeface BoldFont = new("Inter,Segoe UI,-apple-system,sans-serif", FontStyle.Normal, FontWeight.Bold);

    public EventGraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void Attach(EventGraphVM vm)
    {
        if (_vm == vm) return;
        if (_vm is not null)
        {
            _vm.GraphChanged -= InvalidateVisual;
            _vm.ViewCenterProvider = null;
        }
        _vm = vm;
        vm.GraphChanged += InvalidateVisual;
        vm.ViewCenterProvider = () =>
        {
            var c = ScreenToGraph(new Point(Math.Max(1, Bounds.Width) / 2, Math.Max(1, Bounds.Height) / 2));
            return (c.X, c.Y);
        };
        _needsFit = true;
        InvalidateVisual();
    }

    // ================== Преобразования ==================
    Point GraphToScreen(Point p) => new(p.X * _zoom + _pan.X, p.Y * _zoom + _pan.Y);
    Point ScreenToGraph(Point p) => new((p.X - _pan.X) / _zoom, (p.Y - _pan.Y) / _zoom);
    Rect GraphRectToScreen(Rect r) => new(GraphToScreen(r.TopLeft), GraphToScreen(r.BottomRight));

    void SetZoom(double zoom, Point anchor)
    {
        double old = _zoom;
        _zoom = Math.Clamp(zoom, 0.2, 2.5);
        _pan = anchor - (anchor - _pan) * (_zoom / old);
        if (_vm is not null) _vm.ZoomLevel = _zoom;
        InvalidateVisual();
    }

    public void ResetView()
    {
        _pan = new(80, 80);
        _zoom = 1.0;
        if (_vm is not null) _vm.ZoomLevel = _zoom;
        InvalidateVisual();
    }

    public void ZoomIn() => SetZoom(_zoom * 1.2, new Point(Bounds.Width / 2, Bounds.Height / 2));
    public void ZoomOut() => SetZoom(_zoom / 1.2, new Point(Bounds.Width / 2, Bounds.Height / 2));
    public void ZoomReset() => SetZoom(1.0, new Point(Bounds.Width / 2, Bounds.Height / 2));

    /// <summary>Вписать все блоки (или выделенные) в видимую область.</summary>
    public void CenterOnNodes() => FitToNodes(onlySelection: false);

    public void FitToNodes(bool onlySelection)
    {
        if (_vm is null) return;
        var nodes = onlySelection && _vm.SelectedNodes.Count > 0 ? _vm.SelectedNodes.ToList() : _vm.CurrentGraph.Nodes;
        if (nodes.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0) { ResetView(); return; }

        double minX = nodes.Min(n => n.PosX);
        double minY = nodes.Min(n => n.PosY);
        double maxX = nodes.Max(n => n.PosX + NodeWidth);
        double maxY = nodes.Max(n => n.PosY + GetNodeHeight(n));
        foreach (var g in _vm.CurrentGraph.Groups)
        {
            var r = GetGroupRect(g);
            if (r is null) continue;
            minX = Math.Min(minX, r.Value.X); minY = Math.Min(minY, r.Value.Y);
            maxX = Math.Max(maxX, r.Value.Right); maxY = Math.Max(maxY, r.Value.Bottom);
        }

        const double margin = 60;
        double w = Math.Max(1, maxX - minX), h = Math.Max(1, maxY - minY);
        _zoom = Math.Clamp(Math.Min((Bounds.Width - margin * 2) / w, (Bounds.Height - margin * 2) / h), 0.25, 1.2);
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        _pan = new Vector(Bounds.Width / 2 - cx * _zoom, Bounds.Height / 2 - cy * _zoom);
        _vm.ZoomLevel = _zoom;
        InvalidateVisual();
    }

    // ================== Отрисовка ==================
    IBrush B(string key, string fallback) => this.ResBrush(key, new SolidColorBrush(Color.Parse(fallback)));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if (_vm is not null && (_needsFit || !ReferenceEquals(_lastGraph, _vm.CurrentGraph)))
        {
            // Новый граф (глобальный ↔ объекта) — один раз вписываем блоки в экран
            _lastGraph = _vm.CurrentGraph;
            _needsFit = false;
            if (_vm.CurrentGraph.Nodes.Count > 0)
                Dispatcher.UIThread.Post(() => FitToNodes(onlySelection: false), DispatcherPriority.Background);
        }

        context.FillRectangle(B("GraphBgBrush", "#17141A"), bounds);
        RenderGrid(context, bounds);

        if (_vm is null) return;
        var graph = _vm.CurrentGraph;

        // 1. Группы (под блоками)
        foreach (var g in graph.Groups) RenderGroup(context, g);

        // 2. Провода
        var wireWidth = 2.4 * Math.Clamp(_zoom, 0.7, 1.4);
        foreach (var conn in graph.Connections)
        {
            var fromNode = graph.FindNode(conn.FromNodeId);
            var toNode = graph.FindNode(conn.ToNodeId);
            if (fromNode is null || toNode is null) continue;
            var fromPort = fromNode.FindPort(conn.FromPortId);
            var toPort = toNode.FindPort(conn.ToPortId);
            if (fromPort is null || toPort is null) continue;

            var p1 = GraphToScreen(GetPortPosition(fromNode, fromPort));
            var p2 = GraphToScreen(GetPortPosition(toNode, toPort));
            bool highlighted = _vm.IsNodeSelected(fromNode) || _vm.IsNodeSelected(toNode);
            DrawBezierWire(context, p1, p2, GetPortColor(fromPort.Type), highlighted ? wireWidth + 1.2 : wireWidth);
        }

        // 3. Провод в процессе перетаскивания
        if (_mode == DragMode.Wire && _wireStartNode is not null && _wireStartPort is not null)
        {
            var p1 = GraphToScreen(GetPortPosition(_wireStartNode, _wireStartPort));
            DrawBezierWire(context, p1, _currentWireEnd, GetPortColor(_wireStartPort.Type), wireWidth + 0.6, isDashed: true);
        }

        // 4. Блоки
        foreach (var node in graph.Nodes)
            RenderNode(context, node, _vm.IsNodeSelected(node), node == _vm.SelectedNode);

        // 5. Рамка выделения
        if (_mode == DragMode.Marquee && _marquee.Width > 0 && _marquee.Height > 0)
        {
            var sel = B("SelectionBrush", "#FF4B72");
            context.DrawRectangle(B("SelectionFillBrush", "#22FF4B72"), new Pen(sel, 1, new DashStyle([4, 3], 0)), _marquee, 3, 3);
        }

        // 6. Пустое состояние
        if (graph.Nodes.Count == 0) RenderEmptyHint(context, bounds);
    }

    void RenderEmptyHint(DrawingContext context, Rect bounds)
    {
        var title = new FormattedText("Полотно пустое", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, BoldFont, 16, B("Text2Brush", "#B9A8B1"));
        var sub = new FormattedText(
            "Дважды кликните по полотну или нажмите «+ Блок», чтобы добавить первый блок логики.\nИли загрузите готовый шаблон механики.",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 12, B("Text3Brush", "#7E6E77"))
        { TextAlignment = TextAlignment.Center, MaxTextWidth = Math.Max(200, bounds.Width - 80) };
        var cy = bounds.Height / 2 - 20;
        context.DrawText(title, new Point((bounds.Width - title.Width) / 2, cy));
        context.DrawText(sub, new Point((bounds.Width - sub.MaxTextWidth) / 2, cy + 28));
    }

    void RenderGrid(DrawingContext context, Rect bounds)
    {
        double step = 32 * _zoom;
        if (step < 10) step *= 4;
        var dot = B("GraphGridBrush", "#26FFFFFF");
        double startX = _pan.X % step, startY = _pan.Y % step;
        if (startX < 0) startX += step;
        if (startY < 0) startY += step;
        double size = Math.Clamp(1.6 * _zoom, 1, 2);
        for (double x = startX; x < bounds.Width; x += step)
            for (double y = startY; y < bounds.Height; y += step)
                context.FillRectangle(dot, new Rect(x, y, size, size));
    }

    double GetNodeHeight(EventNode node)
    {
        int maxPorts = Math.Max(node.Inputs.Count, node.Outputs.Count);
        return NodeHeaderHeight + Math.Max(1, maxPorts) * PortRowHeight + 14 + (GetBadge(node) is null ? 0 : 22);
    }

    Rect GetNodeRect(EventNode n) => new(n.PosX, n.PosY, NodeWidth, GetNodeHeight(n));

    Point GetPortPosition(EventNode node, EventPort port)
    {
        if (port.Direction == PortDirection.Input)
            return new Point(node.PosX + 12, node.PosY + NodeHeaderHeight + 12 + node.Inputs.IndexOf(port) * PortRowHeight);
        return new Point(node.PosX + NodeWidth - 12, node.PosY + NodeHeaderHeight + 12 + node.Outputs.IndexOf(port) * PortRowHeight);
    }

    /// <summary>Рамка группы в координатах графа (по габаритам её блоков).</summary>
    Rect? GetGroupRect(EventGroup g)
    {
        if (_vm is null) return null;
        var nodes = g.NodeIds.Select(_vm.CurrentGraph.FindNode).OfType<EventNode>().ToList();
        if (nodes.Count == 0) return null;
        double minX = nodes.Min(n => n.PosX) - GroupPadding;
        double minY = nodes.Min(n => n.PosY) - GroupPadding - GroupHeader;
        double maxX = nodes.Max(n => n.PosX + NodeWidth) + GroupPadding;
        double maxY = nodes.Max(n => n.PosY + GetNodeHeight(n)) + GroupPadding;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    Rect? GetGroupHeaderRect(EventGroup g)
    {
        var r = GetGroupRect(g);
        return r is null ? null : new Rect(r.Value.X, r.Value.Y, r.Value.Width, GroupHeader);
    }

    static Color ParseOr(string? hex, Color fallback) => Color.TryParse(hex, out var c) ? c : fallback;

    void RenderGroup(DrawingContext context, EventGroup g)
    {
        var r = GetGroupRect(g);
        if (r is null) return;
        var screen = GraphRectToScreen(r.Value);
        var col = ParseOr(g.Color, Color.Parse("#E05263"));
        bool selected = _vm?.SelectedGroup == g;
        double radius = 10 * _zoom;

        var fill = new SolidColorBrush(Color.FromArgb(28, col.R, col.G, col.B));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(selected ? (byte)255 : (byte)170, col.R, col.G, col.B)), (selected ? 2.4 : 1.4) * Math.Clamp(_zoom, 0.6, 1.4));
        context.DrawRectangle(fill, pen, screen, radius, radius);

        // Заголовок
        var header = new Rect(screen.X, screen.Y, screen.Width, GroupHeader * _zoom);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, col.R, col.G, col.B)), null, header, radius, radius);
        if (_zoom >= 0.35)
        {
            var count = g.NodeIds.Count;
            var title = new FormattedText($"🗂 {g.Title}  ·  {count} бл.", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, BoldFont, 12 * _zoom, B("NodeTextBrush", "#F4EAEE"))
            {
                MaxTextWidth = Math.Max(10, header.Width - 16 * _zoom),
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1
            };
            context.DrawText(title, new Point(header.X + 10 * _zoom, header.Y + (header.Height - title.Height) / 2));
        }
    }

    static string? GetBadge(EventNode node)
    {
        if (node.NodeType == "ui_set_text")
        {
            string target = node.Properties.TryGetValue("TargetName", out var t) && !string.IsNullOrEmpty(t) ? t : "Text";
            string pfx = node.Properties.TryGetValue("Prefix", out var p) ? p : "";
            return string.IsNullOrEmpty(pfx) ? $"🎯 Цель: {target}" : $"🎯 {target} | \"{pfx}\"";
        }
        if (node.NodeType == "event_ui_button_clicked" && node.Properties.TryGetValue("ButtonName", out var btn) && !string.IsNullOrEmpty(btn))
            return $"🔘 Кнопка: {btn}";
        if (node.NodeType == "event_object_clicked" && node.Properties.TryGetValue("ObjectName", out var obj) && !string.IsNullOrEmpty(obj))
            return $"📦 Объект: {obj}";
        if (node.NodeType is "var_get" or "var_set" or "var_increment" or "var_decrement" &&
            node.Properties.TryGetValue("VariableName", out var v) && !string.IsNullOrEmpty(v) && !node.Title.Contains(v))
            return $"🔢 {v}";
        return null;
    }

    void RenderNode(DrawingContext context, EventNode node, bool isSelected, bool isPrimary)
    {
        var screenPos = GraphToScreen(new Point(node.PosX, node.PosY));
        double width = NodeWidth * _zoom;
        double height = GetNodeHeight(node) * _zoom;
        var nodeRect = new Rect(screenPos.X, screenPos.Y, width, height);
        if (!nodeRect.Intersects(new Rect(Bounds.Size).Inflate(40))) return; // вне экрана
        double radius = 8 * _zoom;

        // Тень + корпус
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, nodeRect.Translate(new Vector(0, 3 * _zoom)), radius, radius);
        var borderPen = isSelected
            ? new Pen(B("SelectionBrush", "#FF4B72"), (isPrimary ? 2.6 : 2.0) * Math.Clamp(_zoom, 0.7, 1.4))
            : new Pen(node == _hoverNode ? B("BorderStrongBrush", "#4A3E50") : B("NodeBorderBrush", "#3E3444"), 1.2 * Math.Clamp(_zoom, 0.7, 1.4));
        context.DrawRectangle(B("NodeBodyBrush", "#241E28"), borderPen, nodeRect, radius, radius);

        // Заголовок (цвет категории)
        double headerH = NodeHeaderHeight * _zoom;
        var headerRect = new Rect(screenPos.X, screenPos.Y, width, headerH);
        using (context.PushClip(new RoundedRect(nodeRect.Deflate(isSelected ? 1.5 : 0.6), radius)))
        {
            context.FillRectangle(new SolidColorBrush(GetCategoryColor(node.Category)), headerRect);
        }

        if (_zoom >= 0.35)
        {
            var titleText = new FormattedText(node.Title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, BoldFont, 12 * _zoom, Brushes.White)
            {
                MaxTextWidth = Math.Max(10, width - 20 * _zoom),
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1
            };
            context.DrawText(titleText, new Point(screenPos.X + 10 * _zoom, screenPos.Y + (headerH - titleText.Height) / 2));
        }

        // Порты
        foreach (var port in node.Inputs) RenderPort(context, port, GraphToScreen(GetPortPosition(node, port)), isInput: true);
        foreach (var port in node.Outputs) RenderPort(context, port, GraphToScreen(GetPortPosition(node, port)), isInput: false);

        // Бейдж свойства внизу блока
        var badge = GetBadge(node);
        if (badge is not null && _zoom >= 0.45)
        {
            var info = new FormattedText(badge, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 10 * _zoom, B("InfoBrush", "#7CB4FF"))
            {
                MaxTextWidth = Math.Max(10, width - 20 * _zoom),
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1
            };
            context.DrawText(info, new Point(screenPos.X + 10 * _zoom, screenPos.Y + (GetNodeHeight(node) - 19) * _zoom));
        }
    }

    void RenderPort(DrawingContext context, EventPort port, Point pt, bool isInput)
    {
        bool hovered = port == _hoverPort;
        double r = PinRadius * _zoom * (hovered ? 1.35 : 1.0);
        var pinBrush = new SolidColorBrush(GetPortColor(port.Type));
        var pinPen = new Pen(B("NodeBodyBrush", "#241E28"), 1.5 * _zoom);

        if (port.Type == PortType.Exec)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(pt.X - r * 0.8, pt.Y - r), true);
                ctx.LineTo(new Point(pt.X + r * 1.2, pt.Y));
                ctx.LineTo(new Point(pt.X - r * 0.8, pt.Y + r));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(pinBrush, pinPen, geo);
        }
        else
        {
            context.DrawEllipse(pinBrush, pinPen, pt, r, r);
        }

        if (_zoom < 0.45) return;
        var label = new FormattedText(port.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 10.5 * _zoom, B("PortLabelBrush", "#D2C4CB"))
        {
            MaxTextWidth = Math.Max(10, (NodeWidth / 2 - 22) * _zoom),
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1
        };
        double gap = (PinRadius + 5) * _zoom;
        if (isInput) context.DrawText(label, new Point(pt.X + gap, pt.Y - label.Height / 2));
        else context.DrawText(label, new Point(pt.X - label.Width - gap, pt.Y - label.Height / 2));
    }

    void DrawBezierWire(DrawingContext context, Point p1, Point p2, Color color, double thickness, bool isDashed = false)
    {
        double dx = Math.Max(40 * _zoom, Math.Abs(p2.X - p1.X) * 0.5);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p1, false);
            ctx.CubicBezierTo(new Point(p1.X + dx, p1.Y), new Point(p2.X - dx, p2.Y), p2);
            ctx.EndFigure(false);
        }
        var pen = new Pen(new SolidColorBrush(color), thickness) { LineCap = PenLineCap.Round };
        if (isDashed) pen.DashStyle = DashStyle.Dash;
        context.DrawGeometry(null, pen, geo);
    }

    static Color GetCategoryColor(string category) => category switch
    {
        "События" => Color.Parse("#B45309"),
        "Поток" => Color.Parse("#475569"),
        "Переменные" => Color.Parse("#C2410C"),
        "Математика" => Color.Parse("#0F766E"),
        "Логика" => Color.Parse("#4338CA"),
        "Объекты" => Color.Parse("#6D28D9"),
        "Здоровье" => Color.Parse("#BE123C"),
        "Интерфейс" => Color.Parse("#047857"),
        "Инвентарь" => Color.Parse("#854D0E"),
        "Аудио" => Color.Parse("#A21CAF"),
        _ => Color.Parse("#4B5563")
    };

    Color GetPortColor(PortType type) => type switch
    {
        PortType.Exec => this.ResColor("ExecPinBrush", Colors.White),
        PortType.Boolean => Color.Parse("#EF4444"),
        PortType.Integer => Color.Parse("#10B981"),
        PortType.Float => Color.Parse("#38BDF8"),
        PortType.String => Color.Parse("#EC4899"),
        PortType.Vector3 => Color.Parse("#F59E0B"),
        PortType.Color => Color.Parse("#A855F7"),
        PortType.ObjectRef => Color.Parse("#6366F1"),
        _ => Color.Parse("#9CA3AF")
    };

    // ================== Попадания ==================
    (EventNode? node, EventPort? port) HitTestPort(Point screenPt)
    {
        if (_vm is null) return (null, null);
        double maxDistSq = Math.Pow(Math.Max(10, 13 * _zoom), 2);
        for (int i = _vm.CurrentGraph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = _vm.CurrentGraph.Nodes[i];
            foreach (var port in node.Inputs.Concat(node.Outputs))
            {
                var pt = GraphToScreen(GetPortPosition(node, port));
                double d2 = (pt.X - screenPt.X) * (pt.X - screenPt.X) + (pt.Y - screenPt.Y) * (pt.Y - screenPt.Y);
                if (d2 <= maxDistSq) return (node, port);
            }
        }
        return (null, null);
    }

    EventNode? HitTestNode(Point screenPt)
    {
        if (_vm is null) return null;
        var g = ScreenToGraph(screenPt);
        for (int i = _vm.CurrentGraph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = _vm.CurrentGraph.Nodes[i];
            if (GetNodeRect(node).Contains(g)) return node;
        }
        return null;
    }

    EventGroup? HitTestGroupHeader(Point screenPt)
    {
        if (_vm is null) return null;
        var g = ScreenToGraph(screenPt);
        for (int i = _vm.CurrentGraph.Groups.Count - 1; i >= 0; i--)
        {
            var grp = _vm.CurrentGraph.Groups[i];
            if (GetGroupHeaderRect(grp) is { } r && r.Contains(g)) return grp;
        }
        return null;
    }

    static bool IsAdditive(KeyModifiers m) =>
        m.HasFlag(KeyModifiers.Shift) || m.HasFlag(KeyModifiers.Control) || m.HasFlag(KeyModifiers.Meta);

    // ================== Мышь ==================
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        _pressPoint = pos;
        _lastPointer = pos;
        _movedSincePress = false;
        var props = e.GetCurrentPoint(this).Properties;

        if (_vm is null) return;

        // Панорама: средняя / правая кнопка или Пробел + ЛКМ
        if (props.IsMiddleButtonPressed || props.IsRightButtonPressed || (props.IsLeftButtonPressed && _spaceHeld))
        {
            _mode = DragMode.Pan;
            _rightButtonPan = props.IsRightButtonPressed;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
            return;
        }
        if (!props.IsLeftButtonPressed) return;

        // Двойной клик по пустому месту — каталог блоков в этой точке
        if (e.ClickCount == 2 && HitTestNode(pos) is null && HitTestPort(pos).node is null)
        {
            var gp = ScreenToGraph(pos);
            RequestNodePalette?.Invoke(gp.X, gp.Y);
            e.Handled = true;
            return;
        }

        bool additive = IsAdditive(e.KeyModifiers);

        // 1. Порт — тянем провод
        var (hitNode, hitPort) = HitTestPort(pos);
        if (hitNode is not null && hitPort is not null)
        {
            _detachedWire = false;
            if (hitPort.Direction == PortDirection.Input)
            {
                var existing = _vm.CurrentGraph.GetConnectedSource(hitNode.Id, hitPort.Id);
                if (existing is not null)
                {
                    var srcNode = _vm.CurrentGraph.FindNode(existing.FromNodeId);
                    var srcPort = srcNode?.FindPort(existing.FromPortId);
                    _vm.CurrentGraph.Disconnect(existing.FromNodeId, existing.FromPortId, existing.ToNodeId, existing.ToPortId);
                    if (srcNode is not null && srcPort is not null)
                    {
                        _detachedWire = true;
                        BeginWire(srcNode, srcPort, pos, e);
                        return;
                    }
                }
            }
            BeginWire(hitNode, hitPort, pos, e);
            return;
        }

        // 2. Блок
        var clicked = HitTestNode(pos);
        if (clicked is not null)
        {
            if (additive) _vm.ToggleNodeSelection(clicked);
            else if (!_vm.IsNodeSelected(clicked)) _vm.SelectOnly(clicked);
            else if (_vm.SelectedNode != clicked) _vm.SetSelection(_vm.SelectedNodes.ToList(), clicked);

            if (_vm.IsNodeSelected(clicked))
            {
                _vm.LeftSidebarTabIndex = 0;
                _dragNodes = _vm.SelectedNodes.ToList();
                _mode = DragMode.Nodes;
                e.Pointer.Capture(this);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // 3. Заголовок группы — выбрать группу и тянуть её целиком
        var group = HitTestGroupHeader(pos);
        if (group is not null)
        {
            _vm.SelectGroup(group, additive);
            _dragNodes = _vm.SelectedNodes.ToList();
            _mode = DragMode.Nodes;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // 4. Пустое место — рамка выделения
        _mode = DragMode.Marquee;
        _marqueeAdditive = additive;
        _marqueeBase = additive ? _vm.SelectedNodes.ToList() : [];
        _marquee = new Rect(pos, pos);
        if (!additive) _vm.ClearSelection();
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    void BeginWire(EventNode node, EventPort port, Point pos, PointerPressedEventArgs e)
    {
        _mode = DragMode.Wire;
        _wireStartNode = node;
        _wireStartPort = port;
        _currentWireEnd = pos;
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var delta = pos - _lastPointer;
        if (Math.Abs(pos.X - _pressPoint.X) > 3 || Math.Abs(pos.Y - _pressPoint.Y) > 3) _movedSincePress = true;

        switch (_mode)
        {
            case DragMode.Pan:
                _pan += delta;
                _lastPointer = pos;
                InvalidateVisual();
                return;

            case DragMode.Nodes:
                if (!_movedSincePress) return;
                foreach (var n in _dragNodes)
                {
                    n.PosX += delta.X / _zoom;
                    n.PosY += delta.Y / _zoom;
                }
                _lastPointer = pos;
                _vm?.Editor.MarkDirty();
                InvalidateVisual();
                return;

            case DragMode.Marquee:
                _marquee = new Rect(new Point(Math.Min(_pressPoint.X, pos.X), Math.Min(_pressPoint.Y, pos.Y)),
                                    new Point(Math.Max(_pressPoint.X, pos.X), Math.Max(_pressPoint.Y, pos.Y)));
                UpdateMarqueeSelection();
                _lastPointer = pos;
                return;

            case DragMode.Wire:
                _currentWireEnd = pos;
                var (_, port) = HitTestPort(pos);
                _hoverPort = port;
                InvalidateVisual();
                return;
        }

        // Наведение без кнопок — подсветка блока / порта и курсор
        var (hn, hp) = HitTestPort(pos);
        var node = hp is null ? HitTestNode(pos) : hn;
        if (node != _hoverNode || hp != _hoverPort)
        {
            _hoverNode = node;
            _hoverPort = hp;
            Cursor = hp is not null ? new Cursor(StandardCursorType.Cross)
                : node is not null || HitTestGroupHeader(pos) is not null ? new Cursor(StandardCursorType.Hand)
                : Cursor.Default;
            InvalidateVisual();
        }
    }

    void UpdateMarqueeSelection()
    {
        if (_vm is null) return;
        var gRect = new Rect(ScreenToGraph(_marquee.TopLeft), ScreenToGraph(_marquee.BottomRight));
        var hit = _vm.CurrentGraph.Nodes.Where(n => GetNodeRect(n).Intersects(gRect)).ToList();
        var result = _marqueeAdditive ? _marqueeBase.Union(hit).ToList() : hit;
        _vm.SetSelection(result);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);

        if (_mode == DragMode.Wire && _wireStartNode is not null && _wireStartPort is not null && _vm is not null)
        {
            var (hitNode, hitPort) = HitTestPort(pos);
            bool connected = false;
            if (hitNode is not null && hitPort is not null && hitNode != _wireStartNode)
            {
                connected = _vm.CurrentGraph.Connect(_wireStartNode.Id, _wireStartPort.Id, hitNode.Id, hitPort.Id);
                if (!connected && hitPort.Direction != _wireStartPort.Direction &&
                    !_vm.CurrentGraph.CanConnect(_wireStartNode, _wireStartPort, hitNode, hitPort, out var reason))
                {
                    _vm.Editor.StatusText = "⚠️ " + reason;
                }
            }
            if (connected || _detachedWire)
            {
                _vm.Editor.MarkDirty();
                _vm.NotifyGraphChanged();
            }
        }
        else if (_mode == DragMode.Pan && _rightButtonPan && !_movedSincePress)
        {
            // Правый клик без перемещения — контекстное меню
            ShowContextMenu(pos);
        }
        else if (_mode == DragMode.Nodes && !_movedSincePress && _vm is not null &&
                 !IsAdditive(e.KeyModifiers) && HitTestNode(pos) is { } clicked && _vm.SelectedNodes.Count > 1 && HitTestGroupHeader(pos) is null)
        {
            // Клик по одному из нескольких выделенных блоков без перетаскивания — оставить только его
            _vm.SelectOnly(clicked);
        }

        _mode = DragMode.None;
        _rightButtonPan = false;
        _detachedWire = false;
        _dragNodes = [];
        _wireStartNode = null;
        _wireStartPort = null;
        _marquee = default;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var cursor = e.GetPosition(this);
        // Ctrl/⌘ + колесо или обычное колесо — масштаб; Shift + колесо — горизонтальная прокрутка
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _pan += new Vector(e.Delta.Y * 40, 0);
            InvalidateVisual();
        }
        else
        {
            SetZoom(_zoom * (e.Delta.Y > 0 ? 1.12 : 1 / 1.12), cursor);
        }
        e.Handled = true;
    }

    // ================== Контекстное меню ==================
    void ShowContextMenu(Point pos)
    {
        if (_vm is null) return;
        var vm = _vm;
        var items = new List<object>();
        var node = HitTestNode(pos);
        var group = node is null ? HitTestGroupHeader(pos) : null;

        MenuItem Item(string header, Action action, KeyGesture? gesture = null, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            if (gesture is not null) mi.InputGesture = gesture;
            mi.Click += (_, _) => { action(); InvalidateVisual(); };
            return mi;
        }

        if (node is not null)
        {
            if (!vm.IsNodeSelected(node)) vm.SelectOnly(node);
            var n = vm.SelectedNodes.Count;
            items.Add(Item(n > 1 ? $"📋 Копировать ({n})" : "📋 Копировать", vm.CopyNodes, Hk.Copy));
            items.Add(Item("✂️ Вырезать", vm.CutNodes, Hk.Cut));
            items.Add(Item("⧉ Дублировать", vm.DuplicateNodes, Hk.Duplicate));
            items.Add(new Separator());
            items.Add(Item("🗂 Сгруппировать", vm.GroupSelection, Hk.Group));
            items.Add(Item("Расформировать группу", vm.UngroupSelection, Hk.Ungroup,
                vm.SelectedNodes.Any(x => vm.CurrentGraph.FindGroupOfNode(x.Id) is not null)));
            items.Add(new Separator());
            items.Add(Item(n > 1 ? $"🗑 Удалить блоки ({n})" : "🗑 Удалить блок", vm.DeleteSelectedNodes, Hk.Delete));
        }
        else if (group is not null)
        {
            vm.SelectGroup(group);
            items.Add(Item("✏️ Переименовать / цвет…", () => vm.LeftSidebarTabIndex = 0));
            items.Add(Item("📋 Копировать группу", vm.CopyNodes, Hk.Copy));
            items.Add(Item("⧉ Дублировать группу", vm.DuplicateNodes, Hk.Duplicate));
            items.Add(new Separator());
            items.Add(Item("Расформировать (оставить блоки)", vm.UngroupSelection, Hk.Ungroup));
            items.Add(Item("🗑 Удалить группу вместе с блоками", vm.DeleteGroupWithNodes));
        }
        else
        {
            var gp = ScreenToGraph(pos);
            items.Add(Item("➕ Добавить блок здесь…", () => RequestNodePalette?.Invoke(gp.X, gp.Y)));
            items.Add(Item("📌 Вставить здесь", () => vm.PasteNodes(gp.X, gp.Y), Hk.Paste, vm.CanPaste));
            items.Add(new Separator());
            items.Add(Item("Выделить всё", vm.SelectAllNodes, Hk.SelectAll, vm.CurrentGraph.Nodes.Count > 0));
            items.Add(Item("🎯 Показать все блоки", CenterOnNodes, Hk.Focus));
            items.Add(Item("Масштаб 100%", ZoomReset));
        }

        var menu = new ContextMenu { ItemsSource = items, Placement = PlacementMode.Pointer };
        menu.Open(this);
    }

    // ================== Клавиатура ==================
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_vm is null || e.Handled) return;
        if (HandleGraphKey(_vm, e, this)) e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space) _spaceHeld = false;
    }

    /// <summary>
    /// Горячие клавиши редактора событий. Вызывается и полотном, и окном,
    /// когда фокус находится в боковой панели редактора событий.
    /// </summary>
    public static bool HandleGraphKey(EventGraphVM vm, KeyEventArgs e, EventGraphCanvas? canvas)
    {
        if (Hk.IsDelete(e)) { vm.DeleteSelectedNodes(); return true; }
        if (Hk.Cmd(e, Key.A)) { vm.SelectAllNodes(); return true; }
        if (Hk.Cmd(e, Key.C)) { vm.CopyNodes(); return true; }
        if (Hk.Cmd(e, Key.X)) { vm.CutNodes(); return true; }
        if (Hk.Cmd(e, Key.V))
        {
            if (canvas is not null && canvas.IsPointerOver && canvas._lastPointer != default)
            {
                var gp = canvas.ScreenToGraph(canvas._lastPointer);
                vm.PasteNodes(gp.X, gp.Y);
            }
            else vm.PasteNodes();
            return true;
        }
        if (Hk.Cmd(e, Key.D)) { vm.DuplicateNodes(); return true; }
        if (Hk.Cmd(e, Key.G)) { vm.GroupSelection(); return true; }
        if (Hk.CmdShift(e, Key.G)) { vm.UngroupSelection(); return true; }
        if (Hk.Plain(e, Key.Escape)) { vm.ClearSelection(); return true; }

        if (canvas is null) return false;
        if (Hk.Plain(e, Key.Space)) { canvas._spaceHeld = true; return true; }
        if (Hk.Plain(e, Key.F) || Hk.Plain(e, Key.Home)) { canvas.FitToNodes(onlySelection: false); return true; }
        if (Hk.Is(e, Key.F, KeyModifiers.Shift)) { canvas.FitToNodes(onlySelection: true); return true; }
        if (Hk.Cmd(e, Key.D0) || Hk.Cmd(e, Key.NumPad0)) { canvas.ZoomReset(); return true; }
        if (Hk.Cmd(e, Key.OemPlus) || Hk.Cmd(e, Key.Add) || Hk.CmdShift(e, Key.OemPlus)) { canvas.ZoomIn(); return true; }
        if (Hk.Cmd(e, Key.OemMinus) || Hk.Cmd(e, Key.Subtract)) { canvas.ZoomOut(); return true; }

        // Стрелки — сдвиг выделенных блоков (Shift — крупный шаг)
        if (vm.SelectedNodes.Count > 0 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var m = Hk.Mods(e);
            if (m != KeyModifiers.None && m != KeyModifiers.Shift) return false;
            double step = m == KeyModifiers.Shift ? 40 : 8;
            double dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            double dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            vm.NudgeSelection(dx, dy);
            return true;
        }
        return false;
    }
}
