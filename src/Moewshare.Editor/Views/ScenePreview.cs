using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine;

namespace Moewshare.Editor.Views;

public sealed class ScenePreview : Control
{
    EditorViewModel? _vm;

    // 3D Camera state
    Vec3 _camPos = new(0, 5, -14);
    double _camYaw = 0;    // degrees
    double _camPitch = -18; // degrees (negative looks slightly down towards scene center)

    // Interaction states
    bool _isFlying;
    bool _isOrbiting;
    bool _isPanning;
    bool _isDraggingUi;
    Point _dragStartUiPos;
    Point _lastPointerPos;

    // Active gizmo drag
    string? _activeGizmoAxis; // "X", "Y", "Z", "XYZ"
    Point _dragStartPointer;
    Vec3 _dragStartPos = new();
    Vec3 _dragStartRot = new();
    Vec3 _dragStartScale = new();
    readonly Dictionary<ObjectVM, Vec3> _dragStartMultiPos = new();

    // Flight keyboard state
    readonly HashSet<Key> _heldKeys = [];
    readonly DispatcherTimer _flightTimer;

    // Texture image cache
    static readonly Dictionary<string, Bitmap> _textureCache = new();

    public Vec3 GetWorldPosition(ObjectVM vm)
    {
        var p = vm.Model.Position;
        var res = new Vec3(p.X, p.Y, p.Z);
        var cur = vm;
        int depth = 0;
        while (!string.IsNullOrEmpty(cur.ParentId) && depth++ < 10)
        {
            var parent = _vm?.Objects.FirstOrDefault(o => o.Id == cur.ParentId);
            if (parent is null || parent == cur) break;
            res.X += parent.PosX;
            res.Y += parent.PosY;
            res.Z += parent.PosZ;
            cur = parent;
        }
        return res;
    }

    public ScenePreview()
    {
        ClipToBounds = true;
        Focusable = true;

        _flightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _flightTimer.Tick += OnFlightTick;
    }

    public void Attach(EditorViewModel vm)
    {
        _vm = vm;
        vm.SceneChanged += InvalidateVisual;
        vm.RequestFocus += FocusOnSelected;
    }

    public void FocusOnSelected()
    {
        if (_vm?.Selected is not null)
        {
            var p = GetWorldPosition(_vm.Selected);
            var radYaw = _camYaw * Math.PI / 180.0;
            var radPitch = _camPitch * Math.PI / 180.0;
            var dist = Math.Max(6, Math.Max(_vm.Selected.Model.Scale.X, Math.Max(_vm.Selected.Model.Scale.Y, _vm.Selected.Model.Scale.Z)) * 3.5);
            
            var fx = Math.Sin(radYaw) * Math.Cos(radPitch);
            var fy = Math.Sin(radPitch);
            var fz = Math.Cos(radYaw) * Math.Cos(radPitch);

            _camPos = new Vec3(p.X - fx * dist, p.Y - fy * dist, p.Z - fz * dist);
            InvalidateVisual();
        }
    }

    public void SetViewPreset(string preset)
    {
        switch (preset.ToLowerInvariant())
        {
            case "top":
                _camYaw = 0; _camPitch = -89.9;
                _camPos = new Vec3(0, 20, 0);
                break;
            case "isometric":
            case "iso":
                _camYaw = 0; _camPitch = -45;
                _camPos = new Vec3(0, 16, -16);
                break;
            case "front":
                _camYaw = 0; _camPitch = 0;
                _camPos = new Vec3(0, 2, -18);
                break;
            case "side":
                _camYaw = 90; _camPitch = 0;
                _camPos = new Vec3(-18, 2, 0);
                break;
            default: // perspective
                _camYaw = 25; _camPitch = -20;
                _camPos = new Vec3(-8, 7, -14);
                break;
        }
        InvalidateVisual();
    }

    void OnFlightTick(object? sender, EventArgs e)
    {
        if (!_isFlying || _vm is null) return;

        double speed = _heldKeys.Contains(Key.LeftShift) || _heldKeys.Contains(Key.RightShift) ? 0.45 : 0.18;
        var (fwd, right, up) = GetCameraVectors();

        var move = new Vec3();
        if (_heldKeys.Contains(Key.W) || _heldKeys.Contains(Key.Up))
        {
            move.X += fwd.X * speed; move.Y += fwd.Y * speed; move.Z += fwd.Z * speed;
        }
        if (_heldKeys.Contains(Key.S) || _heldKeys.Contains(Key.Down))
        {
            move.X -= fwd.X * speed; move.Y -= fwd.Y * speed; move.Z -= fwd.Z * speed;
        }
        if (_heldKeys.Contains(Key.A) || _heldKeys.Contains(Key.Left))
        {
            move.X -= right.X * speed; move.Y -= right.Y * speed; move.Z -= right.Z * speed;
        }
        if (_heldKeys.Contains(Key.D) || _heldKeys.Contains(Key.Right))
        {
            move.X += right.X * speed; move.Y += right.Y * speed; move.Z += right.Z * speed;
        }
        if (_heldKeys.Contains(Key.E)) move.Y += speed;
        if (_heldKeys.Contains(Key.Q)) move.Y -= speed;

        if (Math.Abs(move.X) > 0.0001 || Math.Abs(move.Y) > 0.0001 || Math.Abs(move.Z) > 0.0001)
        {
            _camPos.X += move.X;
            _camPos.Y += move.Y;
            _camPos.Z += move.Z;
            InvalidateVisual();
        }
    }

    (Vec3 fwd, Vec3 right, Vec3 up) GetCameraVectors()
    {
        var radYaw = _camYaw * Math.PI / 180.0;
        var radPitch = _camPitch * Math.PI / 180.0;

        var fwd = new Vec3(
            Math.Sin(radYaw) * Math.Cos(radPitch),
            Math.Sin(radPitch),
            Math.Cos(radYaw) * Math.Cos(radPitch)
        );

        var right = new Vec3(
            Math.Cos(radYaw),
            0,
            -Math.Sin(radYaw)
        );

        // up = fwd x right (points upwards for standard orientation)
        var up = new Vec3(
            fwd.Y * right.Z - fwd.Z * right.Y,
            fwd.Z * right.X - fwd.X * right.Z,
            fwd.X * right.Y - fwd.Y * right.X
        );

        return (fwd, right, up);
    }

