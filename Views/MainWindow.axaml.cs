using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace LensCleaner.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

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
