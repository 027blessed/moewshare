using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Moewshare.Editor.Services;
using Moewshare.Editor.ViewModels;
using Moewshare.Engine;
using Path = System.IO.Path;
using File = System.IO.File;
using Directory = System.IO.Directory;

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

    sealed class ObjMesh
    {
        public List<Vec3> Vertices { get; } = [];
        public List<int[]> Faces { get; } = [];
    }
    static readonly Dictionary<string, ObjMesh> _objCache = new();

    static ObjMesh? LoadObjMesh(string path)
    {
        if (_objCache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;
        try
        {
            var mesh = new ObjMesh();
            var rawVerts = new List<Vec3>();
            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("v "))
                {
                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vx) &&
                        double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vy) &&
                        double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vz))
                    {
                        rawVerts.Add(new Vec3(vx, vy, vz));
                    }
                }
                else if (trimmed.StartsWith("f "))
                {
                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var faceVerts = new List<int>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var slash = parts[i].IndexOf('/');
                        var vStr = slash >= 0 ? parts[i][..slash] : parts[i];
                        if (int.TryParse(vStr, out var vIdx))
                        {
                            faceVerts.Add(vIdx > 0 ? vIdx - 1 : rawVerts.Count + vIdx);
                        }
                    }
                    if (faceVerts.Count >= 3)
                    {
                        mesh.Faces.Add(faceVerts.ToArray());
                    }
                }
            }

            if (rawVerts.Count == 0) return null;

            double minX = rawVerts.Min(v => v.X), maxX = rawVerts.Max(v => v.X);
            double minY = rawVerts.Min(v => v.Y), maxY = rawVerts.Max(v => v.Y);
            double minZ = rawVerts.Min(v => v.Z), maxZ = rawVerts.Max(v => v.Z);
            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;
            double centerZ = (minZ + maxZ) / 2.0;
            double maxDim = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            if (maxDim <= 0.0001) maxDim = 1.0;

            foreach (var v in rawVerts)
            {
                mesh.Vertices.Add(new Vec3(
                    (v.X - centerX) / maxDim,
                    (v.Y - centerY) / maxDim,
                    (v.Z - centerZ) / maxDim
                ));
            }

            _objCache[path] = mesh;
            return mesh;
        }
        catch { return null; }
    }

    public Vec3 GetWorldPosition(ObjectVM vm)
    {
        if (_vm is not null) return _vm.GetWorldPosition(vm);
        return new Vec3(vm.PosX, vm.PosY, vm.PosZ);
    }

    public ScenePreview()
    {
        ClipToBounds = true;
        Focusable = true;

        _flightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _flightTimer.Tick += OnFlightTick;

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnViewportDragOver);
        AddHandler(DragDrop.DropEvent, OnViewportDrop);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void Attach(EditorViewModel vm)
    {
        if (_vm == vm) return;
        if (_vm is not null)
        {
            _vm.SceneChanged -= InvalidateVisual;
            _vm.RequestFocus -= FocusOnSelected;
        }
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

        // 4. 3D Model (.obj, .fbx, .stl, .glb) rendering
        if (o.Type == ObjectTypes.Model || !string.IsNullOrEmpty(o.ModelPath))
        {
            RenderModelMesh(ctx, vm, worldPos, isSelected);
            return;
        }

        // 5. Box / Plane / Cylinder rendering as shaded 3D mesh
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
        var modeName = _vm?.CurrentGizmoMode switch
        {
            GizmoMode.Translate => "Перемещение (W)",
            GizmoMode.Rotate => "Вращение (E)",
            GizmoMode.Scale => "Масштабирование (R)",
            _ => "Выбор (Q)"
        };

        var pipetteHint = _vm?.IsEyeDropperActive == true ? " | 🧪 Кликните по объекту!" : "";

        var orbit = OperatingSystem.IsMacOS() ? "⌥+ЛКМ" : "Alt+ЛКМ";
        var text = new FormattedText(
            $"{modeName}   ·   ПКМ + WASD — полёт   ·   {orbit} — орбита   ·   F — фокус   ·   F1 — все клавиши{pipetteHint}",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Inter,system-ui,sans-serif"),
            11.5,
            _vm?.IsEyeDropperActive == true ? Brushes.DeepPink : this.ResBrush("HudTextBrush", new SolidColorBrush(Color.Parse("#482C30"))))
        {
            MaxTextWidth = Math.Max(60, Bounds.Width - 40),
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1
        };
        var hudRect = new Rect(10, 10, Math.Min(Bounds.Width - 20, text.Width + 20), 28);
        if (hudRect.Width <= 0) return;
        ctx.DrawRectangle(this.ResBrush("HudBgBrush", new SolidColorBrush(Color.Parse("#DDFFFFFF"))),
            new Pen(this.ResBrush("BorderBrush2", Brushes.Transparent), 1), hudRect, 8, 8);
        ctx.DrawText(text, new Point(20, 10 + (28 - text.Height) / 2));
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
                    // Skip if any ancestor is also in SelectedObjects (avoid double translation)
                    bool hasSelectedAncestor = false;
                    var curP = obj.ParentId;
                    int d = 0;
                    while (!string.IsNullOrEmpty(curP) && d++ < 20)
                    {
                        var ancestor = _vm.Objects.FirstOrDefault(o => o.Id == curP);
                        if (ancestor is not null && _vm.SelectedObjects.Contains(ancestor))
                        {
                            hasSelectedAncestor = true;
                            break;
                        }
                        curP = ancestor?.ParentId;
                    }
                    if (hasSelectedAncestor) continue;

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
        if (_vm?.DraggedAsset is not null)
        {
            var asset = _vm.DraggedAsset;
            _vm.DraggedAsset = null;
            var pt = e.GetPosition(this);
            var spawnWorld = ScreenToGroundPlane(pt);

            if (asset.IsModel)
            {
                _vm.SpawnAssetOnScene(asset, spawnWorld);
            }
            else if (asset.IsTexture)
            {
                var picked = PickObject(pt);
                if (picked is not null)
                {
                    picked.Texture = asset.RelativePath;
                    _vm.Say($"Текстура «{asset.Name}» применена к объекту «{picked.Name}».", ConsoleLevel.Success);
                }
                else
                {
                    _vm.SpawnAssetOnScene(asset, spawnWorld);
                }
            }
        }

        _isFlying = false;
        _flightTimer.Stop();
        _isOrbiting = false;
        _isPanning = false;
        _isDraggingUi = false;
        _activeGizmoAxis = null;
        InvalidateVisual();
    }

    public Vec3 ScreenToGroundPlane(Point screenPt)
    {
        var (fwd, right, up) = GetCameraVectors();
        var f = Math.Max(Bounds.Height, Bounds.Width) * 0.9;
        var xc_norm = (screenPt.X - Bounds.Width / 2.0) / f;
        var yc_norm = (Bounds.Height / 2.0 - screenPt.Y) / f;

        var dirX = fwd.X + right.X * xc_norm + up.X * yc_norm;
        var dirY = fwd.Y + right.Y * xc_norm + up.Y * yc_norm;
        var dirZ = fwd.Z + right.Z * xc_norm + up.Z * yc_norm;

        if (Math.Abs(dirY) > 0.001)
        {
            var t = -_camPos.Y / dirY;
            if (t > 0 && t < 100)
            {
                return new Vec3(
                    Math.Round(_camPos.X + dirX * t, 1),
                    0,
                    Math.Round(_camPos.Z + dirZ * t, 1)
                );
            }
        }
        return new Vec3(
            Math.Round(_camPos.X + fwd.X * 8.0, 1),
            0,
            Math.Round(_camPos.Z + fwd.Z * 8.0, 1)
        );
    }

    void OnViewportDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files) || _vm?.DraggedAsset is not null)
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    void OnViewportDrop(object? sender, DragEventArgs e)
    {
        var pt = e.GetPosition(this);
        var spawnWorld = ScreenToGroundPlane(pt);

        if (_vm?.DraggedAsset is not null)
        {
            var asset = _vm.DraggedAsset;
            _vm.DraggedAsset = null;
            if (asset.IsModel)
            {
                _vm.SpawnAssetOnScene(asset, spawnWorld);
            }
            else if (asset.IsTexture)
            {
                var picked = PickObject(pt);
                if (picked is not null)
                {
                    picked.Texture = asset.RelativePath;
                    _vm.Say($"Текстура «{asset.Name}» применена к объекту «{picked.Name}».", ConsoleLevel.Success);
                }
                else
                {
                    _vm.SpawnAssetOnScene(asset, spawnWorld);
                }
            }
            return;
        }

        var files = e.Data.GetFiles();
        if (files is not null && _vm is not null)
        {
            foreach (var item in files)
            {
                var localPath = item.TryGetLocalPath();
                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    var ext = Path.GetExtension(localPath).ToLowerInvariant();
                    var targetFolder = ext is ".obj" or ".fbx" or ".stl" or ".glb" or ".gltf"
                        ? Path.Combine(_vm.ProjectFolder, "Assets", "Models")
                        : Path.Combine(_vm.ProjectFolder, "Assets", "Textures");
                    Directory.CreateDirectory(targetFolder);
                    var dest = Path.Combine(targetFolder, Path.GetFileName(localPath));
                    File.Copy(localPath, dest, true);
                    _vm.RefreshAssets();
                    var rel = Path.GetRelativePath(_vm.ProjectFolder, dest).Replace('\\', '/');
                    if (ext is ".obj" or ".fbx" or ".stl" or ".glb" or ".gltf")
                    {
                        var assetItem = new AssetItemVM
                        {
                            Name = Path.GetFileName(dest),
                            FullPath = dest,
                            RelativePath = rel,
                            IsDirectory = false,
                            Kind = AssetKind.Model3D,
                            Extension = ext
                        };
                        _vm.SpawnAssetOnScene(assetItem, spawnWorld);
                    }
                }
            }
        }
    }

    void RenderModelMesh(DrawingContext ctx, ObjectVM vm, Vec3 worldPos, bool isSelected)
    {
        var o = vm.Model;
        var depth = GetDepth(worldPos);
        if (depth <= 0.2) return;

        var fullModelPath = !string.IsNullOrEmpty(o.ModelPath) && _vm is not null
            ? (Path.IsPathRooted(o.ModelPath) ? o.ModelPath : Path.Combine(_vm.ProjectFolder, o.ModelPath))
            : null;

        var isObj = fullModelPath is not null && fullModelPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase);
        var objMesh = isObj ? LoadObjMesh(fullModelPath!) : null;

        Color.TryParse(o.Color, out var baseCol);
        if (baseCol.A == 0) baseCol = Color.Parse("#DCDDE1");

        var radX = o.Rotation.X * Math.PI / 180.0;
        var radY = o.Rotation.Y * Math.PI / 180.0;
        var radZ = o.Rotation.Z * Math.PI / 180.0;

        Vec3 RotateModelVert(Vec3 v, double sx, double sy, double sz)
        {
            var vx = v.X * sx;
            var vy = v.Y * sy;
            var vz = v.Z * sz;

            var y1 = vy * Math.Cos(radX) - vz * Math.Sin(radX);
            var z1 = vy * Math.Sin(radX) + vz * Math.Cos(radX);
            var x2 = vx * Math.Cos(radY) + z1 * Math.Sin(radY);
            var z2 = -vx * Math.Sin(radY) + z1 * Math.Cos(radY);
            var x3 = x2 * Math.Cos(radZ) - y1 * Math.Sin(radZ);
            var y3 = x2 * Math.Sin(radZ) + y1 * Math.Cos(radZ);

            return new Vec3(worldPos.X + x3, worldPos.Y + y3, worldPos.Z + z2);
        }

        if (objMesh is not null && objMesh.Vertices.Count > 0)
        {
            var projVerts = new Point?[objMesh.Vertices.Count];
            var worldVerts = new Vec3[objMesh.Vertices.Count];
            for (int i = 0; i < objMesh.Vertices.Count; i++)
            {
                var wv = RotateModelVert(objMesh.Vertices[i], o.Scale.X, o.Scale.Y, o.Scale.Z);
                worldVerts[i] = wv;
                projVerts[i] = Project3D(wv);
            }

            var wirePen = isSelected
                ? new Pen(Brushes.DeepPink, 1.8)
                : new Pen(new SolidColorBrush(Color.FromArgb(120, baseCol.R, baseCol.G, baseCol.B)), 0.8);

            var faceBrush = new SolidColorBrush(Color.FromArgb(200, (byte)Math.Min(255, baseCol.R * 0.9), (byte)Math.Min(255, baseCol.G * 0.9), (byte)Math.Min(255, baseCol.B * 0.9)));

            foreach (var face in objMesh.Faces)
            {
                if (face.Length < 3) continue;
                bool allVisible = true;
                for (int i = 0; i < face.Length; i++)
                {
                    if (face[i] < 0 || face[i] >= projVerts.Length || !projVerts[face[i]].HasValue)
                    {
                        allVisible = false;
                        break;
                    }
                }
                if (!allVisible) continue;

                var geom = new StreamGeometry();
                using (var gc = geom.Open())
                {
                    gc.BeginFigure(projVerts[face[0]]!.Value, true);
                    for (int i = 1; i < face.Length; i++)
                    {
                        gc.LineTo(projVerts[face[i]]!.Value);
                    }
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(faceBrush, wirePen, geom);
            }
        }
        else
        {
            var hX = Math.Abs(o.Scale.X) * 0.7;
            var hY = Math.Abs(o.Scale.Y) * 0.9;
            var hZ = Math.Abs(o.Scale.Z) * 0.7;

            Vec3[] verts =
            [
                new(0, hY, 0),
                new(hX, 0, 0),
                new(0, 0, hZ),
                new(-hX, 0, 0),
                new(0, 0, -hZ),
                new(0, -hY, 0)
            ];

            var worldV = new Vec3[6];
            var projV = new Point?[6];
            for (int i = 0; i < 6; i++)
            {
                worldV[i] = RotateModelVert(verts[i], 1, 1, 1);
                projV[i] = Project3D(worldV[i]);
            }

            int[][] faces =
            [
                [0, 1, 2], [0, 2, 3], [0, 3, 4], [0, 4, 1],
                [5, 2, 1], [5, 3, 2], [5, 4, 3], [5, 1, 4]
            ];

            double[] faceShades = [1.0, 0.85, 0.7, 0.85, 0.65, 0.5, 0.55, 0.6];

            for (int i = 0; i < faces.Length; i++)
            {
                var f = faces[i];
                if (!projV[f[0]].HasValue || !projV[f[1]].HasValue || !projV[f[2]].HasValue) continue;

                var p0 = projV[f[0]]!.Value;
                var p1 = projV[f[1]]!.Value;
                var p2 = projV[f[2]]!.Value;
                var normalZ = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                if (normalZ < 0) continue;

                var shade = faceShades[i];
                var fCol = Color.FromArgb(235, (byte)(baseCol.R * shade), (byte)(baseCol.G * shade), (byte)(baseCol.B * shade));
                var fBrush = new SolidColorBrush(fCol);
                var fPen = isSelected ? new Pen(Brushes.DeepPink, 1.8) : new Pen(new SolidColorBrush(Color.Parse("#44000000")), 1);

                var geom = new StreamGeometry();
                using (var gc = geom.Open())
                {
                    gc.BeginFigure(p0, true);
                    gc.LineTo(p1);
                    gc.LineTo(p2);
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(fBrush, fPen, geom);
            }
        }

        var topWorld = new Vec3(worldPos.X, worldPos.Y + Math.Abs(o.Scale.Y) * 1.1, worldPos.Z);
        var topPt = Project3D(topWorld);
        if (topPt.HasValue)
        {
            var badgeText = new FormattedText(
                $"🧊 {vm.Name}",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Inter,system-ui,sans-serif"),
                11,
                isSelected ? Brushes.DeepPink : new SolidColorBrush(Color.Parse("#4A2929"))
            );
            var pillRect = new Rect(topPt.Value.X - badgeText.Width / 2 - 4, topPt.Value.Y - 14, badgeText.Width + 8, 16);
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#EEFCE8EC")), pillRect, 4);
            ctx.DrawRectangle(new Pen(isSelected ? Brushes.DeepPink : new SolidColorBrush(Color.Parse("#DDFFB6C1")), 1), pillRect, 4);
            ctx.DrawText(badgeText, new Point(pillRect.X + 4, pillRect.Y + 1));
        }

        if (isSelected)
        {
            var centerPt = Project3D(worldPos);
            if (centerPt.HasValue)
            {
                var selPen = new Pen(Brushes.DeepPink, 2, new DashStyle([4, 4], 0));
                double boundSz = Math.Max(24, (Math.Max(o.Scale.X, Math.Max(o.Scale.Y, o.Scale.Z)) * 400) / depth);
                ctx.DrawRectangle(null, selPen, new Rect(centerPt.Value.X - boundSz / 2, centerPt.Value.Y - boundSz / 2, boundSz, boundSz), 4, 4);
            }
        }
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

        // Во время полёта (ПКМ) WASD/QE управляют камерой — режимы гизмо не переключаем.
        // Остальные горячие клавиши (Q/W/E/R/F, Delete, Ctrl/⌘+…) обрабатывает окно редактора,
        // чтобы они работали одинаково из любой панели.
        if (_isFlying && e.Key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E)
            e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _heldKeys.Remove(e.Key);
    }
}
