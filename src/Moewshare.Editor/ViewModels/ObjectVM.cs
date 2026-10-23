using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Engine;

namespace Moewshare.Editor.ViewModels;

/// <summary>Обёртка над SceneObject: правки сразу попадают в модель сцены.</summary>
public sealed partial class ObjectVM(SceneObject model, Action changed) : ObservableObject
{
    public SceneObject Model { get; } = model;
    void Touch([CallerMemberName] string? n = null) { OnPropertyChanged(n); changed(); }

    static readonly IBrush _selectedBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#FCE8EC"));
    static readonly IBrush _dropTargetBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#FF4B72"));
    static readonly IBrush _transparentBrush = Brushes.Transparent;

    public string Id => Model.Id;
    public string Name { get => Model.Name; set { if (Model.Name != value && !string.IsNullOrWhiteSpace(value)) { Model.Name = value.Trim(); Touch(); } } }
    public string Type => Model.Type;
    public string Display => $"{HierarchyPrefix}{Model.Name}  ·  {Model.Type}";

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(IsSelectedBrush));
                OnPropertyChanged(nameof(NameFontWeight));
            }
        }
    }

    public bool IsVisible
    {
        get => Model.IsVisible;
        set
        {
            if (Model.IsVisible != value)
            {
                Model.IsVisible = value;
                Touch();
                OnPropertyChanged(nameof(VisibilityIcon));
                OnPropertyChanged(nameof(VisibilityOpacity));
                OnPropertyChanged(nameof(VisibilityTooltip));
                _treeRefreshAction?.Invoke();
            }
        }
    }

    public string VisibilityIcon => IsVisible ? "👁" : "🚫";
    public double VisibilityOpacity => IsVisible ? 1.0 : 0.45;
    public string VisibilityTooltip => IsVisible ? "Скрыть объект (👁) — объект станет невидимым на сцене" : "Показать объект (🚫) — сделать видимым на сцене";

    [RelayCommand]
    public void ToggleVisibility() => IsVisible = !IsVisible;

    public bool HasEventGraph => Model.EventGraph is not null && Model.EventGraph.Nodes.Count > 0;
    public string EventGraphBadge => HasEventGraph ? $"⚡ Граф ({Model.EventGraph!.Nodes.Count} узлов)" : "⚡ Добавить логику объекта";

    public bool IsLocked
    {
        get => Model.IsLocked;
        set
        {
            if (Model.IsLocked != value)
            {
                Model.IsLocked = value;
                Touch();
                OnPropertyChanged(nameof(LockIcon));
                OnPropertyChanged(nameof(LockOpacity));
                OnPropertyChanged(nameof(LockTooltip));
                OnPropertyChanged(nameof(IsNotLocked));
                _treeRefreshAction?.Invoke();
            }
        }
    }

    public bool IsNotLocked => !IsLocked;
    public string LockIcon => IsLocked ? "🔒" : "🔓";
    public double LockOpacity => IsLocked ? 1.0 : 0.28;
    public string LockTooltip => IsLocked ? "Разблокировать объект (🔒) — разрешить перемещение и редактирование" : "Заблокировать объект (🔓) — защитить от случайных изменений";

    [RelayCommand]
    public void ToggleLock() => IsLocked = !IsLocked;

    bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value))
            {
                OnPropertyChanged(nameof(ExpandIcon));
                _treeRefreshAction?.Invoke();
            }
        }
    }

    bool _hasChildren;
    public bool HasChildren
    {
        get => _hasChildren;
        set
        {
            if (SetProperty(ref _hasChildren, value))
            {
                OnPropertyChanged(nameof(HasNoChildren));
                OnPropertyChanged(nameof(ExpandIcon));
                OnPropertyChanged(nameof(HierarchyIndent));
            }
        }
    }
    public bool HasNoChildren => !_hasChildren;

    int _hierarchyDepth;
    public int HierarchyDepth
    {
        get => _hierarchyDepth;
        set
        {
            if (SetProperty(ref _hierarchyDepth, value))
            {
                OnPropertyChanged(nameof(HierarchyIndent));
            }
        }
    }

    bool _isDropTarget;
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set
        {
            if (SetProperty(ref _isDropTarget, value))
            {
                OnPropertyChanged(nameof(DropTargetBrush));
                OnPropertyChanged(nameof(DropTargetBorderThickness));
            }
        }
    }

    [RelayCommand]
    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    public string ExpandIcon => HasChildren ? (IsExpanded ? "▼" : "▶") : "";
    public string ChainBranchIcon => HasParent ? "└─ 🔗 " : "";
    public Thickness HierarchyIndent => new(HierarchyDepth * 16, 1, 2, 1);

    public IBrush IsSelectedBrush => IsSelected ? _selectedBrush : _transparentBrush;
    public IBrush DropTargetBrush => IsDropTarget ? _dropTargetBrush : _transparentBrush;
    public Thickness DropTargetBorderThickness => IsDropTarget ? new Thickness(1.5) : new Thickness(0);
    public FontWeight NameFontWeight => IsSelected ? FontWeight.Bold : FontWeight.Medium;

    public string? ParentId
    {
        get => Model.ParentId;
        set
        {
            if (Model.ParentId != value)
            {
                Model.ParentId = string.IsNullOrWhiteSpace(value) ? null : value;
                Touch();
                OnPropertyChanged(nameof(HasParent));
                OnPropertyChanged(nameof(HierarchyIndent));
                OnPropertyChanged(nameof(HierarchyPrefix));
                OnPropertyChanged(nameof(ChainBranchIcon));
                OnPropertyChanged(nameof(Display));
                _treeRefreshAction?.Invoke();
            }
        }
    }

    Action<ObjectVM>? _selectAction;
    Action<ObjectVM>? _duplicateAction;
    Action<ObjectVM>? _deleteAction;
    Action? _openAddComponentAction;
    Action? _treeRefreshAction;

    public void SetSelectAction(Action<ObjectVM> act) => _selectAction = act;
    public void SetTreeRefreshAction(Action act) => _treeRefreshAction = act;

    public void SetActions(Action<ObjectVM> select, Action<ObjectVM> dup, Action<ObjectVM> del, Action openComp, Action treeRefresh)
    {
        _selectAction = select;
        _duplicateAction = dup;
        _deleteAction = del;
        _openAddComponentAction = openComp;
        _treeRefreshAction = treeRefresh;
    }

    [RelayCommand]
    public void SelectThis() => _selectAction?.Invoke(this);

    [RelayCommand]
    public void OpenAddComponentForThis()
    {
        SelectThis();
        _openAddComponentAction?.Invoke();
    }

    [RelayCommand]
    public void DuplicateThis()
    {
        SelectThis();
        _duplicateAction?.Invoke(this);
    }

    [RelayCommand]
    public void DeleteThis()
    {
        SelectThis();
        _deleteAction?.Invoke(this);
    }

    [RelayCommand]
    public void UnparentThis() => ParentId = null;

    public bool HasParent => !string.IsNullOrEmpty(Model.ParentId);

    public string HierarchyTypeIcon => Type switch
    {
        ObjectTypes.Canvas => "🖼️ [Canvas] ",
        ObjectTypes.Sprite => "🖼️ [Sprite] ",
        ObjectTypes.UiText => "📝 ",
        ObjectTypes.UiImage => "🖼️ ",
        ObjectTypes.UiButton => "🔘 ",
        ObjectTypes.Light => "💡 ",
        ObjectTypes.Sphere => "🔮 ",
        ObjectTypes.Cylinder => "🥫 ",
        ObjectTypes.Plane => "🟩 ",
        ObjectTypes.Model => "🧊 [3D] ",
        _ => "📦 "
    };

    public string HierarchyPrefix => HasParent ? "└─ 🔗 " : HierarchyTypeIcon;

    public bool IsUiElement => Type.StartsWith("ui_");
    public bool IsCanvas => Type == ObjectTypes.Canvas;
    public bool IsSprite => Type == ObjectTypes.Sprite;
    public bool IsUiComponent => Type is ObjectTypes.UiText or ObjectTypes.UiImage or ObjectTypes.UiButton;
    public bool IsUiText => Type == ObjectTypes.UiText;
    public bool IsUiImage => Type == ObjectTypes.UiImage;
    public bool IsUiButton => Type == ObjectTypes.UiButton;
    public bool IsWorldObject => !IsUiElement;
    public bool IsModel => Type == ObjectTypes.Model;

    public string? ModelPath
    {
        get => Model.ModelPath;
        set
        {
            if (Model.ModelPath != value)
            {
                Model.ModelPath = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                Touch();
                OnPropertyChanged(nameof(ModelName));
            }
        }
    }
    public string ModelName => string.IsNullOrEmpty(ModelPath) ? "(модель не выбрана)" : System.IO.Path.GetFileName(ModelPath);

    // === Component System ===
    public bool HasComponent(string type)
    {
        if (Model.Components.Contains(type)) return true;
        return type switch
        {
            ComponentTypes.PlatformerController => Model.Behavior == Behaviors.Platformer,
            ComponentTypes.TopDownController => Model.Behavior == Behaviors.TopDown,
            ComponentTypes.Spin => Model.Behavior == Behaviors.Spin,
            ComponentTypes.BoxCollider => Model.Solid && Model.Type == ObjectTypes.Box,
            ComponentTypes.SphereCollider => Model.Solid && Model.Type == ObjectTypes.Sphere,
            ComponentTypes.TriggerZone => Model.IsTrigger,
            ComponentTypes.SpriteRenderer => IsSprite || !string.IsNullOrEmpty(Model.Texture),
            ComponentTypes.LightSource => Type == ObjectTypes.Light,
            _ => false
        };
    }

    [RelayCommand]
    public void AddComponent(string type)
    {
        if (!Model.Components.Contains(type))
        {
            Model.Components.Add(type);
        }
        switch (type)
        {
            case ComponentTypes.PlatformerController:
                Model.Behavior = Behaviors.Platformer;
                break;
            case ComponentTypes.TopDownController:
                Model.Behavior = Behaviors.TopDown;
                break;
            case ComponentTypes.Spin:
                Model.Behavior = Behaviors.Spin;
                break;
            case ComponentTypes.BoxCollider:
            case ComponentTypes.SphereCollider:
            case ComponentTypes.Rigidbody:
                Model.Solid = true;
                break;
            case ComponentTypes.TriggerZone:
                Model.IsTrigger = true;
                Model.Solid = true;
                break;
            case ComponentTypes.SpriteRenderer:
                Model.Color = "#FFFFFF";
                break;
        }
        Touch();
        NotifyComponentsChanged();
    }

    [RelayCommand]
    public void RemoveComponent(string type)
    {
        Model.Components.Remove(type);
        switch (type)
        {
            case ComponentTypes.PlatformerController when Model.Behavior == Behaviors.Platformer:
            case ComponentTypes.TopDownController when Model.Behavior == Behaviors.TopDown:
            case ComponentTypes.Spin when Model.Behavior == Behaviors.Spin:
                Model.Behavior = Behaviors.None;
                break;
            case ComponentTypes.BoxCollider:
            case ComponentTypes.SphereCollider:
                Model.Solid = false;
                break;
            case ComponentTypes.TriggerZone:
                Model.IsTrigger = false;
                break;
        }
        Touch();
        NotifyComponentsChanged();
    }

    public void NotifyComponentsChanged()
    {
        OnPropertyChanged(nameof(HasPlatformerComponent));
        OnPropertyChanged(nameof(HasTopDownComponent));
        OnPropertyChanged(nameof(HasSpinComponent));
        OnPropertyChanged(nameof(HasBoxColliderComponent));
        OnPropertyChanged(nameof(HasSphereColliderComponent));
        OnPropertyChanged(nameof(HasRigidbodyComponent));
        OnPropertyChanged(nameof(HasTriggerZoneComponent));
        OnPropertyChanged(nameof(HasSpriteRendererComponent));
        OnPropertyChanged(nameof(HasLightSourceComponent));
    }

    public bool HasPlatformerComponent => HasComponent(ComponentTypes.PlatformerController);
    public bool HasTopDownComponent => HasComponent(ComponentTypes.TopDownController);
    public bool HasSpinComponent => HasComponent(ComponentTypes.Spin);
    public bool HasBoxColliderComponent => HasComponent(ComponentTypes.BoxCollider);
    public bool HasSphereColliderComponent => HasComponent(ComponentTypes.SphereCollider);
    public bool HasRigidbodyComponent => HasComponent(ComponentTypes.Rigidbody);
    public bool HasTriggerZoneComponent => HasComponent(ComponentTypes.TriggerZone);
    public bool HasSpriteRendererComponent => HasComponent(ComponentTypes.SpriteRenderer);
    public bool HasLightSourceComponent => HasComponent(ComponentTypes.LightSource);

    public double Speed
    {
        get => Model.Speed <= 0 ? 8.0 : Model.Speed;
        set { if (Model.Speed != value) { Model.Speed = Math.Max(1.0, Math.Min(50.0, value)); Touch(); } }
    }

    public double JumpForce
    {
        get => Model.JumpForce <= 0 ? 10.0 : Model.JumpForce;
        set { if (Model.JumpForce != value) { Model.JumpForce = Math.Max(1.0, Math.Min(50.0, value)); Touch(); } }
    }

    public double Gravity
    {
        get => Model.Gravity <= 0 ? 25.0 : Model.Gravity;
        set { if (Model.Gravity != value) { Model.Gravity = Math.Max(1.0, Math.Min(100.0, value)); Touch(); } }
    }

    public double SpinSpeed
    {
        get => Model.SpinSpeed == 0 ? 90.0 : Model.SpinSpeed;
        set { if (Model.SpinSpeed != value) { Model.SpinSpeed = value; Touch(); } }
    }

    public bool IsTrigger
    {
        get => Model.IsTrigger;
        set { if (Model.IsTrigger != value) { Model.IsTrigger = value; Touch(); } }
    }

    public bool Billboard
    {
        get => Model.Billboard;
        set { if (Model.Billboard != value) { Model.Billboard = value; Touch(); } }
    }

    public string UiScaleMode
    {
        get => Model.UiScaleMode ?? UiScaleModes.ScaleWithScreen;
        set { if (Model.UiScaleMode != value && value is not null) { Model.UiScaleMode = value; Touch(); } }
    }

    public string UiText
    {
        get => Model.UiText ?? "";
        set { if (Model.UiText != value) { Model.UiText = value; Touch(); } }
    }

    public int UiFontSize
    {
        get => Model.UiFontSize <= 0 ? 18 : Model.UiFontSize;
        set { if (Model.UiFontSize != value) { Model.UiFontSize = Math.Max(8, Math.Min(96, value)); Touch(); } }
    }

    public string UiDevicePreset
    {
        get => Model.UiDevicePreset ?? DevicePresets.Desktop;
        set
        {
            if (Model.UiDevicePreset != value && value is not null)
            {
                Model.UiDevicePreset = value;
                if (IsCanvas)
                {
                    var (resX, resY) = value switch
                    {
                        DevicePresets.MobilePortrait => (1080, 1920),
                        DevicePresets.MobileLandscape => (2400, 1080),
                        DevicePresets.Tablet => (2048, 1536),
                        _ => (1920, 1080)
                    };
                    Model.Scale.X = resX;
                    Model.Scale.Y = resY;
                    OnPropertyChanged(nameof(ScaleX));
                    OnPropertyChanged(nameof(ScaleY));
                }
                Touch();
            }
        }
    }

    public string UiAction
    {
        get => Model.UiAction ?? "jump";
        set { if (Model.UiAction != value && value is not null) { Model.UiAction = value; Touch(); } }
    }

    public double PosX { get => Model.Position.X; set { Model.Position.X = value; Touch(); } }
    public double PosY { get => Model.Position.Y; set { Model.Position.Y = value; Touch(); } }
    public double PosZ { get => Model.Position.Z; set { Model.Position.Z = value; Touch(); } }
    public double RotX { get => Model.Rotation.X; set { Model.Rotation.X = value; Touch(); } }
    public double RotY { get => Model.Rotation.Y; set { Model.Rotation.Y = value; Touch(); } }
    public double RotZ { get => Model.Rotation.Z; set { Model.Rotation.Z = value; Touch(); } }
    public double ScaleX { get => Model.Scale.X; set { Model.Scale.X = value; Touch(); } }
    public double ScaleY { get => Model.Scale.Y; set { Model.Scale.Y = value; Touch(); } }
    public double ScaleZ { get => Model.Scale.Z; set { Model.Scale.Z = value; Touch(); } }
    public bool Solid { get => Model.Solid; set { Model.Solid = value; Touch(); } }
    public string Behavior { get => Model.Behavior; set { if (value is not null) { Model.Behavior = value; Touch(); } } }

    public string Color
    {
        get => Model.Color;
        set
        {
            if (Regex.IsMatch(value ?? "", "^#[0-9a-fA-F]{6}$"))
            {
                Model.Color = value!;
                Touch();
                OnPropertyChanged(nameof(ColorHex));
                OnPropertyChanged(nameof(ColorR));
                OnPropertyChanged(nameof(ColorG));
                OnPropertyChanged(nameof(ColorB));
            }
        }
    }

    public string ColorHex => Model.Color;

    public byte ColorR
    {
        get => Avalonia.Media.Color.TryParse(Color, out var c) ? c.R : (byte)255;
        set => SetColorRgb(value, ColorG, ColorB);
    }

    public byte ColorG
    {
        get => Avalonia.Media.Color.TryParse(Color, out var c) ? c.G : (byte)255;
        set => SetColorRgb(ColorR, value, ColorB);
    }

    public byte ColorB
    {
        get => Avalonia.Media.Color.TryParse(Color, out var c) ? c.B : (byte)255;
        set => SetColorRgb(ColorR, ColorG, value);
    }

    public void RotateBy(double dx, double dy, double dz)
    {
        RotX = Math.Round((RotX + dx) % 360, 1);
        RotY = Math.Round((RotY + dy) % 360, 1);
        RotZ = Math.Round((RotZ + dz) % 360, 1);
    }

    public void ResetRotation()
    {
        RotX = 0; RotY = 0; RotZ = 0;
    }

    public string? Texture
    {
        get => Model.Texture;
        set { if (Model.Texture != value) { Model.Texture = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Touch(); } }
    }

    public void SetColorRgb(byte r, byte g, byte b)
    {
        Color = $"#{r:X2}{g:X2}{b:X2}";
    }

    public void NameChangedRefresh() => OnPropertyChanged(nameof(Display));
}
