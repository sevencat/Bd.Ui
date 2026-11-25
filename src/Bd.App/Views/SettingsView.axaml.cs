using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Bd.Core.ViewModels;

namespace Bd.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnBrowseClick(object? sender, RoutedEventArgs e) => _ = BrowseAsync();

    private async Task BrowseAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择包含 .beads 的仓库目录",
            AllowMultiple = false
        });

        if (folders.Count > 0 && DataContext is SettingsViewModel vm)
        {
            vm.RepoPath = folders[0].Path.LocalPath;
        }
    }
}
