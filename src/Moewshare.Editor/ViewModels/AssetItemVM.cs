using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Moewshare.Editor.ViewModels;

public enum AssetKind
{
    Folder,
    Model3D,   // .obj, .fbx, .stl, .glb, .gltf
    Texture,   // .png, .jpg, .jpeg, .webp, .svg
    Audio,     // .mp3, .wav, .ogg
    Scene,     // .scene.json
    Other
}

public sealed partial class AssetItemVM : ObservableObject
{
    public EditorViewModel? Editor { get; init; }
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public bool IsDirectory { get; init; }
    public AssetKind Kind { get; init; }
    public string Extension { get; init; } = "";
    public string Icon { get; init; } = "📄";
    public string DisplaySize { get; init; } = "";

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
                OnPropertyChanged(nameof(ItemBackground));
            }
        }
    }

    static readonly IBrush _dropTargetActiveBrush = new SolidColorBrush(Color.Parse("#FF4B72"));
    static readonly IBrush _dropTargetBgBrush = new SolidColorBrush(Color.Parse("#FFF0F3"));
    static readonly IBrush _folderBgBrush = new SolidColorBrush(Color.Parse("#FFF8F9"));
    static readonly IBrush _defaultBgBrush = Brushes.Transparent;

    public IBrush DropTargetBrush => IsDropTarget ? _dropTargetActiveBrush : Brushes.Transparent;
    public Thickness DropTargetBorderThickness => IsDropTarget ? new Thickness(2) : new Thickness(0);
    public IBrush ItemBackground => IsDropTarget ? _dropTargetBgBrush : (IsDirectory ? _folderBgBrush : _defaultBgBrush);

    public bool IsModel => Kind == AssetKind.Model3D;
    public bool IsTexture => Kind == AssetKind.Texture;
    public bool IsAudio => Kind == AssetKind.Audio;
    public bool CanBePutOnScene => IsModel || IsTexture;

    [RelayCommand]
    public void OpenFolder()
    {
        if (IsDirectory && Editor is not null)
            Editor.NavigateIntoFolder(this);
    }

    [RelayCommand]
    public void PutOnScene()
    {
        if (CanBePutOnScene && Editor is not null)
            Editor.PutAssetOnSceneCommand.Execute(this);
    }

    [RelayCommand]
    public void Delete()
    {
        Editor?.DeleteAssetCommand.Execute(this);
    }
}
