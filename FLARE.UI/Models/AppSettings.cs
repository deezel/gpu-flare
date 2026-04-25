namespace FLARE.UI.Models;

public class AppSettings
{
    public int MaxDays { get; set; } = 365;
    public int MaxEvents { get; set; } = 5000;
    public bool SortDescending { get; set; } = true;

    // Default on: shared reports (forum, Discord, support threads) are post-first,
    // read-later — leaking UUID + computer name should not be the easiest path.
    public bool RedactIdentifiers { get; set; } = true;
}