    Point? Project3D(Vec3 world)
    {
        var (fwd, right, up) = GetCameraVectors();
        var dx = world.X - _camPos.X;
        var dy = world.Y - _camPos.Y;
        var dz = world.Z - _camPos.Z;

        var xc = dx * right.X + dy * right.Y + dz * right.Z;
        var yc = dx * up.X + dy * up.Y + dz * up.Z;
        var zc = dx * fwd.X + dy * fwd.Y + dz * fwd.Z;

        if (zc <= 0.15) return null; // Behind camera

        var f = Math.Max(Bounds.Height, Bounds.Width) * 0.9;
        var sx = Bounds.Width / 2.0 + (xc * f) / zc;
        var sy = Bounds.Height / 2.0 - (yc * f) / zc;

        return new Point(sx, sy);
    }

    double GetDepth(Vec3 world)
    {
        var (fwd, _, _) = GetCameraVectors();
        return (world.X - _camPos.X) * fwd.X + (world.Y - _camPos.Y) * fwd.Y + (world.Z - _camPos.Z) * fwd.Z;
    }

    Bitmap? GetTextureBitmap(string? texturePath)
    {
        if (string.IsNullOrWhiteSpace(texturePath) || _vm is null || string.IsNullOrWhiteSpace(_vm.ProjectFolder)) return null;
        if (_textureCache.TryGetValue(texturePath, out var cached)) return cached;

        var full = Path.IsPathRooted(texturePath) ? texturePath : Path.Combine(_vm.ProjectFolder, texturePath);
        if (File.Exists(full))
        {
            try
            {
                var bmp = new Bitmap(full);
                _textureCache[texturePath] = bmp;
                return bmp;
            }
            catch { }
        }
        return null;
    }

    public override void Render(DrawingContext ctx)
    {
        var bg = _vm?.Background ?? "#FFF4F3";
        IBrush bgBrush = Color.TryParse(bg, out var c) ? new SolidColorBrush(c) : Brushes.White;
        ctx.FillRectangle(bgBrush, new Rect(Bounds.Size));

        if (_vm is null) return;

        // Render Ground Grid
        RenderGrid(ctx);

        // Render 3D Objects / Sprites sorted by depth
        var objects = _vm.ObjectList
            .Select(o => new { VM = o, Depth = GetDepth(o.Model.Position) })
            .OrderByDescending(x => x.Depth)
            .ToList();

        foreach (var item in objects)
        {
            RenderObject(ctx, item.VM);
        }

        // Render Transform Gizmo for selected object (if visible and not locked)
        if (_vm.Selected is not null && _vm.Selected.IsVisible && !_vm.Selected.IsLocked)
        {
            RenderGizmo(ctx, _vm.Selected);
        }

        // Render UI Canvas Overlay (device screen & UI elements)
        RenderUiCanvasOverlay(ctx);

        // Render overlay HUD
        RenderHud(ctx);
    }

    void RenderGrid(DrawingContext ctx)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#33482C30")), 1);
        var centerPenX = new Pen(new SolidColorBrush(Color.Parse("#B0FF4B72")), 2);
        var centerPenZ = new Pen(new SolidColorBrush(Color.Parse("#B03498DB")), 2);

        int size = 20;
        int step = 2;

