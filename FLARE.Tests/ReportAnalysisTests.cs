using FLARE.Core;

namespace FLARE.Tests;

public class ReportAnalysisTests
{
    private static GpuInfo MakeGpu(int smCount, int nvidiaCount = 1) =>
        new(
            Name: "RTX Test",
            DriverVersion: "31.0.15.5222",
            VbiosVersion: "",
            Serial: "0",
            Uuid: "GPU-00000000-0000-0000-0000-000000000000",
            PciId: "00000000:01:00.0",
            SmCount: smCount,
            MemoryTotal: "12288 MiB",
            PcieCurrentGen: 4,
            PcieMaxGen: 4,
            PcieCurrentWidth: 16,
            PcieMaxWidth: 16,
            Bar1TotalMib: 256,
            NvidiaDeviceCount: nvidiaCount);

    private static NvlddmkmError ErrAt(int gpc, int tpc, int sm) =>
        new(DateTime.Now, 13, "test", gpc, tpc, sm, "Illegal Instruction Encoding");

    private static NvlddmkmError ErrNoCoords() =>
        new(DateTime.Now, 14, "test", null, null, null, "ECC Error");

    private static List<NvlddmkmError> Repeat(int count, int gpc, int tpc, int sm) =>
        Enumerable.Range(0, count).Select(_ => ErrAt(gpc, tpc, sm)).ToList();

    [Fact]
    public void MultiGpu_SuppressesLocalizationVerdict()
    {
        var gpu = MakeGpu(smCount: 100, nvidiaCount: 2);
        var errors = Repeat(12, 0, 0, 0);

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(ReportAnalysis.LocalizationVerdict.MultiGpu, c.Verdict);
        Assert.False(c.SingleGpu);
        Assert.False(c.SuggestsLocalizedFailure);
    }

    [Fact]
    public void SmCountZero_YieldsSmCountUnknown()
    {
        var gpu = MakeGpu(smCount: 0);
        var errors = Repeat(12, 0, 0, 0);

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(ReportAnalysis.LocalizationVerdict.SmCountUnknown, c.Verdict);
        Assert.False(c.SmCountKnown);
    }

    [Fact]
    public void BelowMinErrors_YieldsSmallSample()
    {
        var gpu = MakeGpu(smCount: 100);
        var errors = Repeat(ReportAnalysis.StrongEvidenceMinErrors - 1, 0, 0, 0);

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(ReportAnalysis.LocalizationVerdict.SmallSample, c.Verdict);
        Assert.False(c.SuggestsLocalizedFailure);
        Assert.True(c.IsWeakSingleGpuSignal);
    }

    [Fact]
    public void ErrorsPerLocationBelowThreshold_YieldsTooSpread()
    {
        var gpu = MakeGpu(smCount: 100);
        var errors = new List<NvlddmkmError>();
        for (int sm = 0; sm < 5; sm++)
            errors.AddRange(Repeat(2, 0, 0, sm));

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(ReportAnalysis.LocalizationVerdict.TooSpread, c.Verdict);
        Assert.True(c.IsWeakSingleGpuSignal);
        Assert.Equal(10, c.ErrorsWithCoords);
        Assert.Equal(5, c.UniqueLocations);
    }

    [Fact]
    public void TooManyLocationsByCount_YieldsTooManyLocations()
    {
        var gpu = MakeGpu(smCount: 100);
        var errors = new List<NvlddmkmError>();
        for (int sm = 0; sm < ReportAnalysis.StrongEvidenceMaxLocations + 1; sm++)
            errors.AddRange(Repeat((int)ReportAnalysis.StrongEvidenceMinErrorsPerLocation, 0, 0, sm));

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(ReportAnalysis.LocalizationVerdict.TooManyLocations, c.Verdict);
        Assert.True(c.IsWeakSingleGpuSignal);
    }

    [Fact]
    public void TooManyLocationsByFraction_YieldsTooManyLocations()
    {
        var gpu = MakeGpu(smCount: 50);
        var errors = new List<NvlddmkmError>();
        for (int sm = 0; sm < ReportAnalysis.StrongEvidenceMaxLocations; sm++)
            errors.AddRange(Repeat((int)ReportAnalysis.StrongEvidenceMinErrorsPerLocation, 0, 0, sm));

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(ReportAnalysis.LocalizationVerdict.TooManyLocations, c.Verdict);
        Assert.True(c.AffectedSmFraction > ReportAnalysis.StrongEvidenceMaxSmFraction);
    }

    [Fact]
    public void TightClusterMeetingAllThresholds_YieldsStrong()
    {
        var gpu = MakeGpu(smCount: 100);
        var errors = Repeat(6, 0, 0, 0).Concat(Repeat(6, 0, 0, 1)).ToList();

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(ReportAnalysis.LocalizationVerdict.Strong, c.Verdict);
        Assert.True(c.SuggestsLocalizedFailure);
        Assert.False(c.IsWeakSingleGpuSignal);
        Assert.Equal(12, c.ErrorsWithCoords);
        Assert.Equal(2, c.UniqueLocations);
    }

    [Fact]
    public void ErrorsWithoutCoordinates_AreCountedSeparately()
    {
        var gpu = MakeGpu(smCount: 100);
        var errors = Repeat(12, 0, 0, 0);
        errors.Add(ErrNoCoords());
        errors.Add(ErrNoCoords());

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Equal(12, c.ErrorsWithCoords);
        Assert.Equal(2, c.ErrorsWithoutCoords);
        Assert.Equal(ReportAnalysis.LocalizationVerdict.Strong, c.Verdict);
    }

    [Fact]
    public void LocationsAreOrderedByDescendingFrequency()
    {
        var gpu = MakeGpu(smCount: 100);
        var errors = Repeat(3, 0, 0, 1).Concat(Repeat(7, 0, 0, 2)).Concat(Repeat(5, 0, 0, 3)).ToList();

        var c = ReportAnalysis.ComputeConcentration(errors, gpu);

        Assert.Collection(
            c.LocationsByFrequency,
            l => Assert.Equal(7, l.Count),
            l => Assert.Equal(5, l.Count),
            l => Assert.Equal(3, l.Count));
    }
}
