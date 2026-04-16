namespace FLARE.UI.Services;

public interface IFileDialogService
{
    string? SaveFileDialog(string filter, string? defaultFileName = null);
    string? SelectFolderDialog(string? initialDirectory = null);
}
