using Avalonia.Controls;
using Avalonia.Interactivity;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public partial class InspectorView : UserControl
{
    public InspectorView() => InitializeComponent();

    void OnColorPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex } && DataContext is InspectorPanelVM vm)
        {
            vm.Editor.SetColorPreset(hex);
        }
    }
}
