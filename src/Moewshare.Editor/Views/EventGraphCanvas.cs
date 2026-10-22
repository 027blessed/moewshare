using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine.Events;

namespace Moewshare.Editor.Views;

public sealed class EventGraphCanvas : Control
{
    EventGraphVM? _vm;

    // View transform
    Vector _pan = new(100, 80);
    double _zoom = 1.0;

    // Interaction states
    bool _isPanning;
    Point _lastPointer;

    EventNode? _draggingNode;
    Point _dragNodeStartPos;

    bool _isDraggingWire;
    EventNode? _wireStartNode;
    EventPort? _wireStartPort;
    Point _currentWireEnd;

    // Geometry layout constants
    const double NodeWidth = 200;
    const double NodeHeaderHeight = 32;
    const double PortRowHeight = 24;
    const double PinRadius = 6;

    static readonly Typeface Font = new("Inter,Segoe UI,-apple-system,sans-serif");
    static readonly Typeface BoldFont = new("Inter,Segoe UI,-apple-system,sans-serif", FontStyle.Normal, FontWeight.Bold);

    public EventGraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public void Attach(EventGraphVM vm)
    {
        _vm = vm;
        vm.GraphChanged += InvalidateVisual;
        InvalidateVisual();
    }

    Point GraphToScreen(Point p) => new(p.X * _zoom + _pan.X, p.Y * _zoom + _pan.Y);
    Point ScreenToGraph(Point p) => new((p.X - _pan.X) / _zoom, (p.Y - _pan.Y) / _zoom);

    public void ResetView()
    {
        _pan = new(80, 80);
        _zoom = 1.0;
        if (_vm is not null) _vm.ZoomLevel = _zoom;
        InvalidateVisual();
    }

    public void CenterOnNodes()
    {
        if (_vm is null || _vm.CurrentGraph.Nodes.Count == 0)
        {
            ResetView();
            return;
        }

        double minX = _vm.CurrentGraph.Nodes.Min(n => n.PosX);
        double minY = _vm.CurrentGraph.Nodes.Min(n => n.PosY);
        double maxX = _vm.CurrentGraph.Nodes.Max(n => n.PosX + NodeWidth);
        double maxY = _vm.CurrentGraph.Nodes.Max(n => n.PosY + GetNodeHeight(n));

        double cx = (minX + maxX) / 2;
        double cy = (minY + maxY) / 2;

        _zoom = 1.0;
        _pan = new Vector(Bounds.Width / 2 - cx * _zoom, Bounds.Height / 2 - cy * _zoom);
        if (_vm is not null) _vm.ZoomLevel = _zoom;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // 1. Background
        var bgBrush = new SolidColorBrush(Color.Parse("#1A1D24"));
        context.FillRectangle(bgBrush, bounds);

        // 2. Subtle Grid
        RenderGrid(context, bounds);

        if (_vm is null) return;
        var graph = _vm.CurrentGraph;

        // 3. Render Connections (Bézier Wires)
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

            var wireColor = GetPortColor(fromPort.Type);
            DrawBezierWire(context, p1, p2, wireColor, 2.5 * Math.Clamp(_zoom, 0.7, 1.4));
        }

        // 4. Render Active Dragging Wire
        if (_isDraggingWire && _wireStartNode is not null && _wireStartPort is not null)
        {
            var p1 = GraphToScreen(GetPortPosition(_wireStartNode, _wireStartPort));
            var wireColor = GetPortColor(_wireStartPort.Type);
            DrawBezierWire(context, p1, _currentWireEnd, wireColor, 3.0 * Math.Clamp(_zoom, 0.7, 1.4), isDashed: true);
        }

