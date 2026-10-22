using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moewshare.Engine;

public enum ProjectKind { TwoD, ThreeD }

public static class ObjectTypes
{
    public const string Box = "box", Sphere = "sphere", Cylinder = "cylinder", Plane = "plane", Light = "light", Sprite = "sprite";
    public const string Canvas = "ui_canvas", UiText = "ui_text", UiImage = "ui_image", UiButton = "ui_button";
    public static readonly string[] WorldTypes = [Box, Sphere, Cylinder, Plane, Light, Sprite];
    public static readonly string[] UiTypes = [Canvas, UiText, UiImage, UiButton];
    public static readonly string[] All = [Box, Sphere, Cylinder, Plane, Light, Sprite, Canvas, UiText, UiImage, UiButton];
}

public static class ComponentTypes
{
    public const string BoxCollider = "BoxCollider";
    public const string SphereCollider = "SphereCollider";
    public const string Rigidbody = "Rigidbody";
    public const string TriggerZone = "TriggerZone";
    public const string PlatformerController = "PlatformerController";
    public const string TopDownController = "TopDownController";
    public const string Spin = "Spin";
    public const string Patrol = "Patrol";
    public const string SpriteRenderer = "SpriteRenderer";
    public const string LightSource = "LightSource";
}

public sealed record ComponentDescriptor(
    string Type,
    string Name,
    string Category,
    string Icon,
    string Description
);

public static class ComponentCatalog
{
    public static readonly ComponentDescriptor[] All =
    [
        new(ComponentTypes.BoxCollider, "Box Collider", "Физика", "🧱", "Трёхмерная коллизия-куб. Блокирует прохождение других объектов."),
        new(ComponentTypes.SphereCollider, "Sphere Collider", "Физика", "🔮", "Сферическая коллизия для круглых объектов и мячей."),
        new(ComponentTypes.Rigidbody, "Rigidbody (Физика)", "Физика", "⚡", "Гравитация и физика твердого тела."),
        new(ComponentTypes.TriggerZone, "Trigger (Триггер)", "Физика", "🎯", "Зона обнаружения входа без физической блокировки движения."),

        new(ComponentTypes.PlatformerController, "2D Platformer", "Геймплей", "🏃", "Управление персонажем платформера: скорость, сила прыжка, гравитация."),
        new(ComponentTypes.TopDownController, "Top-Down Controller", "Геймплей", "🕹️", "Управление с видом сверху во всех 8 направлениях."),
        new(ComponentTypes.Spin, "Spin (Вращение)", "Геймплей", "🔄", "Постоянное плавное вращение вокруг выбранной оси со скоростью."),
        new(ComponentTypes.Patrol, "Patrol (Патруль)", "Геймплей", "↔️", "Автоматическое движение персонажа или платформы туда-сюда."),

        new(ComponentTypes.SpriteRenderer, "2D Sprite Renderer", "Графика", "🖼️", "2D спрайт с поддержкой прозрачности PNG без фоновых рамок."),
        new(ComponentTypes.LightSource, "Light Source", "Графика", "💡", "Точечный источник света с настраиваемым радиусом и яркостью.")
    ];
}

public static class DevicePresets
{
    public const string Desktop = "pc_16_9", MobilePortrait = "mobile_9_16", MobileLandscape = "mobile_19_9", Tablet = "tablet_4_3";
    public static readonly string[] All = [Desktop, MobilePortrait, MobileLandscape, Tablet];
}

public static class UiScaleModes
{
    public const string ScaleWithScreen = "scale_with_screen", ConstantPixelSize = "constant_pixel_size";
    public static readonly string[] All = [ScaleWithScreen, ConstantPixelSize];
}

public static class Behaviors
{
    public const string None = "none", Platformer = "platformer", TopDown = "topdown", ThirdPerson = "thirdperson", Spin = "spin";
    public static readonly string[] All = [None, Platformer, TopDown, ThirdPerson, Spin];
}

public static class CameraModes
{
    public const string Side2D = "side2d", TopDown = "topdown", Isometric = "isometric", Follow3D = "follow3d";
    public static readonly string[] All = [Side2D, TopDown, Isometric, Follow3D];
}

public static class MovePlanes
{
    public const string Auto = "auto", Xy2D = "xy", Xz3D = "xz";
    public static readonly string[] All = [Auto, Xy2D, Xz3D];
}

public sealed class ProjectConfig
{
    public int FormatVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ProjectKind Kind { get; set; }
    public string? EngineVersion { get; set; }
    public string TemplateId { get; set; } = "";
    public string StartScene { get; set; } = "Scenes/Main.scene.json";
    /// <summary>Откуда рантайм берёт Babylon.js, если нет локального lib/babylon.js.</summary>
    public string BabylonUrl { get; set; } = "https://cdnjs.cloudflare.com/ajax/libs/babylonjs/7.54.3/babylon.js";
    public DateTime Created { get; set; } = DateTime.Now;
}

