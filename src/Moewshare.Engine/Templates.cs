namespace Moewshare.Engine;

public sealed record TemplateDefinition(string Id, string Name, ProjectKind Kind, Func<SceneDocument> Build);

public static class TemplateCatalog
{
    static SceneObject Obj(string name, string type, double x, double y, double z, double sx, double sy, double sz,
        string color, bool solid = false, string behavior = Behaviors.None, bool isTrigger = false) => new()
    {
        Name = name, Type = type, Position = new(x, y, z), Scale = new(sx, sy, sz),
        Color = color, Solid = solid, Behavior = behavior, IsTrigger = isTrigger
    };

    static SceneObject Box(string n, double x, double y, double z, double sx, double sy, double sz, string c, bool solid = false, string b = Behaviors.None)
        => Obj(n, ObjectTypes.Box, x, y, z, sx, sy, sz, c, solid, b);

    public static IReadOnlyList<TemplateDefinition> All { get; } =
    [
        new("clicker", "🐱 Кликер (Тапалка + Автоклик)", ProjectKind.TwoD, () => new SceneDocument
        {
            Camera = new() { Mode = CameraModes.Side2D, Zoom = 12, Pitch = 0 },
            Objects =
            [
                Obj("CatMascot", ObjectTypes.Sphere, 0, 0, 0, 3, 3, 3, "#FFB6C1", false, Behaviors.Spin),
                new SceneObject
                {
                    Id = "canvas_clicker",
                    Name = "Canvas",
                    Type = ObjectTypes.Canvas,
                    UiScaleMode = UiScaleModes.ConstantPixelSize,
                    UiDevicePreset = DevicePresets.Desktop
                },
                new SceneObject
                {
                    Id = "ui_score_text",
                    Name = "ScoreText",
                    Type = ObjectTypes.UiText,
                    ParentId = "canvas_clicker",
                    Position = new(0, 220, 0),
                    Scale = new(320, 50, 1),
                    Color = "#FFD700",
                    UiText = "Монеты: 0 🐾",
                    UiFontSize = 32
                },
                new SceneObject
                {
                    Id = "btn_tap",
                    Name = "ClickButton",
                    Type = ObjectTypes.UiButton,
                    ParentId = "canvas_clicker",
                    Position = new(0, -180, 0),
                    Scale = new(240, 60, 1),
                    Color = "#E05263",
                    UiText = "🐾 КЛИК! (+1)",
                    UiFontSize = 20
                }
            ],
            EventGraph = Events.EventGraph.CreateClickerPreset()
        }),
        new("coin_collector", "Сбор монет (10 монет + UI)", ProjectKind.TwoD, () => new SceneDocument
        {
            Camera = new() { Mode = CameraModes.Side2D, Zoom = 14, Pitch = 0 },
            Objects =
            [
                Box("Ground", 0, -4, 0, 32, 1, 1, "#F7CDD5", true),
                Box("Platform 1", -6, -1.5, 0, 4, 0.6, 1, "#E9A9B8", true),
                Box("Platform 2", 0, 0.5, 0, 4, 0.6, 1, "#E9A9B8", true),
                Box("Platform 3", 6, 2.5, 0, 4, 0.6, 1, "#E9A9B8", true),
                Box("Player", -12, -2.5, 0, 0.9, 0.9, 0.9, "#4A2929", false, Behaviors.Platformer),

                // 10 Coins with Spin and Trigger
                Obj("Coin 1", ObjectTypes.Sphere, -10, -2, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 2", ObjectTypes.Sphere, -7, -0.5, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 3", ObjectTypes.Sphere, -5, -0.5, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 4", ObjectTypes.Sphere, -2, -2, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 5", ObjectTypes.Sphere, 0, 1.5, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 6", ObjectTypes.Sphere, 2, -2, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 7", ObjectTypes.Sphere, 5, 3.5, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 8", ObjectTypes.Sphere, 7, 3.5, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 9", ObjectTypes.Sphere, 10, -2, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),
                Obj("Coin 10", ObjectTypes.Sphere, 12, -2, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin, isTrigger: true),

                // Canvas & Score UI
                new SceneObject
                {
                    Id = "canvas_main",
                    Name = "Canvas",
                    Type = ObjectTypes.Canvas,
                    UiScaleMode = UiScaleModes.ConstantPixelSize,
                    UiDevicePreset = DevicePresets.Desktop
                },
                new SceneObject
                {
                    Id = "ui_score_text",
                    Name = "ScoreText",
                    Type = ObjectTypes.UiText,
                    ParentId = "canvas_main",
                    Position = new(0, 300, 0),
                    Scale = new(240, 40, 1),
                    Color = "#FFD700",
                    UiText = "Монеты: 0 / 10",
                    UiFontSize = 26
                }
            ],
            EventGraph = Events.EventGraph.CreateCoinCollectorPreset()
        }),
        new("empty", "Пустой 2D-проект", ProjectKind.TwoD, () => new SceneDocument
        {
            Camera = new() { Mode = CameraModes.Side2D, Zoom = 12, Pitch = 0 },
            Objects = [Box("Cube", 0, 0, 0, 2, 2, 1, "#F7CDD5")]
        }),
        new("empty3d", "Пустой 3D-проект", ProjectKind.ThreeD, () => new SceneDocument
        {
            Camera = new() { Mode = CameraModes.Follow3D, Zoom = 10, Pitch = 45 },
            Objects = [Box("Cube", 0, 1, 0, 2, 2, 2, "#F7CDD5")]
        }),
        new("platformer2d", "2D-платформер", ProjectKind.TwoD, () => new SceneDocument
        {
            Camera = new() { Mode = CameraModes.Side2D, Zoom = 14, Pitch = 0 },
            Objects =
            [
                Box("Ground", 0, -4, 0, 30, 1, 1, "#F7CDD5", true),
                Box("Platform 1", -4, -1.5, 0, 4, 0.6, 1, "#E9A9B8", true),
                Box("Platform 2", 3, 0.5, 0, 4, 0.6, 1, "#E9A9B8", true),
                Box("Platform 3", 9, 2.5, 0, 4, 0.6, 1, "#E9A9B8", true),
                Box("Player", -10, -2.5, 0, 0.9, 0.9, 0.9, "#4A2929", false, Behaviors.Platformer),
                Obj("Coin 1", ObjectTypes.Sphere, -4, 0, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin),
                Obj("Coin 2", ObjectTypes.Sphere, 3, 2, 0, 0.5, 0.5, 0.5, "#F2B84B", false, Behaviors.Spin),
            ]
        }),
        new("topdown", "Игра с видом сверху", ProjectKind.TwoD, () => new SceneDocument
        {
            Camera = new() { Mode = CameraModes.TopDown, Zoom = 18, Pitch = 90 },
            Objects =
            [
                Box("Floor", 0, -0.5, 0, 24, 1, 16, "#FFFFFF"),
                Box("Wall N", 0, 0.5, 8.5, 26, 1, 1, "#E9A9B8", true),
                Box("Wall S", 0, 0.5, -8.5, 26, 1, 1, "#E9A9B8", true),
                Box("Wall W", -12.5, 0.5, 0, 1, 1, 18, "#E9A9B8", true),
                Box("Wall E", 12.5, 0.5, 0, 1, 1, 18, "#E9A9B8", true),
                Box("Crate 1", 4, 0.5, 2, 2, 1, 2, "#C99A7A", true),
                Box("Crate 2", -5, 0.5, -3, 2, 1, 2, "#C99A7A", true),
                Obj("Player", ObjectTypes.Sphere, 0, 0.5, 0, 0.9, 0.9, 0.9, "#4A2929", false, Behaviors.TopDown),
            ]
        }),
        new("adventure3d", "3D-игра", ProjectKind.ThreeD, () => new SceneDocument
        {
            Camera = new() { Mode = CameraModes.Follow3D, Zoom = 16 },
            Objects =
            [
                Box("Ground", 0, -0.5, 0, 40, 1, 40, "#CFEFD5", true),
                Box("Block 1", 4, 1, 3, 2, 2, 2, "#F7CDD5", true),
                Box("Block 2", -5, 0.5, 6, 3, 1, 3, "#E9A9B8", true),
                Box("Block 3", 8, 1.5, -6, 4, 3, 2, "#C99A7A", true),
                Obj("Pillar 1", ObjectTypes.Cylinder, -6, 2, -4, 1.5, 4, 1.5, "#AEC6CF", true),
                Obj("Pillar 2", ObjectTypes.Cylinder, 6, 2, 8, 1.5, 4, 1.5, "#AEC6CF", true),
                Obj("Player", ObjectTypes.Sphere, 0, 1, 0, 1, 1, 1, "#4A2929", false, Behaviors.ThirdPerson),
                new SceneObject { Name = "Sun", Type = ObjectTypes.Light, Position = new(6, 12, -4), Color = "#FFFFFF" },
            ]
        }),
    ];

    public static TemplateDefinition? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
