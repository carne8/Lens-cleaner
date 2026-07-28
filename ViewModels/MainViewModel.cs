using Avalonia.Platform.Storage;

namespace LensCleaner.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; } = new ChooseFolderViewModel();

    public void OpenSortingView(IStorageFolder folder) =>
        CurrentPage = new SortingViewModel(folder);
}