public sealed class Vec3
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public Vec3() { }
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
}

public sealed class SceneObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Object";
    public string Type { get; set; } = ObjectTypes.Box;
    public Vec3 Position { get; set; } = new();
    /// <summary>Градусы.</summary>
    public Vec3 Rotation { get; set; } = new();
    public Vec3 Scale { get; set; } = new(1, 1, 1);
    public string Color { get; set; } = "#F7CDD5";
    public string? Texture { get; set; }
    /// <summary>Участвует в столкновениях (AABB, без учёта поворота).</summary>
    public bool Solid { get; set; }
    public string Behavior { get; set; } = Behaviors.None;
    /// <summary>Id родительского объекта для связки (как в Unity).</summary>
    public string? ParentId { get; set; }
    /// <summary>Текст для UI-элементов (ui_text, ui_button).</summary>
    public string? UiText { get; set; }
    /// <summary>Размер шрифта для UI текста/кнопки.</summary>
    public int UiFontSize { get; set; } = 18;
    /// <summary>Профиль целевого устройства для Canvas (pc_16_9, mobile_9_16, mobile_19_9, tablet_4_3).</summary>
    public string UiDevicePreset { get; set; } = DevicePresets.Desktop;
    /// <summary>Режим масштабирования UI Canvas (scale_with_screen, constant_pixel_size).</summary>
    public string UiScaleMode { get; set; } = UiScaleModes.ScaleWithScreen;
    /// <summary>Действие кнопки (jump, restart, alert).</summary>
    public string? UiAction { get; set; }

    // === Настройки компонентов ===
    /// <summary>Скорость бега для контроллеров (Platformer, Top-Down).</summary>
    public double Speed { get; set; } = 8.0;
    /// <summary>Сила прыжка для платформера.</summary>
    public double JumpForce { get; set; } = 10.0;
    /// <summary>Гравитация для платформера/физики.</summary>
    public double Gravity { get; set; } = 25.0;
    /// <summary>Скорость постоянного вращения (град/сек).</summary>
    public double SpinSpeed { get; set; } = 90.0;
    /// <summary>Является ли коллайдер триггером (пропускает сквозь себя).</summary>
    public bool IsTrigger { get; set; }
    /// <summary>Биллборд (спрайт всегда поворачивается к камере).</summary>
    public bool Billboard { get; set; } = true;
    /// <summary>Список активных компонентов на объекте.</summary>
    public List<string> Components { get; set; } = [];
    /// <summary>Отображается ли объект на сцене (глазик).</summary>
    public bool IsVisible { get; set; } = true;
    /// <summary>Заблокирован ли объект от случайных изменений в редакторе (замок).</summary>
    public bool IsLocked { get; set; } = false;
    /// <summary>Индивидуальный визуальный граф логики этого конкретного объекта.</summary>
    public Events.EventGraph? EventGraph { get; set; }
}

public sealed class CameraSettings
{
    public string Mode { get; set; } = CameraModes.Follow3D;
    public bool Follow { get; set; } = true;
    public string? FollowTargetId { get; set; }
    public double Zoom { get; set; } = 14;
    /// <summary>Угол наклона камеры в градусах (0 = сбоку 2D, 45 = изометрия, 90 = строго сверху вниз).</summary>
    public double Pitch { get; set; } = 45;
    /// <summary>Горизонтальный поворот камеры в градусах (0..360).</summary>
    public double Yaw { get; set; } = 0;
    /// <summary>Смещение камеры по высоте (Y).</summary>
    public double OffsetY { get; set; } = 0;
    /// <summary>Смещение камеры по глубине (Z).</summary>
    public double OffsetZ { get; set; } = 0;
    /// <summary>Плоскость перемещения для TopDown: auto, xy, xz.</summary>
    public string MovePlane { get; set; } = MovePlanes.Auto;
}

public sealed class SceneDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Main";
    public string Background { get; set; } = "#FFF4F3";
    public CameraSettings Camera { get; set; } = new();
    public List<SceneObject> Objects { get; set; } = [];
    /// <summary>Визуальный граф игровой логики и событий Moewshare Event System.</summary>
    public Events.EventGraph? EventGraph { get; set; }
}

public sealed record EngineResult(bool Ok, string? Error = null)
{
    public static EngineResult Success { get; } = new(true);
    public static EngineResult Fail(string e) => new(false, e);
}

public sealed record EngineResult<T>(bool Ok, T? Value, string? Error = null)
{
    public static EngineResult<T> Success(T v) => new(true, v);
    public static EngineResult<T> Fail(string e) => new(false, default, e);
}

public static class EngineJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T?> TryReadAsync<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            await using var s = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(s, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Атомарная запись: во временный файл, затем замена.</summary>
    public static async Task SaveAtomicAsync<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        await using (var s = File.Create(tmp)) await JsonSerializer.SerializeAsync(s, value, Options);
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }
}
