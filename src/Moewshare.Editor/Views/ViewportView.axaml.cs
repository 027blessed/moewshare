using Avalonia.Controls;
using Avalonia.Interactivity;
using Moewshare.Editor.ViewModels;

namespace Moewshare.Editor.Views;

public partial class ViewportView : UserControl
{
    public ViewportView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ViewportPanelVM vm)
        {
            PreviewControl.Attach(vm.Editor);
        }
    }

    void OnPerspectiveClick(object? sender, RoutedEventArgs e) => PreviewControl.SetViewPreset("perspective");
    void OnIsometricClick(object? sender, RoutedEventArgs e) => PreviewControl.SetViewPreset("isometric");
    void OnTopClick(object? sender, RoutedEventArgs e) => PreviewControl.SetViewPreset("top");
    void OnFrontClick(object? sender, RoutedEventArgs e) => PreviewControl.SetViewPreset("front");
}
