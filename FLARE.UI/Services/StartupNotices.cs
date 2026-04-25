namespace FLARE.UI.Services;

// Collector for pre-UI failures (at present: FlareStorage migration). First-in
// wins — BottomStatusText shows one line, and the first failure is usually the
// root cause rather than the cascade.
public interface IStartupNotices
{
    string? FirstMessage { get; }
    void Add(string message);
}

public sealed class StartupNotices : IStartupNotices
{
    private string? _firstMessage;

    public string? FirstMessage => _firstMessage;

    public void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _firstMessage ??= message;
    }
}
