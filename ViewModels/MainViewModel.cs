using Avalonia.Platform.Storage;
using LensCleaner.Models;

namespace LensCleaner.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; } = new ChooseFolderViewModel();

    public void OpenLoadingSortingView(IStorageFolder folder) =>
        CurrentPage = new LoadingSortingViewModel(folder, this);

    public void OpenSortingView(Photo[] photos) =>
        CurrentPage = new SortingViewModel(photos);
}
