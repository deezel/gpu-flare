using System;
using FLARE.Core;

namespace FLARE.UI.Models;

public class AppSettings
{
    public int MaxDays { get; set; } = FlareOptions.DefaultMaxDays;
    public int MaxEvents { get; set; } = FlareOptions.DefaultMaxEvents;
    public bool SortDescending { get; set; } = true;

    // Default on: shared reports (forum, Discord, support threads) are post-first,
    // read-later — leaking UUID + computer name should not be the easiest path.
    public bool RedactIdentifiers { get; set; } = true;

    public DateTime? SinceDate { get; set; }

    public int MaxLiveKernelDumps { get; set; } = FlareOptions.DefaultMaxLiveKernelDumps;
}