        for (int i = -size; i <= size; i += step)
        {
            var p1 = Project3D(new Vec3(i, 0, -size));
            var p2 = Project3D(new Vec3(i, 0, size));
            if (p1.HasValue && p2.HasValue)
            {
                ctx.DrawLine(i == 0 ? centerPenZ : gridPen, p1.Value, p2.Value);
            }

            var p3 = Project3D(new Vec3(-size, 0, i));
            var p4 = Project3D(new Vec3(size, 0, i));
            if (p3.HasValue && p4.HasValue)
            {
                ctx.DrawLine(i == 0 ? centerPenX : gridPen, p3.Value, p4.Value);
            }
        }
    }

    void RenderObject(DrawingContext ctx, ObjectVM vm)
    {
        if (!vm.IsVisible) return;
        if (vm.IsUiElement || vm.IsCanvas) return;
        var o = vm.Model;
        var isSelected = vm == _vm?.Selected || (_vm?.SelectedObjects.Contains(vm) ?? false);
        var worldPos = GetWorldPosition(vm);

        // Draw connection line to parent if any
        if (vm.HasParent)
        {
            var parent = _vm?.Objects.FirstOrDefault(p => p.Id == vm.ParentId);
            if (parent is not null)
            {
                var p1 = Project3D(GetWorldPosition(parent));
                var p2 = Project3D(worldPos);
                if (p1.HasValue && p2.HasValue)
                {
                    var linkPen = new Pen(new SolidColorBrush(Color.Parse("#B0E05263")), 1.5, new DashStyle([4, 4], 0));
                    ctx.DrawLine(linkPen, p1.Value, p2.Value);
                }
            }
        }

        // 1. Check if object has a texture/sprite assigned
        var bmp = GetTextureBitmap(o.Texture);
        if (bmp is not null)
        {
            var centerPt = Project3D(worldPos);
            var depth = GetDepth(worldPos);
            if (centerPt.HasValue && depth > 0.1)
            {
                double sw = Math.Max(16, (Math.Abs(o.Scale.X) * 600) / depth);
                double heightScale = o.Type == ObjectTypes.Plane ? Math.Abs(o.Scale.Z) : Math.Abs(o.Scale.Y);
                double sh = Math.Max(16, (heightScale * 600) / depth);

                var destRect = new Rect(centerPt.Value.X - sw / 2, centerPt.Value.Y - sh / 2, sw, sh);
                var spriteAngle = -o.Rotation.Z * Math.PI / 180.0;

                using (ctx.PushTransform(
                    Matrix.CreateTranslation(-centerPt.Value.X, -centerPt.Value.Y) *
                    Matrix.CreateRotation(spriteAngle) *
                    Matrix.CreateTranslation(centerPt.Value.X, centerPt.Value.Y)))
                {
                    ctx.DrawImage(bmp, destRect);
                    if (isSelected)
                    {
                        var selPen = new Pen(Brushes.DeepPink, 2.5);
                        ctx.DrawRectangle(null, selPen, destRect, 4, 4);
                    }
                }
                return;
            }
        }

        // 2. Light Source rendering
        if (o.Type == ObjectTypes.Light)
        {
            var pt = Project3D(worldPos);
            if (!pt.HasValue) return;

            var lightBrush = new SolidColorBrush(Color.Parse("#F1C40F"));
            var haloBrush = new SolidColorBrush(Color.Parse("#40F1C40F"));
            var lightPen = isSelected ? new Pen(Brushes.DeepPink, 3) : new Pen(Brushes.DarkGoldenrod, 1.5);

            ctx.DrawEllipse(haloBrush, null, pt.Value, 16, 16);
            ctx.DrawEllipse(lightBrush, lightPen, pt.Value, 8, 8);
            return;
        }

        // 3. Sphere rendering
        if (o.Type == ObjectTypes.Sphere)
        {
            var pt = Project3D(worldPos);
            if (!pt.HasValue) return;

            var depth = GetDepth(worldPos);
            if (depth <= 0.2) return;

            var r = Math.Max(6, (Math.Abs(o.Scale.X) * 0.5 * 600) / depth);
            IBrush colBrush = Color.TryParse(o.Color, out var col) ? new SolidColorBrush(col) : Brushes.Pink;
            var spherePen = isSelected ? new Pen(Brushes.DeepPink, 3) : new Pen(new SolidColorBrush(Color.Parse("#66482C30")), 1);

            ctx.DrawEllipse(colBrush, spherePen, pt.Value, r, r);

            // Specular highlight
            var hiPt = new Point(pt.Value.X - r * 0.25, pt.Value.Y - r * 0.25);
            ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#66FFFFFF")), null, hiPt, r * 0.35, r * 0.35);

            if (isSelected)
            {
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#99FF1493")), 1.5), pt.Value, r, r * 0.4);
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#99FF1493")), 1.5), pt.Value, r * 0.4, r);
            }
            return;
        }

        // 4. Box / Plane / Cylinder rendering as shaded 3D mesh
        var halfX = Math.Abs(o.Scale.X) / 2.0;
        var halfY = Math.Abs(o.Scale.Y) / 2.0;
        var halfZ = Math.Abs(o.Scale.Z) / 2.0;

        if (o.Type == ObjectTypes.Plane) halfY = 0.02;

        Vec3[] localVertices =
        [
            new(-halfX, -halfY, -halfZ),
            new( halfX, -halfY, -halfZ),
            new( halfX,  halfY, -halfZ),
            new(-halfX,  halfY, -halfZ),
            new(-halfX, -halfY,  halfZ),
            new( halfX, -halfY,  halfZ),
            new( halfX,  halfY,  halfZ),
            new(-halfX,  halfY,  halfZ)
        ];

        // Apply rotation (Euler degrees)
        var radX = o.Rotation.X * Math.PI / 180.0;
        var radY = o.Rotation.Y * Math.PI / 180.0;
        var radZ = o.Rotation.Z * Math.PI / 180.0;

        var worldVerts = new Vec3[8];
        for (int i = 0; i < 8; i++)
        {
            var v = localVertices[i];
            // Rotate around X
            var y1 = v.Y * Math.Cos(radX) - v.Z * Math.Sin(radX);
            var z1 = v.Y * Math.Sin(radX) + v.Z * Math.Cos(radX);
            // Rotate around Y
            var x2 = v.X * Math.Cos(radY) + z1 * Math.Sin(radY);
            var z2 = -v.X * Math.Sin(radY) + z1 * Math.Cos(radY);
            // Rotate around Z
            var x3 = x2 * Math.Cos(radZ) - y1 * Math.Sin(radZ);
            var y3 = x2 * Math.Sin(radZ) + y1 * Math.Cos(radZ);

            worldVerts[i] = new Vec3(worldPos.X + x3, worldPos.Y + y3, worldPos.Z + z2);
        }

        int[][] faces =
        [
            [0, 1, 2, 3], // Front
            [5, 4, 7, 6], // Back
            [3, 2, 6, 7], // Top
            [4, 1, 0, 5], // Bottom
            [4, 0, 3, 7], // Left
            [1, 5, 6, 2]  // Right
        ];

        double[] faceShades = [0.85, 0.7, 1.0, 0.55, 0.75, 0.9];

        Color.TryParse(o.Color, out var baseCol);
        var baseR = baseCol.R;
        var baseG = baseCol.G;
        var baseB = baseCol.B;

        var sortedFaces = faces
            .Select((f, idx) =>
            {
                var avgDepth = f.Average(vi => GetDepth(worldVerts[vi]));
                return new { Indices = f, Shade = faceShades[idx], Depth = avgDepth };
            })
            .OrderByDescending(f => f.Depth)
            .ToList();

        var facePen = isSelected ? new Pen(Brushes.DeepPink, 2) : new Pen(new SolidColorBrush(Color.Parse("#44482C30")), 1);

        foreach (var face in sortedFaces)
        {
            var screenPoints = face.Indices.Select(vi => Project3D(worldVerts[vi])).ToList();
            if (screenPoints.Any(p => !p.HasValue)) continue;

            var r = (byte)Math.Clamp(baseR * face.Shade, 0, 255);
            var g = (byte)Math.Clamp(baseG * face.Shade, 0, 255);
            var b = (byte)Math.Clamp(baseB * face.Shade, 0, 255);
            var faceBrush = new SolidColorBrush(Color.FromRgb(r, g, b));

            var polyGeometry = new StreamGeometry();
            using (var gctx = polyGeometry.Open())
            {
                gctx.BeginFigure(screenPoints[0]!.Value, true);
                for (int i = 1; i < screenPoints.Count; i++)
                {
                    gctx.LineTo(screenPoints[i]!.Value);
                }
                gctx.EndFigure(true);
            }

            ctx.DrawGeometry(faceBrush, facePen, polyGeometry);
        }

        if (isSelected)
        {
            // Draw 3D bounding box corners
            var selPen = new Pen(Brushes.DeepPink, 2.5);
            int[,] edges = {
                {0,1},{1,2},{2,3},{3,0},
                {4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            for (int e = 0; e < 12; e++)
            {
                var p1 = Project3D(worldVerts[edges[e, 0]]);
                var p2 = Project3D(worldVerts[edges[e, 1]]);
                if (p1.HasValue && p2.HasValue)
                {
                    ctx.DrawLine(selPen, p1.Value, p2.Value);
                }
            }
        }
    }

    void RenderGizmo(DrawingContext ctx, ObjectVM selected)
    {
        if (selected.IsUiElement || selected.IsCanvas) return;
        var worldPos = GetWorldPosition(selected);
        var origin = Project3D(worldPos);
        if (!origin.HasValue) return;

        var mode = _vm?.CurrentGizmoMode ?? GizmoMode.Translate;
        if (mode == GizmoMode.Select) return;

        double gizmoLen = 2.2;
        var p = worldPos;

        var tipX = Project3D(new Vec3(p.X + gizmoLen, p.Y, p.Z));
        var tipY = Project3D(new Vec3(p.X, p.Y + gizmoLen, p.Z));
        var tipZ = Project3D(new Vec3(p.X, p.Y, p.Z + gizmoLen));

        var penX = new Pen(new SolidColorBrush(Color.Parse("#E74C3C")), _activeGizmoAxis == "X" ? 4 : 3);
        var penY = new Pen(new SolidColorBrush(Color.Parse("#2ECC71")), _activeGizmoAxis == "Y" ? 4 : 3);
        var penZ = new Pen(new SolidColorBrush(Color.Parse("#3498DB")), _activeGizmoAxis == "Z" ? 4 : 3);

        if (mode == GizmoMode.Translate)
        {
            if (tipX.HasValue) { ctx.DrawLine(penX, origin.Value, tipX.Value); DrawGizmoHandle(ctx, tipX.Value, penX.Brush ?? Brushes.Red, "X"); }
            if (tipY.HasValue) { ctx.DrawLine(penY, origin.Value, tipY.Value); DrawGizmoHandle(ctx, tipY.Value, penY.Brush ?? Brushes.Green, "Y"); }
            if (tipZ.HasValue) { ctx.DrawLine(penZ, origin.Value, tipZ.Value); DrawGizmoHandle(ctx, tipZ.Value, penZ.Brush ?? Brushes.Blue, "Z"); }
            ctx.DrawEllipse(Brushes.White, new Pen(Brushes.DarkGray, 1), origin.Value, 4, 4);
        }
        else if (mode == GizmoMode.Rotate)
        {
            double r = 2.0;
            // Draw 3 rotation rings around origin
            DrawRotationRing(ctx, selected.Model.Position, "X", penX);
            DrawRotationRing(ctx, selected.Model.Position, "Y", penY);
            DrawRotationRing(ctx, selected.Model.Position, "Z", penZ);

            // Draw distinct handle knobs for clicking/dragging on each axis
            var hX = Project3D(new Vec3(p.X, p.Y + r, p.Z));
            var hY = Project3D(new Vec3(p.X + r, p.Y, p.Z));
            var hZ = Project3D(new Vec3(p.X, p.Y, p.Z + r));

            if (hX.HasValue) DrawGizmoHandle(ctx, hX.Value, penX.Brush ?? Brushes.Red, "X");
            if (hY.HasValue) DrawGizmoHandle(ctx, hY.Value, penY.Brush ?? Brushes.Green, "Y");
            if (hZ.HasValue) DrawGizmoHandle(ctx, hZ.Value, penZ.Brush ?? Brushes.Blue, "Z");
        }
        else if (mode == GizmoMode.Scale)
        {
            if (tipX.HasValue) { ctx.DrawLine(penX, origin.Value, tipX.Value); ctx.DrawRectangle(penX.Brush, null, new Rect(tipX.Value.X - 4, tipX.Value.Y - 4, 8, 8)); }
            if (tipY.HasValue) { ctx.DrawLine(penY, origin.Value, tipY.Value); ctx.DrawRectangle(penY.Brush, null, new Rect(tipY.Value.X - 4, tipY.Value.Y - 4, 8, 8)); }
            if (tipZ.HasValue) { ctx.DrawLine(penZ, origin.Value, tipZ.Value); ctx.DrawRectangle(penZ.Brush, null, new Rect(tipZ.Value.X - 4, tipZ.Value.Y - 4, 8, 8)); }
            ctx.DrawRectangle(Brushes.White, new Pen(Brushes.DarkGray, 1), new Rect(origin.Value.X - 5, origin.Value.Y - 5, 10, 10));
        }
    }

    void DrawGizmoHandle(DrawingContext ctx, Point pt, IBrush brush, string label)
    {
        ctx.DrawEllipse(brush, null, pt, 8, 8);
        ctx.DrawEllipse(Brushes.White, null, pt, 3, 3);
    }

    void DrawRotationRing(DrawingContext ctx, Vec3 center, string axis, Pen pen)
    {
        int segs = 32;
        double r = 2.0;
        Point? prev = null;
        for (int i = 0; i <= segs; i++)
        {
            double angle = (i * 2 * Math.PI) / segs;
            Vec3 pt = axis switch
            {
                "X" => new Vec3(center.X, center.Y + Math.Cos(angle) * r, center.Z + Math.Sin(angle) * r),
                "Y" => new Vec3(center.X + Math.Cos(angle) * r, center.Y, center.Z + Math.Sin(angle) * r),
                _ => new Vec3(center.X + Math.Cos(angle) * r, center.Y + Math.Sin(angle) * r, center.Z)
            };
            var s = Project3D(pt);
            if (s.HasValue && prev.HasValue)
            {
                ctx.DrawLine(pen, prev.Value, s.Value);
            }
            prev = s;
        }
    }

    void RenderHud(DrawingContext ctx)
    {
        var hintBg = new SolidColorBrush(Color.Parse("#DDFFFFFF"));
        var hudRect = new Rect(10, 10, 420, 28);
        ctx.FillRectangle(hintBg, hudRect, 8);

        var modeName = _vm?.CurrentGizmoMode switch
        {
            GizmoMode.Translate => "Перемещение (W)",
            GizmoMode.Rotate => "Вращение (E)",
            GizmoMode.Scale => "Масштабирование (R)",
            _ => "Выбор (Q)"
        };

        var pipetteHint = _vm?.IsEyeDropperActive == true ? " | 🧪 Кликните по объекту!" : "";

        var text = new FormattedText(
            $"[ {modeName} ]  ПКМ: Полёт (WASD)  F: Фокус{pipetteHint}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter,system-ui,sans-serif"),
            11.5,
            _vm?.IsEyeDropperActive == true ? Brushes.DeepPink : new SolidColorBrush(Color.Parse("#482C30"))
        );
        ctx.DrawText(text, new Point(18, 16));
    }

    Rect GetDeviceCanvasRect(string? preset)
    {
        double aspect = preset switch
        {
            DevicePresets.MobilePortrait => 9.0 / 16.0,
            DevicePresets.MobileLandscape => 19.5 / 9.0,
            DevicePresets.Tablet => 4.0 / 3.0,
            _ => 16.0 / 9.0
        };

        double maxH = Math.Max(180, Bounds.Height * 0.84);
        double maxW = Math.Max(220, Bounds.Width * 0.88);

        double h = maxH;
        double w = h * aspect;
        if (w > maxW)
        {
            w = maxW;
            h = w / aspect;
        }

        double x = (Bounds.Width - w) / 2.0;
        double y = (Bounds.Height - h) / 2.0;
        return new Rect(x, y, w, h);
    }

    Rect GetUiElementRect(ObjectVM el, double centerX, double centerY)
    {
        double w = Math.Max(24, Math.Abs(el.ScaleX));
        double h = Math.Max(20, Math.Abs(el.ScaleY));
        double x = centerX + el.PosX - w / 2.0;
        double y = centerY - el.PosY - h / 2.0;
        return new Rect(x, y, w, h);
    }

    ObjectVM? PickUiElement(Point mouse)
    {
        if (_vm is null) return null;
        var canvas = _vm.Objects.FirstOrDefault(o => o.IsCanvas);
        if (canvas is not null && !canvas.IsVisible) return null;
        var uiElements = _vm.Objects.Where(o => o.IsUiElement && o.IsVisible).ToList();
        if (canvas is null && uiElements.Count == 0) return null;

        var frame = GetDeviceCanvasRect(canvas?.UiDevicePreset);
        var centerX = frame.X + frame.Width / 2.0;
        var centerY = frame.Y + frame.Height / 2.0;

        // 1. Check UI elements in reverse order (top-most first)
        foreach (var el in uiElements.AsEnumerable().Reverse())
        {
            var elRect = GetUiElementRect(el, centerX, centerY);
            if (elRect.Contains(mouse)) return el;
        }

        // 2. Check Canvas header pill or outer border
        if (canvas is not null)
        {
            var headerRect = new Rect(frame.X + 12, frame.Y + 12, 220, 24);
            if (headerRect.Contains(mouse)) return canvas;

            if (frame.Contains(mouse))
            {
                var inner = frame.Deflate(12);
                if (!inner.Contains(mouse)) return canvas;
            }
        }

        return null;
    }

    void RenderUiCanvasOverlay(DrawingContext ctx)
    {
        if (_vm is null) return;
        var canvas = _vm.Objects.FirstOrDefault(o => o.IsCanvas);
        // If Canvas exists and is hidden, hide the entire UI Canvas and all its elements!
        if (canvas is not null && !canvas.IsVisible) return;
        var uiElements = _vm.Objects.Where(o => o.IsUiElement).ToList();
        if (canvas is null && uiElements.Count == 0) return;

        var frame = GetDeviceCanvasRect(canvas?.UiDevicePreset);
        var isCanvasSelected = canvas is not null && (_vm.Selected == canvas || _vm.SelectedObjects.Contains(canvas));

        // 1. Render Device Frame
        var frameBrush = new SolidColorBrush(Color.Parse("#06000000"));
        var borderPen = isCanvasSelected
            ? new Pen(Brushes.DeepPink, 2.5)
            : new Pen(new SolidColorBrush(Color.Parse("#88F7CDD5")), 1.5, new DashStyle([6, 4], 0));

        ctx.FillRectangle(frameBrush, frame, 16);
        ctx.DrawRectangle(borderPen, frame, 16);

        // 2. Render Device Header Pill (drawn cleanly inside top-left of canvas frame)
        var deviceName = canvas?.UiDevicePreset switch
        {
            DevicePresets.MobilePortrait => "📱 Мобильный (9:16)",
            DevicePresets.MobileLandscape => "📱 Мобильный Горизонт (19.5:9)",
            DevicePresets.Tablet => "📟 Планшет (4:3)",
            _ => "🖥️ Компьютер (16:9)"
        };

        var headerRect = new Rect(frame.X + 12, frame.Y + 12, 220, 24);
        IBrush pillBrush = isCanvasSelected ? Brushes.DeepPink : new SolidColorBrush(Color.Parse("#E0FCE8EC"));
        ctx.FillRectangle(pillBrush, headerRect, 6);

        var tf = new Typeface("Inter,system-ui,sans-serif");
        IBrush textBrush = isCanvasSelected ? Brushes.White : new SolidColorBrush(Color.Parse("#4A2929"));
        var headerText = new FormattedText(
            $"🖼️ Canvas: {deviceName}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            tf,
            11,
            textBrush
        );
        ctx.DrawText(headerText, new Point(headerRect.X + 8, headerRect.Y + 4));

        // 3. Render UI Elements inside Canvas
        var centerX = frame.X + frame.Width / 2.0;
        var centerY = frame.Y + frame.Height / 2.0;

        foreach (var el in uiElements)
        {
            if (!el.IsVisible) continue;
            var isElSelected = el == _vm.Selected || _vm.SelectedObjects.Contains(el);
            var rect = GetUiElementRect(el, centerX, centerY);

            if (el.IsUiText)
            {
                Color.TryParse(el.Color, out var textColor);
                if (textColor.A == 0) textColor = Color.Parse("#4A2929");

                var txt = new FormattedText(
                    el.UiText ?? el.Name,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    tf,
                    Math.Clamp(el.UiFontSize, 10, 48),
                    new SolidColorBrush(textColor)
                );
                var tx = rect.X + (rect.Width - txt.Width) / 2.0;
                var ty = rect.Y + (rect.Height - txt.Height) / 2.0;
                ctx.DrawText(txt, new Point(tx, ty));
            }
            else if (el.IsUiImage)
            {
                Color.TryParse(el.Color, out var imgColor);
                if (imgColor.A == 0) imgColor = Colors.White;
                var brush = new SolidColorBrush(imgColor);
                ctx.FillRectangle(brush, rect, 8);
                ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#33F1D9DB")), 1), rect, 8);

                var imgBmp = GetTextureBitmap(el.Texture);
                if (imgBmp is not null)
                {
                    ctx.DrawImage(imgBmp, rect);
                }
                else
                {
                    var imgHint = new FormattedText("🖼️ Image", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, 11, Brushes.Gray);
                    ctx.DrawText(imgHint, new Point(rect.X + (rect.Width - imgHint.Width) / 2, rect.Y + (rect.Height - imgHint.Height) / 2));
                }
            }
            else if (el.IsUiButton)
            {
                Color.TryParse(el.Color, out var btnColor);
                if (btnColor.A == 0) btnColor = Color.Parse("#F7CDD5");

                var bgBrush = new SolidColorBrush(btnColor);
                ctx.FillRectangle(bgBrush, rect, 12);
                ctx.DrawRectangle(new Pen(Brushes.White, 1.5), rect, 12);

                var btnText = new FormattedText(
                    el.UiText ?? "Кнопка",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    tf,
                    Math.Clamp(el.UiFontSize, 10, 24),
                    new SolidColorBrush(Color.Parse("#4A2929"))
                );
                var bx = rect.X + (rect.Width - btnText.Width) / 2.0;
                var by = rect.Y + (rect.Height - btnText.Height) / 2.0;
                ctx.DrawText(btnText, new Point(bx, by));
            }

            // Selection Highlight
            if (isElSelected)
            {
                var selPen = new Pen(Brushes.DeepPink, 2);
                var selRect = new Rect(rect.X - 3, rect.Y - 3, rect.Width + 6, rect.Height + 6);
                ctx.DrawRectangle(selPen, selRect, 4);

                var knobPen = new Pen(Brushes.White, 1);
                ctx.DrawRectangle(Brushes.DeepPink, knobPen, new Rect(selRect.Left - 3, selRect.Top - 3, 6, 6));
                ctx.DrawRectangle(Brushes.DeepPink, knobPen, new Rect(selRect.Right - 3, selRect.Top - 3, 6, 6));
                ctx.DrawRectangle(Brushes.DeepPink, knobPen, new Rect(selRect.Left - 3, selRect.Bottom - 3, 6, 6));
                ctx.DrawRectangle(Brushes.DeepPink, knobPen, new Rect(selRect.Right - 3, selRect.Bottom - 3, 6, 6));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        _lastPointerPos = p;

        var prop = e.GetCurrentPoint(this).Properties;

        // Eye-dropper tool logic
        if (_vm is not null && _vm.IsEyeDropperActive && prop.IsLeftButtonPressed)
        {
            var pickedObj = PickObject(p);
            if (pickedObj is not null && _vm.Selected is not null)
            {
                _vm.Selected.Color = pickedObj.Model.Color;
                _vm.Say($"Пипетка: скопирован цвет {pickedObj.Model.Color} с объекта «{pickedObj.Name}».");
            }
            _vm.IsEyeDropperActive = false;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (prop.IsRightButtonPressed)
        {
            // Enter Flight Mode
            _isFlying = true;
            _flightTimer.Start();
            e.Handled = true;
            return;
        }

        if (prop.IsMiddleButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _isPanning = true;
            else _isOrbiting = true;
            e.Handled = true;
            return;
        }

        if (prop.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _isOrbiting = true;
            e.Handled = true;
            return;
        }

        if (prop.IsLeftButtonPressed && _vm?.Selected is not null && !_vm.Selected.IsLocked && _vm.Selected.IsVisible)
        {
            // Hit-test gizmo handles
            var axis = HitTestGizmo(p, _vm.Selected);
            if (axis is not null)
            {
                _activeGizmoAxis = axis;
                _dragStartPointer = p;
                var m = _vm.Selected.Model;
                _dragStartPos = new Vec3(m.Position.X, m.Position.Y, m.Position.Z);
                _dragStartRot = new Vec3(m.Rotation.X, m.Rotation.Y, m.Rotation.Z);
                _dragStartScale = new Vec3(m.Scale.X, m.Scale.Y, m.Scale.Z);
                _dragStartMultiPos.Clear();
                foreach (var obj in _vm.SelectedObjects)
                {
                    _dragStartMultiPos[obj] = new Vec3(obj.PosX, obj.PosY, obj.PosZ);
                }
                e.Handled = true;
                return;
            }
        }

        // Pick UI element or object in scene
        if (prop.IsLeftButtonPressed && _vm is not null)
        {
            var pickedUi = PickUiElement(p);
            if (pickedUi is not null)
            {
                var isMulti = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _vm.ToggleSelection(pickedUi, isMulti);
                if (pickedUi.IsUiElement && !pickedUi.IsLocked)
                {
                    _isDraggingUi = true;
                    _dragStartPointer = p;
                    _dragStartUiPos = new Point(pickedUi.PosX, pickedUi.PosY);
                }
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var picked = PickObject(p);
            if (picked is not null)
            {
                var isMulti = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _vm.ToggleSelection(picked, isMulti);
            }
            else
            {
                _vm.DeselectAll(); // Переключает на настройки сцены при клике по пустому месту!
            }
            InvalidateVisual();
        }
    }

    string? HitTestGizmo(Point mouse, ObjectVM selected)
    {
        var worldPos = GetWorldPosition(selected);
        var origin = Project3D(worldPos);
        if (!origin.HasValue) return null;

        var mode = _vm?.CurrentGizmoMode ?? GizmoMode.Translate;
        if (mode == GizmoMode.Select) return null;

        double len = 2.2;
        var p = worldPos;

        if (mode == GizmoMode.Rotate)
        {
            double r = 2.0;
            var hX = Project3D(new Vec3(p.X, p.Y + r, p.Z));
            var hY = Project3D(new Vec3(p.X + r, p.Y, p.Z));
            var hZ = Project3D(new Vec3(p.X, p.Y, p.Z + r));

            if (hX.HasValue && Distance(mouse, hX.Value) <= 24) return "X";
            if (hY.HasValue && Distance(mouse, hY.Value) <= 24) return "Y";
            if (hZ.HasValue && Distance(mouse, hZ.Value) <= 24) return "Z";

            // Proximity to rings
            double dX = DistanceToRing(mouse, p, "X", r);
            double dY = DistanceToRing(mouse, p, "Y", r);
            double dZ = DistanceToRing(mouse, p, "Z", r);

            double minDist = Math.Min(dX, Math.Min(dY, dZ));
            if (minDist <= 18)
            {
                if (minDist == dX) return "X";
                if (minDist == dY) return "Y";
                return "Z";
            }
            return null;
        }

        var tipX = Project3D(new Vec3(p.X + len, p.Y, p.Z));
        var tipY = Project3D(new Vec3(p.X, p.Y + len, p.Z));
        var tipZ = Project3D(new Vec3(p.X, p.Y, p.Z + len));

        double radius = 18.0;

        if (tipX.HasValue && Distance(mouse, tipX.Value) <= radius) return "X";
        if (tipY.HasValue && Distance(mouse, tipY.Value) <= radius) return "Y";
        if (tipZ.HasValue && Distance(mouse, tipZ.Value) <= radius) return "Z";
        if (Distance(mouse, origin.Value) <= radius) return "XYZ";

        return null;
    }

    double DistanceToRing(Point mouse, Vec3 center, string axis, double r)
    {
        double min = 9999;
        int segs = 16;
        for (int i = 0; i < segs; i++)
        {
            double angle = (i * 2 * Math.PI) / segs;
            Vec3 pt = axis switch
            {
                "X" => new Vec3(center.X, center.Y + Math.Cos(angle) * r, center.Z + Math.Sin(angle) * r),
                "Y" => new Vec3(center.X + Math.Cos(angle) * r, center.Y, center.Z + Math.Sin(angle) * r),
                _ => new Vec3(center.X + Math.Cos(angle) * r, center.Y + Math.Sin(angle) * r, center.Z)
            };
            var s = Project3D(pt);
            if (s.HasValue)
            {
                min = Math.Min(min, Distance(mouse, s.Value));
            }
        }
        return min;
    }

    double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    ObjectVM? PickObject(Point mouse)
    {
        if (_vm is null) return null;
        return _vm.ObjectList
            .Where(o => !o.IsCanvas && !o.IsUiElement && o.IsVisible)
            .Select(o => {
                var wp = GetWorldPosition(o);
                return new { VM = o, Pt = Project3D(wp), Depth = GetDepth(wp) };
            })
            .Where(x => x.Pt.HasValue && x.Depth > 0.2)
            .OrderBy(x => x.Depth)
            .FirstOrDefault(x => Distance(mouse, x.Pt!.Value) < Math.Max(24, (350 / x.Depth)))?.VM;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        var dx = p.X - _lastPointerPos.X;
        var dy = p.Y - _lastPointerPos.Y;
        _lastPointerPos = p;

        if (_isDraggingUi && _vm?.Selected?.IsUiElement == true)
        {
            _vm.Selected.PosX = Math.Round(_dragStartUiPos.X + (p.X - _dragStartPointer.X));
            _vm.Selected.PosY = Math.Round(_dragStartUiPos.Y - (p.Y - _dragStartPointer.Y));
            InvalidateVisual();
            return;
        }

        if (_isFlying)
        {
            _camYaw += dx * 0.25;
            _camPitch = Math.Clamp(_camPitch - dy * 0.25, -89.0, 89.0);
            InvalidateVisual();
            return;
        }

        if (_isOrbiting)
        {
            _camYaw += dx * 0.35;
            _camPitch = Math.Clamp(_camPitch - dy * 0.35, -89.0, 89.0);
            InvalidateVisual();
            return;
        }

        if (_isPanning)
        {
            var (_, right, up) = GetCameraVectors();
            double panSpeed = 0.03;
            _camPos.X -= (right.X * dx - up.X * dy) * panSpeed;
            _camPos.Y -= (right.Y * dx - up.Y * dy) * panSpeed;
            _camPos.Z -= (right.Z * dx - up.Z * dy) * panSpeed;
            InvalidateVisual();
            return;
        }

        if (_activeGizmoAxis is not null && _vm?.Selected is not null && !_vm.Selected.IsLocked)
        {
            ApplyGizmoDrag(p);
            InvalidateVisual();
        }
    }

    void ApplyGizmoDrag(Point currentMouse)
    {
        if (_vm?.Selected is null || _vm.Selected.IsLocked) return;
        var mode = _vm.CurrentGizmoMode;
        var deltaPixelsX = currentMouse.X - _dragStartPointer.X;
        var deltaPixelsY = currentMouse.Y - _dragStartPointer.Y;

        double sensitivity = 0.04;

        if (mode == GizmoMode.Translate)
        {
            double dX = 0, dY = 0, dZ = 0;
            if (_activeGizmoAxis == "X") dX = deltaPixelsX * sensitivity;
            else if (_activeGizmoAxis == "Y") dY = -deltaPixelsY * sensitivity;
            else if (_activeGizmoAxis == "Z") dZ = (deltaPixelsX + deltaPixelsY) * sensitivity * 0.7;
            else if (_activeGizmoAxis == "XYZ")
            {
                dX = deltaPixelsX * sensitivity;
                dZ = -deltaPixelsY * sensitivity;
            }

            if (_vm.SelectedObjects.Count > 1)
            {
                foreach (var obj in _vm.SelectedObjects)
                {
                    if (_dragStartMultiPos.TryGetValue(obj, out var startP))
                    {
                        if (_activeGizmoAxis == "X" || _activeGizmoAxis == "XYZ") obj.PosX = Math.Round(startP.X + dX, 2);
                        if (_activeGizmoAxis == "Y") obj.PosY = Math.Round(startP.Y + dY, 2);
                        if (_activeGizmoAxis == "Z" || _activeGizmoAxis == "XYZ") obj.PosZ = Math.Round(startP.Z + dZ, 2);
                    }
                }
            }
            else
            {
                if (_activeGizmoAxis == "X" || _activeGizmoAxis == "XYZ") _vm.Selected.PosX = Math.Round(_dragStartPos.X + dX, 2);
                if (_activeGizmoAxis == "Y") _vm.Selected.PosY = Math.Round(_dragStartPos.Y + dY, 2);
                if (_activeGizmoAxis == "Z" || _activeGizmoAxis == "XYZ") _vm.Selected.PosZ = Math.Round(_dragStartPos.Z + dZ, 2);
            }
        }
        else if (mode == GizmoMode.Rotate)
        {
            double rotSens = 1.0;
            if (_activeGizmoAxis == "X")
            {
                _vm.Selected.RotX = Math.Round((_dragStartRot.X - deltaPixelsY * rotSens) % 360, 1);
            }
            else if (_activeGizmoAxis == "Y")
            {
                _vm.Selected.RotY = Math.Round((_dragStartRot.Y + deltaPixelsX * rotSens) % 360, 1);
            }
            else if (_activeGizmoAxis == "Z")
            {
                _vm.Selected.RotZ = Math.Round((_dragStartRot.Z + (deltaPixelsX - deltaPixelsY) * rotSens) % 360, 1);
            }
        }
        else if (mode == GizmoMode.Scale)
        {
            double scaleSens = 0.03;
            if (_activeGizmoAxis == "X") _vm.Selected.ScaleX = Math.Max(0.1, Math.Round(_dragStartScale.X + deltaPixelsX * scaleSens, 2));
            else if (_activeGizmoAxis == "Y") _vm.Selected.ScaleY = Math.Max(0.1, Math.Round(_dragStartScale.Y - deltaPixelsY * scaleSens, 2));
            else if (_activeGizmoAxis == "Z") _vm.Selected.ScaleZ = Math.Max(0.1, Math.Round(_dragStartScale.Z + deltaPixelsX * scaleSens, 2));
            else if (_activeGizmoAxis == "XYZ")
            {
                var s = Math.Max(0.1, Math.Round(_dragStartScale.X + (deltaPixelsX - deltaPixelsY) * scaleSens, 2));
                _vm.Selected.ScaleX = s;
                _vm.Selected.ScaleY = s;
                _vm.Selected.ScaleZ = s;
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _isFlying = false;
        _flightTimer.Stop();
        _isOrbiting = false;
        _isPanning = false;
        _isDraggingUi = false;
        _activeGizmoAxis = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var (fwd, _, _) = GetCameraVectors();
        double zoomAmount = e.Delta.Y * 1.5;
        _camPos.X += fwd.X * zoomAmount;
        _camPos.Y += fwd.Y * zoomAmount;
        _camPos.Z += fwd.Z * zoomAmount;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _heldKeys.Add(e.Key);

        if (_vm is null) return;

        // Hotkeys for Gizmo modes
        if (e.Key == Key.W && !_isFlying) { _vm.SetGizmoMode("translate"); InvalidateVisual(); e.Handled = true; }
        else if (e.Key == Key.E && !_isFlying) { _vm.SetGizmoMode("rotate"); InvalidateVisual(); e.Handled = true; }
        else if (e.Key == Key.R && !_isFlying) { _vm.SetGizmoMode("scale"); InvalidateVisual(); e.Handled = true; }
        else if (e.Key == Key.Q && !_isFlying) { _vm.SetGizmoMode("select"); InvalidateVisual(); e.Handled = true; }
        else if (e.Key == Key.F) { FocusOnSelected(); e.Handled = true; }
        else if (e.Key is Key.Delete or Key.Back) { _vm.DeleteSelectedCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.D && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            _vm.DuplicateSelected(); e.Handled = true;
        }
        else if (e.Key == Key.S && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            _vm.SaveCommand.Execute(null); e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            if (_vm.IsRunning) _vm.StopCommand.Execute(null);
            else _vm.RunCommand.Execute(null);
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);
    }
}
