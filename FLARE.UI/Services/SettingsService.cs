using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using FLARE.UI.Models;

namespace FLARE.UI.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? LastLoadWarning { get; private set; }
    public string? LastSaveWarning { get; private set; }

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "FLARE");
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");
    }

    internal SettingsService(string settingsPath)
    {
        var dir = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _settingsPath = settingsPath;
    }

    public AppSettings LoadSettings()
    {
        LastLoadWarning = null;
        if (!File.Exists(_settingsPath)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            // Preserve the bad file under .corrupt so a disk glitch or partial write
            // doesn't silently discard the user's settings choices on the
            // next debounced save. One rolling copy is enough for triage.
            var quarantined = QuarantineCorruptFile();
            LastLoadWarning = quarantined != null
                ? $"Settings file was unreadable ({ex.Message}); reset to defaults. Previous file preserved at {quarantined}."
                : $"Settings file was unreadable ({ex.Message}); reset to defaults.";
            Trace.TraceWarning("FLARE: corrupt settings at {0}: {1}", _settingsPath, ex.Message);
        }
        catch (Exception ex)
        {
            LastLoadWarning = $"Could not read settings file ({ex.Message}); using defaults for this run.";
            Trace.TraceWarning("FLARE: failed to load settings from {0}: {1}", _settingsPath, ex.Message);
        }
        return new AppSettings();
    }

    private string? QuarantineCorruptFile()
    {
        var quarantined = _settingsPath + ".corrupt";
        try
        {
            if (File.Exists(quarantined)) File.Delete(quarantined);
            File.Move(_settingsPath, quarantined);
            return quarantined;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("FLARE: could not quarantine corrupt settings file: {0}", ex.Message);
            return null;
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        string? tempPath = null;
        LastSaveWarning = null;
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            tempPath = Path.Combine(
                dir ?? Path.GetTempPath(),
                $"{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");

            File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, JsonOptions));

            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }
            tempPath = null;
        }
        catch (Exception ex)
        {
            LastSaveWarning = $"Could not save settings ({ex.Message}); changes may not persist.";
            Trace.TraceWarning("FLARE: failed to save settings to {0}: {1}", _settingsPath, ex.Message);
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }
        }
    }
}
