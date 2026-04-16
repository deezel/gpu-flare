using FLARE.UI.Models;

namespace FLARE.UI.Services;

public interface ISettingsService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
}
