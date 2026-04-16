using Microsoft.Win32;

namespace FLARE.UI.Services;

public class FileDialogService : IFileDialogService
{
    public string? SaveFileDialog(string filter, string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            FileName = defaultFileName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectFolderDialog(string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog();
        if (!string.IsNullOrEmpty(initialDirectory))
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
