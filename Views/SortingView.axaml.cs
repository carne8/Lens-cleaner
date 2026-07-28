using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace LensCleaner.Views;

public partial class SortingView : UserControl
{
    private ViewModels.SortingViewModel ViewModel => (DataContext as ViewModels.SortingViewModel)!;

    public SortingView()
    {
        InitializeComponent();
        KeyDownEvent.AddClassHandler<TopLevel>(OnKeyDown, handledEventsToo: true, routes: RoutingStrategies.Tunnel);
    }

    private void OnKeyDown(TopLevel sender, KeyEventArgs arg)
    {
        if (arg.Key == Key.Right)
        {
            ViewModel.NextImage();
            arg.Handled = true;
        }
        if (arg.Key == Key.Left)
        {
            ViewModel.PreviousImage();
            arg.Handled = true;
        }
    }
}
