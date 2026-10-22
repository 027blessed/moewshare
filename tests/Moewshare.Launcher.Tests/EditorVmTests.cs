using Moewshare.Editor.ViewModels;
using Moewshare.Engine;
using Xunit;

namespace Moewshare.Launcher.Tests;

public class MockEditorUi : IEditorUi
{
    public Task<string?> PickFileAsync(string title, string startDir) => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<bool> ConfirmAsync(string title, string message, string okText) => Task.FromResult(true);
    public Task MessageAsync(string title, string message) => Task.CompletedTask;
    public Task ShowAddObjectDialogAsync(EditorViewModel editor) => Task.CompletedTask;
    public Task ShowSettingsDialogAsync(EditorViewModel editor) => Task.CompletedTask;
    public void Close() { }
}

public class EditorVmTests : TempDirTest
{
    [Fact]
    public void Dock_panels_can_be_moved_and_reset()
    {
        var vm = new EditorViewModel(new MockEditorUi());
        var hierarchy = vm.AllPanels.First(p => p.Id == "hierarchy");
        Assert.Equal(DockZone.Left, hierarchy.Zone);
        Assert.Contains(hierarchy, vm.LeftPanels);

        // Move to Bottom
        hierarchy.MoveToBottomCommand.Execute(null);
        Assert.Equal(DockZone.Bottom, hierarchy.Zone);
        Assert.Contains(hierarchy, vm.BottomPanels);
        Assert.DoesNotContain(hierarchy, vm.LeftPanels);
        Assert.Equal(hierarchy, vm.ActiveBottomPanel);

        // Move to Right
        hierarchy.MoveToRightCommand.Execute(null);
        Assert.Equal(DockZone.Right, hierarchy.Zone);
        Assert.Contains(hierarchy, vm.RightPanels);

        // Reset Layout
        vm.ResetLayoutCommand.Execute(null);
        Assert.Equal(DockZone.Left, hierarchy.Zone);
        Assert.Contains(hierarchy, vm.LeftPanels);
    }

    [Fact]
    public void Gizmo_modes_switch_correctly()
    {
        var vm = new EditorViewModel(new MockEditorUi());
        Assert.Equal(GizmoMode.Translate, vm.CurrentGizmoMode);

        vm.SetGizmoMode("rotate");
        Assert.Equal(GizmoMode.Rotate, vm.CurrentGizmoMode);

        vm.SetGizmoMode("scale");
        Assert.Equal(GizmoMode.Scale, vm.CurrentGizmoMode);

        vm.SetGizmoMode("select");
        Assert.Equal(GizmoMode.Select, vm.CurrentGizmoMode);

        vm.SetGizmoMode("translate");
        Assert.Equal(GizmoMode.Translate, vm.CurrentGizmoMode);
    }