        // 5. Render Nodes
        foreach (var node in graph.Nodes)
        {
            RenderNode(context, node, isSelected: node == _vm.SelectedNode);
        }
    }

    void RenderGrid(DrawingContext context, Rect bounds)
    {
        double gridStep = 32 * _zoom;
        if (gridStep < 8) return;

        var dotPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1.5);
        double startX = (_pan.X % gridStep);
        double startY = (_pan.Y % gridStep);

        for (double x = startX; x < bounds.Width; x += gridStep)
        {
            for (double y = startY; y < bounds.Height; y += gridStep)
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)), null, new Rect(x, y, 1.5, 1.5));
            }
        }
    }

    double GetNodeHeight(EventNode node)
    {
        int maxPorts = Math.Max(node.Inputs.Count, node.Outputs.Count);
        double extra = 0;
        if (node.NodeType == "ui_set_text") extra = 22;
        else if (node.NodeType == "event_ui_button_clicked" && node.Properties.ContainsKey("ButtonName") && !string.IsNullOrEmpty(node.Properties["ButtonName"])) extra = 20;
        else if (node.NodeType == "event_object_clicked" && node.Properties.ContainsKey("ObjectName") && !string.IsNullOrEmpty(node.Properties["ObjectName"])) extra = 20;
        return NodeHeaderHeight + Math.Max(1, maxPorts) * PortRowHeight + 14 + extra;
    }

    Point GetPortPosition(EventNode node, EventPort port)
    {
        double h = GetNodeHeight(node);
        if (port.Direction == PortDirection.Input)
        {
            int idx = node.Inputs.IndexOf(port);
            return new Point(node.PosX + 10, node.PosY + NodeHeaderHeight + 12 + idx * PortRowHeight);
        }
        else
        {
            int idx = node.Outputs.IndexOf(port);
            return new Point(node.PosX + NodeWidth - 10, node.PosY + NodeHeaderHeight + 12 + idx * PortRowHeight);
        }
    }

    void RenderNode(DrawingContext context, EventNode node, bool isSelected)
    {
        var screenPos = GraphToScreen(new Point(node.PosX, node.PosY));
        double width = NodeWidth * _zoom;
        double height = GetNodeHeight(node) * _zoom;
        var nodeRect = new Rect(screenPos.X, screenPos.Y, width, height);

        // Node Body Background
        var bodyBrush = new SolidColorBrush(Color.Parse("#222631"));
        var borderBrush = isSelected
            ? new SolidColorBrush(Color.Parse("#F59E0B")) // Gold outline when selected
            : new SolidColorBrush(Color.Parse("#374151")); // Subtle gray border
        var borderPen = new Pen(borderBrush, (isSelected ? 2.5 : 1.2) * _zoom);

        context.DrawRectangle(bodyBrush, borderPen, nodeRect, 8 * _zoom, 8 * _zoom);

        // Header Background
        double headerH = NodeHeaderHeight * _zoom;
        var headerRect = new Rect(screenPos.X, screenPos.Y, width, headerH);
        var catColor = GetCategoryColor(node.Category);
        var headerBrush = new SolidColorBrush(catColor);
        context.DrawRectangle(headerBrush, null, headerRect, 8 * _zoom, 8 * _zoom);

        // Header Title Text
        var titleText = new FormattedText(
            node.Title,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            BoldFont,
            12 * _zoom,
            Brushes.White
        );
        context.DrawText(titleText, new Point(screenPos.X + 10 * _zoom, screenPos.Y + (headerH - titleText.Height) / 2));

        // Render Input Ports
        for (int i = 0; i < node.Inputs.Count; i++)
        {
            var port = node.Inputs[i];
            var pt = GraphToScreen(GetPortPosition(node, port));
            RenderPort(context, port, pt, isInput: true);
        }

        // Render Output Ports
        for (int i = 0; i < node.Outputs.Count; i++)
        {
            var port = node.Outputs[i];
            var pt = GraphToScreen(GetPortPosition(node, port));
            RenderPort(context, port, pt, isInput: false);
        }

        // Render Property Badges at bottom of node
        if (node.NodeType == "ui_set_text")
        {
            string target = node.Properties.TryGetValue("TargetName", out var t) && !string.IsNullOrEmpty(t) ? t : "Text";
            string pfx = node.Properties.TryGetValue("Prefix", out var p) ? p : "";
            string desc = string.IsNullOrEmpty(pfx) ? $"🎯 Цель: {target}" : $"🎯 {target} | \"{pfx}\"";
            var infoText = new FormattedText(
                desc,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Font,
                10 * _zoom,
                new SolidColorBrush(Color.Parse("#93C5FD"))
            );
            double infoY = screenPos.Y + (GetNodeHeight(node) - 18) * _zoom;
            context.DrawText(infoText, new Point(screenPos.X + 10 * _zoom, infoY));
        }
        else if (node.NodeType == "event_ui_button_clicked" && node.Properties.TryGetValue("ButtonName", out var btn) && !string.IsNullOrEmpty(btn))
        {
            var infoText = new FormattedText(
                $"🔘 Кнопка: {btn}",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Font,
                10 * _zoom,
                new SolidColorBrush(Color.Parse("#FCD34D"))
            );
            double infoY = screenPos.Y + (GetNodeHeight(node) - 18) * _zoom;
            context.DrawText(infoText, new Point(screenPos.X + 10 * _zoom, infoY));
        }
        else if (node.NodeType == "event_object_clicked" && node.Properties.TryGetValue("ObjectName", out var obj) && !string.IsNullOrEmpty(obj))
        {
            var infoText = new FormattedText(
                $"📦 Объект: {obj}",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Font,
                10 * _zoom,
                new SolidColorBrush(Color.Parse("#A7F3D0"))
            );
            double infoY = screenPos.Y + (GetNodeHeight(node) - 18) * _zoom;
            context.DrawText(infoText, new Point(screenPos.X + 10 * _zoom, infoY));
        }
    }

    void RenderPort(DrawingContext context, EventPort port, Point pt, bool isInput)
    {
        double r = PinRadius * _zoom;
        var col = GetPortColor(port.Type);
        var pinBrush = new SolidColorBrush(col);
        var pinPen = new Pen(Brushes.White, 1 * _zoom);

        if (port.Type == PortType.Exec)
        {
            // Triangle pin for Exec
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
            // Circle pin for Data
            context.DrawEllipse(pinBrush, pinPen, pt, r, r);
        }

        // Port Label Text
        var label = new FormattedText(
            port.Name,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Font,
            10.5 * _zoom,
            new SolidColorBrush(Color.Parse("#E5E7EB"))
        );

        if (isInput)
        {
            context.DrawText(label, new Point(pt.X + (r + 4) * _zoom, pt.Y - label.Height / 2));
        }
        else
        {
            context.DrawText(label, new Point(pt.X - label.Width - (r + 4) * _zoom, pt.Y - label.Height / 2));
        }
    }

    void DrawBezierWire(DrawingContext context, Point p1, Point p2, Color color, double thickness, bool isDashed = false)
    {
        double dx = Math.Max(40 * _zoom, Math.Abs(p2.X - p1.X) * 0.5);
        var c1 = new Point(p1.X + dx, p1.Y);
        var c2 = new Point(p2.X - dx, p2.Y);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p1, false);
            ctx.CubicBezierTo(c1, c2, p2);
            ctx.EndFigure(false);
        }

        var pen = new Pen(new SolidColorBrush(color), thickness);
        if (isDashed)
        {
            pen.DashStyle = DashStyle.Dash;
        }
        context.DrawGeometry(null, pen, geo);
    }

    static Color GetCategoryColor(string category) => category switch
    {
        "События" => Color.Parse("#B45309"),    // Amber / Bronze
        "Поток" => Color.Parse("#334155"),      // Slate Gray
        "Переменные" => Color.Parse("#C2410C"), // Orange
        "Математика" => Color.Parse("#0F766E"), // Teal
        "Логика" => Color.Parse("#4338CA"),     // Indigo
        "Объекты" => Color.Parse("#6D28D9"),    // Purple
        "Здоровье" => Color.Parse("#BE123C"),   // Crimson
        "Интерфейс" => Color.Parse("#047857"),  // Emerald
        "Инвентарь" => Color.Parse("#854D0E"),  // Ochre
        "Аудио" => Color.Parse("#A21CAF"),      // Magenta
        _ => Color.Parse("#4B5563")             // Default Gray
    };

    static Color GetPortColor(PortType type) => type switch
    {
        PortType.Exec => Colors.White,
        PortType.Boolean => Color.Parse("#EF4444"),    // Red
        PortType.Integer => Color.Parse("#10B981"),    // Green
        PortType.Float => Color.Parse("#38BDF8"),      // Cyan
        PortType.String => Color.Parse("#EC4899"),     // Pink
        PortType.Vector3 => Color.Parse("#F59E0B"),    // Amber
        PortType.Color => Color.Parse("#A855F7"),      // Purple
        PortType.ObjectRef => Color.Parse("#6366F1"),  // Indigo
        _ => Color.Parse("#9CA3AF")                    // Gray
    };

    // --- Interactive Hit Testing & Event Handlers ---

    (EventNode? node, EventPort? port) HitTestPort(Point screenPt)
    {
        if (_vm is null) return (null, null);
        double maxDistSq = 14 * 14 * _zoom * _zoom;

        foreach (var node in _vm.CurrentGraph.Nodes)
        {
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
        var graphPt = ScreenToGraph(screenPt);

        // Check in reverse order so top-most node is selected
        for (int i = _vm.CurrentGraph.Nodes.Count - 1; i >= 0; i--)
        {
            var node = _vm.CurrentGraph.Nodes[i];
            double w = NodeWidth;
            double h = GetNodeHeight(node);
            if (graphPt.X >= node.PosX && graphPt.X <= node.PosX + w &&
                graphPt.Y >= node.PosY && graphPt.Y <= node.PosY + h)
            {
                return node;
            }
        }
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var pos = e.GetPosition(this);
        _lastPointer = pos;

        var prop = e.GetCurrentPoint(this).Properties;

        // Middle Click or Right Click starts Panning
        if (prop.IsMiddleButtonPressed || prop.IsRightButtonPressed)
        {
            _isPanning = true;
            e.Pointer.Capture(this);
            return;
        }

        if (prop.IsLeftButtonPressed && _vm is not null)
        {
            // 1. Check if clicking on a port
            var (hitNode, hitPort) = HitTestPort(pos);
            if (hitNode is not null && hitPort is not null)
            {
                if (hitPort.Direction == PortDirection.Input)
                {
                    // If input already connected, disconnect and pick up existing wire
                    var existingConn = _vm.CurrentGraph.GetConnectedSource(hitNode.Id, hitPort.Id);
                    if (existingConn is not null)
                    {
                        var srcNode = _vm.CurrentGraph.FindNode(existingConn.FromNodeId);
                        var srcPort = srcNode?.FindPort(existingConn.FromPortId);
                        _vm.CurrentGraph.DisconnectPort(hitNode.Id, hitPort.Id);
                        if (srcNode is not null && srcPort is not null)
                        {
                            _isDraggingWire = true;
                            _wireStartNode = srcNode;
                            _wireStartPort = srcPort;
                            _currentWireEnd = pos;
                            e.Pointer.Capture(this);
                            InvalidateVisual();
                            return;
                        }
                    }
                }

                // Start new wire drag
                _isDraggingWire = true;
                _wireStartNode = hitNode;
                _wireStartPort = hitPort;
                _currentWireEnd = pos;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }

            // 2. Check if clicking on a node
            var clickedNode = HitTestNode(pos);
            if (clickedNode is not null)
            {
                _vm.SelectedNode = clickedNode;
                _vm.LeftSidebarTabIndex = 0;
                _draggingNode = clickedNode;
                _dragNodeStartPos = new Point(clickedNode.PosX, clickedNode.PosY);
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }

            // 3. Clicked empty space: deselect and start pan
            _vm.SelectedNode = null;
            _isPanning = true;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var delta = pos - _lastPointer;

        if (_isPanning)
        {
            _pan += delta;
            _lastPointer = pos;
            InvalidateVisual();
            return;
        }

        if (_draggingNode is not null)
        {
            _draggingNode.PosX += delta.X / _zoom;
            _draggingNode.PosY += delta.Y / _zoom;
            _lastPointer = pos;
            if (_vm is not null) _vm.Editor.IsDirty = true;
            InvalidateVisual();
            return;
        }

        if (_isDraggingWire)
        {
            _currentWireEnd = pos;
            InvalidateVisual();
            return;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);

        if (_isDraggingWire && _wireStartNode is not null && _wireStartPort is not null && _vm is not null)
        {
            var (hitNode, hitPort) = HitTestPort(pos);
            if (hitNode is not null && hitPort is not null && hitNode != _wireStartNode)
            {
                // Connect if valid!
                bool connected = _vm.CurrentGraph.Connect(_wireStartNode.Id, _wireStartPort.Id, hitNode.Id, hitPort.Id);
                if (connected)
                {
                    _vm.Editor.IsDirty = true;
                    _vm.NotifyGraphChanged();
                }
            }
        }

        _isPanning = false;
        _draggingNode = null;
        _isDraggingWire = false;
        _wireStartNode = null;
        _wireStartPort = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var cursor = e.GetPosition(this);
        double factor = e.Delta.Y > 0 ? 1.12 : 0.89;

        double oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * factor, 0.25, 2.5);
        if (_vm is not null) _vm.ZoomLevel = _zoom;

        // Keep position under cursor stationary
        _pan = cursor - (cursor - _pan) * (_zoom / oldZoom);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Delete or Key.Back)
        {
            _vm?.DeleteSelectedNode();
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.F || e.Key == Key.Home)
        {
            CenterOnNodes();
            e.Handled = true;
        }
    }
}
