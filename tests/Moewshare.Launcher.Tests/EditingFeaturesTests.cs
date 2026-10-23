using Moewshare.Editor.ViewModels;
using Moewshare.Engine;
using Moewshare.Engine.Events;
using Xunit;

namespace Moewshare.Launcher.Tests;

/// <summary>Отмена/повтор, буфер обмена объектов и групповые операции редактора событий.</summary>
public class EditingFeaturesTests : TempDirTest
{
    async Task<EditorViewModel> OpenProjectAsync(string template = "empty3d")
    {
        var folder = Path.Combine(Dir, "Proj_" + Guid.NewGuid().ToString("N")[..6]);
        await ProjectScaffolder.CreateAsync(folder, "Game", template);
        var vm = new EditorViewModel(new MockEditorUi());
        await vm.StartAsync(folder);
        return vm;
    }

    [Fact]
    public async Task Undo_and_redo_restore_scene_state()
    {
        var vm = await OpenProjectAsync();
        var count = vm.Objects.Count;
        vm.Selected = vm.Objects[0];
        var originalColor = vm.Selected.Color;

        vm.SetColorPreset("#123456");
        Assert.True(vm.CanUndo);

        vm.Undo();
        Assert.Equal(originalColor, vm.Objects[0].Color);
        Assert.True(vm.CanRedo);

        vm.Redo();
        Assert.Equal("#123456", vm.Objects[0].Color);

        await Task.Delay(700); // новая «серия» правок — отдельный шаг истории
        vm.Selected = vm.Objects[0];
        vm.DuplicateSelected();
        Assert.Equal(count + 1, vm.Objects.Count);
        vm.Undo();
        Assert.Equal(count, vm.Objects.Count);
    }

    [Fact]
    public async Task Copy_paste_creates_independent_objects()
    {
        var vm = await OpenProjectAsync();
        var count = vm.Objects.Count;
        vm.Selected = vm.Objects[0];
        vm.CopySelected();
        Assert.True(vm.HasObjectClipboard);

        vm.PasteObjects();
        Assert.Equal(count + 1, vm.Objects.Count);
        Assert.NotEqual(vm.Objects[0].Id, vm.Objects[^1].Id);
        Assert.NotEqual(vm.Objects[0].Name, vm.Objects[^1].Name);

        vm.SelectAll();
        Assert.Equal(vm.Objects.Count, vm.SelectionCount);
    }

    [Fact]
    public void Graph_remove_nodes_and_groups_are_consistent()
    {
        var g = EventGraph.CreateClickerPreset();
        var ids = g.Nodes.Take(3).Select(n => n.Id).ToList();
        var group = g.CreateGroup(ids, "Test", "#10B981");
        Assert.NotNull(group);
        Assert.Equal(3, group!.NodeIds.Count);

        var removed = g.RemoveNodes(ids.Take(2));
        Assert.Equal(2, removed);
        Assert.DoesNotContain(g.Connections, c => ids.Take(2).Contains(c.FromNodeId) || ids.Take(2).Contains(c.ToNodeId));
        Assert.Single(g.Groups[0].NodeIds);

        g.RemoveNode(ids[2]);
        Assert.Empty(g.Groups); // пустые группы удаляются автоматически
    }

    [Fact]
    public async Task Graph_vm_multi_select_group_copy_paste_and_delete()
    {
        var editor = await OpenProjectAsync();
        var graphVm = editor.GraphVM!;
        graphVm.LoadPresetClicker();
        var graph = graphVm.CurrentGraph;
        var total = graph.Nodes.Count;

        // Множественное выделение
        graphVm.SetSelection(graph.Nodes.Take(3));
        Assert.Equal(3, graphVm.SelectionCount);
        Assert.True(graphVm.HasMultipleSelection);

        // Группа
        graphVm.GroupSelection();
        Assert.Single(graph.Groups);
        Assert.Equal(3, graph.Groups[0].NodeIds.Count);

        // Копировать / вставить — новые Id, связи внутри фрагмента сохраняются
        graphVm.CopyNodes();
        graphVm.PasteNodes(1000, 1000);
        Assert.Equal(total + 3, graph.Nodes.Count);
        Assert.Equal(2, graph.Groups.Count);
        Assert.Equal(3, graphVm.SelectionCount);

        // Дублирование
        graphVm.DuplicateNodes();
        Assert.Equal(total + 6, graph.Nodes.Count);

        // Массовое удаление выделенного
        graphVm.DeleteSelectedNodes();
        Assert.Equal(total + 3, graph.Nodes.Count);
        Assert.Equal(0, graphVm.SelectionCount);

        // Выделить всё и удалить
        graphVm.SelectAllNodes();
        Assert.Equal(graph.Nodes.Count, graphVm.SelectionCount);
        graphVm.DeleteSelectedNodes();
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Connections);
        Assert.Empty(graph.Groups);
    }

    [Fact]
    public void Theme_preference_roundtrips()
    {
        var prev = Environment.GetEnvironmentVariable("MOEWSHARE_DATA");
        Environment.SetEnvironmentVariable("MOEWSHARE_DATA", Dir);
        try
        {
            Assert.Equal(UiThemeMode.System, UserPreferences.LoadTheme());
            UserPreferences.SaveTheme(UiThemeMode.Dark);
            Assert.Equal(UiThemeMode.Dark, UserPreferences.LoadTheme());
            Assert.Equal(UiThemeMode.Light, UserPreferences.ParseTheme("light"));
            Assert.Equal(UiThemeMode.System, UserPreferences.ParseTheme("garbage"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOEWSHARE_DATA", prev);
        }
    }
}
