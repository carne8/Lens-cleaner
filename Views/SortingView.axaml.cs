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
        KeyUpEvent.AddClassHandler<TopLevel>(OnKeyUp, handledEventsToo: true, routes: RoutingStrategies.Tunnel);
    }

    private int leftPresses;
    private int rightPresses;

    private void OnKeyDown(TopLevel sender, KeyEventArgs arg)
    {
        if (arg.Key == Key.Right)
        {
            arg.Handled = true;
            ViewModel.NextImage();
            if (rightPresses == 0) ViewModel.LoadImage();
            rightPresses++;
        }
        if (arg.Key == Key.Left)
        {
            arg.Handled = true;
            ViewModel.PreviousImage();
            if (leftPresses == 0) ViewModel.LoadImage();
            leftPresses++;
        }
    }

    private void OnKeyUp(TopLevel sender, KeyEventArgs arg)
    {
        if (arg.Key == Key.Right)
        {
            arg.Handled = true;
            if (rightPresses > 1) ViewModel.LoadImage();
            rightPresses = 0;
        }
        if (arg.Key == Key.Left)
        {
            arg.Handled = true;
            if (leftPresses > 1) ViewModel.LoadImage();
            leftPresses = 0;
        }
    }
}
