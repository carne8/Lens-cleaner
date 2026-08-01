using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace LensCleaner.Views;

public partial class MainWindow : Window
{
    private WindowState previousWindowState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs arg)
    {
        if (arg.Key != Key.F11) return;

        if (WindowState == WindowState.FullScreen)
            WindowState = previousWindowState;
        else
        {
            previousWindowState = WindowState;
            WindowState = WindowState.FullScreen;
        }

        arg.Handled = true;
    }

    private ViewModels.MainViewModel ViewModel => (ViewModels.MainViewModel)DataContext!;

    private async void OpenFolderDialog(object? sender, RoutedEventArgs e)
    {
        var startLocation = await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures);

        // Start async operation to open the dialog.
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            #if DEBUG
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync("./Test"),
            #else
            SuggestedStartLocation = startLocation,
            #endif
            Title = "Open a folder containing photos"
        });

        if (folders.Count == 0) return;
        ViewModel.OpenLoadingSortingView(folders[0]);
    }
}
