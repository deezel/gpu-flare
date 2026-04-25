using FLARE.UI.Models;
using FLARE.UI.Services;

namespace FLARE.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempPath;

    public SettingsServiceTests()
    {
        _tempPath = Path.Combine(
            Path.GetTempPath(),
            $"flare_settings_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { File.Delete(_tempPath); } catch { }
        try { File.Delete(_tempPath + ".corrupt"); } catch { }
    }

    [Fact]
    public void LoadSettings_MissingFile_ReturnsDefaults()
    {
        Assert.False(File.Exists(_tempPath));
        var svc = new SettingsService(_tempPath);

        var settings = svc.LoadSettings();

        Assert.Equal(365, settings.MaxDays);
        Assert.Equal(5000, settings.MaxEvents);
        Assert.True(settings.SortDescending);
        Assert.True(settings.RedactIdentifiers);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var svc = new SettingsService(_tempPath);
        var original = new AppSettings
        {
            MaxDays = 90,
            MaxEvents = 12345,
            SortDescending = false,
            RedactIdentifiers = true,
        };

        svc.SaveSettings(original);
        var loaded = new SettingsService(_tempPath).LoadSettings();

        Assert.Equal(original.MaxDays, loaded.MaxDays);
        Assert.Equal(original.MaxEvents, loaded.MaxEvents);
        Assert.Equal(original.SortDescending, loaded.SortDescending);
        Assert.Equal(original.RedactIdentifiers, loaded.RedactIdentifiers);
    }

    [Fact]
    public void LoadSettings_MalformedJson_ReturnsDefaultsQuarantinesFileAndSetsWarning()
    {
        File.WriteAllText(_tempPath, "{ this is not valid JSON");
        var svc = new SettingsService(_tempPath);

        var settings = svc.LoadSettings();

        Assert.Equal(365, settings.MaxDays);
        Assert.False(File.Exists(_tempPath), "corrupt file must be moved aside");
        Assert.True(File.Exists(_tempPath + ".corrupt"), "corrupt file must be preserved under .corrupt");
        Assert.NotNull(svc.LastLoadWarning);
        Assert.Contains(".corrupt", svc.LastLoadWarning);

        try { File.Delete(_tempPath + ".corrupt"); } catch { }
    }

    [Fact]
    public void LoadSettings_MalformedJson_OverwritesPriorCorruptFile()
    {
        File.WriteAllText(_tempPath + ".corrupt", "previous garbage");
        File.WriteAllText(_tempPath, "{ fresh garbage");
        var svc = new SettingsService(_tempPath);

        svc.LoadSettings();

        Assert.True(File.Exists(_tempPath + ".corrupt"));
        Assert.Contains("fresh garbage", File.ReadAllText(_tempPath + ".corrupt"));

        try { File.Delete(_tempPath + ".corrupt"); } catch { }
    }

    [Fact]
    public void LoadSettings_CleanLoad_LeavesLastLoadWarningNull()
    {
        var svc = new SettingsService(_tempPath);
        svc.SaveSettings(new AppSettings { MaxDays = 30 });

        var svc2 = new SettingsService(_tempPath);
        svc2.LoadSettings();

        Assert.Null(svc2.LastLoadWarning);
    }

    [Fact]
    public void LoadSettings_MissingFile_LeavesLastLoadWarningNull()
    {
        Assert.False(File.Exists(_tempPath));
        var svc = new SettingsService(_tempPath);

        svc.LoadSettings();

        Assert.Null(svc.LastLoadWarning);
    }

    [Fact]
    public void LoadSettings_UnknownFieldsTolerated()
    {
        File.WriteAllText(_tempPath,
            """{"OutputPath":"C:\\out","MaxDays":42,"CopyMinidumps":true,"CdbPath":"C:\\old\\cdb.exe","FutureField":"ignored"}""");
        var svc = new SettingsService(_tempPath);

        var settings = svc.LoadSettings();

        Assert.Equal(42, settings.MaxDays);
    }

    [Fact]
    public void SaveSettings_WritesIndentedJson()
    {
        var svc = new SettingsService(_tempPath);
        svc.SaveSettings(new AppSettings { MaxDays = 30 });

        var contents = File.ReadAllText(_tempPath);
        Assert.Contains("\n", contents);
        Assert.Contains("\"MaxDays\": 30", contents);
    }

    [Fact]
    public void SaveSettings_ReplacesExistingFileWithoutLeavingTempFiles()
    {
        File.WriteAllText(_tempPath, "{ this is not valid JSON");
        var svc = new SettingsService(_tempPath);

        svc.SaveSettings(new AppSettings { MaxDays = 77 });

        var loaded = new SettingsService(_tempPath).LoadSettings();
        Assert.Equal(77, loaded.MaxDays);

        var dir = Path.GetDirectoryName(_tempPath)!;
        var stem = Path.GetFileName(_tempPath);
        Assert.Empty(Directory.GetFiles(dir, $"{stem}.*.tmp"));
    }

    [Fact]
    public void SaveSettings_Success_LeavesLastSaveWarningNull()
    {
        var svc = new SettingsService(_tempPath);

        svc.SaveSettings(new AppSettings { MaxDays = 30 });

        Assert.Null(svc.LastSaveWarning);
    }

    [Fact]
    public void SaveSettings_TargetPathIsDirectory_SetsLastSaveWarning()
    {
        var dirAsFilePath = Path.Combine(Path.GetTempPath(), $"flare_settings_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dirAsFilePath);
        try
        {
            var svc = new SettingsService(dirAsFilePath);

            svc.SaveSettings(new AppSettings { MaxDays = 30 });

            Assert.NotNull(svc.LastSaveWarning);
            Assert.Contains("changes may not persist", svc.LastSaveWarning);
        }
        finally
        {
            try { Directory.Delete(dirAsFilePath, recursive: true); } catch { }
        }
    }
}
