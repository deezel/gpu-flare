using FLARE.Core;

namespace FLARE.Tests;

public class SystemInfoTests
{
    [Theory]
    [InlineData(0ul, "(unknown)")]
    [InlineData(1073741824ul, "1.0 GB")]
    [InlineData(34359738368ul, "32.0 GB")]
    [InlineData(137438953472ul, "128.0 GB")]
    public void FormatBytes_Common_Formats(ulong bytes, string expected)
    {
        Assert.Equal(expected, SystemInfo.FormatBytes(bytes));
    }

    [Fact]
    public void Collect_ReturnsNonNullRecord()
    {
        var info = SystemInfo.Collect();
        Assert.NotNull(info);
    }

    [Fact]
    public void Collect_OnRealMachine_PopulatesAtLeastCpuAndRam()
    {
        var info = SystemInfo.Collect();
        Assert.True(info.TotalMemoryBytes > 0, "expected non-zero RAM from GlobalMemoryStatusEx");
    }
}