    [Fact]
    public async Task Object_duplication_and_color_preset_work()
    {
        var folder = Path.Combine(Dir, "GameProj");
        await ProjectScaffolder.CreateAsync(folder, "Game", "empty3d");

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        Assert.NotEmpty(vm.Objects);
        var originalCount = vm.Objects.Count;
        vm.Selected = vm.Objects[0];

        // Set color preset
        vm.SetColorPreset("#FFB6C1");
        Assert.Equal("#FFB6C1", vm.Selected.Color);

        // Duplicate
        vm.DuplicateSelected();
        Assert.Equal(originalCount + 1, vm.Objects.Count);
        Assert.Equal("#FFB6C1", vm.Selected.Color);
        Assert.StartsWith("Cube", vm.Selected.Name);

        // Save
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Console_logging_and_clear_work()
    {
        var vm = new EditorViewModel(new MockEditorUi());
        Assert.Empty(vm.ConsoleEntries);

        vm.Say("Test info", ConsoleLevel.Info);
        vm.Say("Test warning", ConsoleLevel.Warning);
        vm.Say("Test error", ConsoleLevel.Error);

        Assert.Equal(3, vm.ConsoleEntries.Count);
        Assert.Equal(1, vm.InfoCount);
        Assert.Equal(1, vm.WarningCount);
        Assert.Equal(1, vm.ErrorCount);

        vm.ClearConsole();
        Assert.Empty(vm.ConsoleEntries);
        Assert.Equal(0, vm.InfoCount);
        Assert.Equal(0, vm.WarningCount);
        Assert.Equal(0, vm.ErrorCount);
    }

    [Fact]
    public async Task Delete_current_project_deletes_folder_and_closes()
    {
        var folder = Path.Combine(Dir, "GameToDelete");
        await ProjectScaffolder.CreateAsync(folder, "GameToDelete", "empty3d");
        Assert.True(Directory.Exists(folder));

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        await vm.DeleteCurrentProjectCommand.ExecuteAsync(null);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task Tree_hierarchy_parenting_folding_and_chain_display_work()
    {
        var folder = Path.Combine(Dir, "HierarchyTreeTest");
        await ProjectScaffolder.CreateAsync(folder, "TreeGame", "platformer2d");

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        Assert.True(vm.Objects.Count >= 2);
        var parent = vm.Objects[0];
        var child = vm.Objects[1];

        // Attach child to parent
        vm.AttachTo(child, parent);

        Assert.Equal(parent.Id, child.ParentId);
        Assert.True(child.HasParent);
        Assert.Equal("└─ 🔗 ", child.ChainBranchIcon);
        Assert.True(parent.HasChildren);
        Assert.Equal("▼", parent.ExpandIcon);

        // In DisplayedHierarchyObjects: parent (depth 0), then child (depth 1)
        Assert.Contains(parent, vm.DisplayedHierarchyObjects);
        Assert.Contains(child, vm.DisplayedHierarchyObjects);
        Assert.Equal(0, parent.HierarchyDepth);
        Assert.Equal(1, child.HierarchyDepth);

        // Fold / collapse parent
        parent.ToggleExpanded();
        Assert.False(parent.IsExpanded);
        Assert.Equal("▶", parent.ExpandIcon);

        // Child should now be hidden in DisplayedHierarchyObjects!
        Assert.Contains(parent, vm.DisplayedHierarchyObjects);
        Assert.DoesNotContain(child, vm.DisplayedHierarchyObjects);

        // Unfold / expand parent again
        parent.ToggleExpanded();
        Assert.True(parent.IsExpanded);
        Assert.Equal("▼", parent.ExpandIcon);

        // Child should reappear!
        Assert.Contains(child, vm.DisplayedHierarchyObjects);
    }

    [Fact]
    public async Task Multi_selection_and_deselection_work()
    {
        var folder = Path.Combine(Dir, "MultiSelectTest");
        await ProjectScaffolder.CreateAsync(folder, "MultiGame", "platformer2d");

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        Assert.True(vm.Objects.Count >= 2);
        var obj1 = vm.Objects[0];
        var obj2 = vm.Objects[1];

        // Single select
        vm.ToggleSelection(obj1, multiSelect: false);
        Assert.True(obj1.IsSelected);
        Assert.False(obj2.IsSelected);
        Assert.Equal(1, vm.SelectionCount);
        Assert.False(vm.HasMultipleSelection);

        // Multi select with Cmd / Ctrl
        vm.ToggleSelection(obj2, multiSelect: true);
        Assert.True(obj1.IsSelected);
        Assert.True(obj2.IsSelected);
        Assert.Equal(2, vm.SelectionCount);
        Assert.True(vm.HasMultipleSelection);

        // Deselect all
        vm.DeselectAll();
        Assert.False(obj1.IsSelected);
        Assert.False(obj2.IsSelected);
        Assert.Equal(0, vm.SelectionCount);
        Assert.False(vm.HasSelection);
        Assert.Equal(1, vm.InspectorTabIndex); // Automatically shows scene settings
    }

    [Fact]
    public async Task Components_can_be_added_filtered_and_configured()
    {
        var folder = Path.Combine(Dir, "ComponentTest");
        await ProjectScaffolder.CreateAsync(folder, "CompGame", "empty3d");

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        Assert.NotEmpty(vm.Objects);
        var obj = vm.Objects[0];
        vm.Selected = obj;

        // Open Add Component and filter
        vm.OpenAddComponentCommand.Execute(null);
        Assert.True(vm.IsAddComponentOpen);
        Assert.NotEmpty(vm.FilteredComponents);

        // Filter by category
        vm.SelectComponentCategoryCommand.Execute("Физика");
        Assert.All(vm.FilteredComponents, c => Assert.Equal("Физика", c.Category));

        // Filter by search
        vm.ComponentSearch = "box";
        Assert.Contains(vm.FilteredComponents, c => c.Type == ComponentTypes.BoxCollider);

        // Add Platformer Controller
        vm.AttachComponentToSelected(ComponentTypes.PlatformerController);
        Assert.False(vm.IsAddComponentOpen);
        Assert.True(obj.HasPlatformerComponent);

        // Configure speed, jump force, gravity
        obj.Speed = 12.5;
        obj.JumpForce = 16.0;
        obj.Gravity = 30.0;
        Assert.Equal(12.5, obj.Speed);
        Assert.Equal(16.0, obj.JumpForce);
        Assert.Equal(30.0, obj.Gravity);

        // Add Spin component
        vm.AttachComponentToSelected(ComponentTypes.Spin);
        Assert.True(obj.HasSpinComponent);
        obj.SpinSpeed = 180;
        Assert.Equal(180, obj.SpinSpeed);

        // Remove Platformer
        obj.RemoveComponent(ComponentTypes.PlatformerController);
        Assert.False(obj.HasPlatformerComponent);
        Assert.True(obj.HasSpinComponent);
    }

    [Fact]
    public async Task Sprite_creation_and_customization_work()
    {
        var folder = Path.Combine(Dir, "SpriteTest");
        await ProjectScaffolder.CreateAsync(folder, "SpriteGame", "empty3d");

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        var initialCount = vm.Objects.Count;
        vm.AddSpriteCommand.Execute(null);

        Assert.Equal(initialCount + 1, vm.Objects.Count);
        var sprite = vm.Selected;
        Assert.NotNull(sprite);
        Assert.Equal(ObjectTypes.Sprite, sprite.Type);
        Assert.True(sprite.IsSprite);
        Assert.True(sprite.HasSpriteRendererComponent);
        Assert.True(sprite.Billboard);
        Assert.Equal("#FFFFFF", sprite.Color);

        // Assign a PNG texture
        sprite.Texture = "Assets/Textures/cat.png";
        Assert.Equal("Assets/Textures/cat.png", sprite.Texture);
    }

    [Fact]
    public async Task Object_lock_and_visibility_toggles_work()
    {
        var folder = Path.Combine(Dir, "LockVisTest");
        await ProjectScaffolder.CreateAsync(folder, "LockVisGame", "empty3d");

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        var obj = vm.Objects.First();
        Assert.True(obj.IsVisible);
        Assert.Equal("👁", obj.VisibilityIcon);
        Assert.False(obj.IsLocked);
        Assert.True(obj.IsNotLocked);
        Assert.Equal("🔓", obj.LockIcon);

        // Toggle visibility
        obj.ToggleVisibility();
        Assert.False(obj.IsVisible);
        Assert.Equal("🚫", obj.VisibilityIcon);
        Assert.True(obj.VisibilityOpacity < 1.0);

        obj.ToggleVisibility();
        Assert.True(obj.IsVisible);

        // Toggle lock
        obj.ToggleLock();
        Assert.True(obj.IsLocked);
        Assert.False(obj.IsNotLocked);
        Assert.Equal("🔒", obj.LockIcon);
        Assert.Equal(1.0, obj.LockOpacity);

        obj.ToggleLock();
        Assert.False(obj.IsLocked);
        Assert.True(obj.IsNotLocked);
    }

    [Fact]
    public async Task Add_from_template_supports_standalone_and_child_creation()
    {
        var folder = Path.Combine(Dir, "AddTemplateTest");
        await ProjectScaffolder.CreateAsync(folder, "AddTemplateGame", "empty3d");

        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);

        var initialCount = vm.Objects.Count;
        var parent = vm.Objects.First();
        vm.Selected = parent;

        // Add child sprite
        vm.AddFromTemplate("sprite", asChild: true);
        Assert.Equal(initialCount + 1, vm.Objects.Count);
        var child = vm.Selected;
        Assert.NotNull(child);
        Assert.Equal(parent.Id, child.ParentId);
        Assert.True(child.HasParent);

        // Add top-down player at root
        vm.AddFromTemplate("player_topdown", asChild: false);
        Assert.Equal(initialCount + 2, vm.Objects.Count);
        var player = vm.Selected;
        Assert.NotNull(player);
        Assert.Null(player.ParentId);
        Assert.Equal(Behaviors.TopDown, player.Behavior);
        Assert.True(player.HasTopDownComponent);
    }
}
