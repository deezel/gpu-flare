namespace FLARE.Core;

public sealed class CollectionTruncation
{
    public int RequestedMaxDays { get; set; }
    public int MaxEventsCap { get; set; }

    public bool GpuErrorsResultCap { get; set; }
    public bool BsodResultCap { get; set; }
    public bool RebootResultCap { get; set; }
    public bool AppCrashesResultCap { get; set; }

    public bool Any =>
        GpuErrorsResultCap || BsodResultCap || RebootResultCap || AppCrashesResultCap;
}
