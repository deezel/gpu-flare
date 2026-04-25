using System.Collections.Generic;
using System.Linq;

namespace FLARE.Core;

internal static class ReportAnalysis
{
    internal const int StrongEvidenceMinErrors = 10;
    internal const double StrongEvidenceMinErrorsPerLocation = 4.0;
    internal const int StrongEvidenceMaxLocations = 4;
    internal const double StrongEvidenceMaxSmFraction = 0.05;

    internal enum LocalizationVerdict
    {
        Strong,
        SmallSample,
        TooSpread,
        TooManyLocations,
        MultiGpu,
        SmCountUnknown,
    }

    internal sealed record SmLocationCount(int Gpc, int Tpc, int Sm, int Count)
    {
        public string Key => $"GPC {Gpc}, TPC {Tpc}, SM {Sm}";
    }

    internal sealed record ErrorConcentration(
        IReadOnlyList<SmLocationCount> LocationsByFrequency,
        int ErrorsWithCoords,
        int ErrorsWithoutCoords,
        double ErrorsPerLocation,
        double AffectedSmFraction,
        bool SmCountKnown,
        bool SingleGpu,
        LocalizationVerdict Verdict)
    {
        public int UniqueLocations => LocationsByFrequency.Count;
        public bool SuggestsLocalizedFailure => Verdict == LocalizationVerdict.Strong;
        public bool IsWeakSingleGpuSignal =>
            Verdict is LocalizationVerdict.SmallSample or LocalizationVerdict.TooSpread or LocalizationVerdict.TooManyLocations;
    }

    internal static ErrorConcentration ComputeConcentration(
        IReadOnlyList<NvlddmkmError> errors, GpuInfo gpu)
    {
        var locations = new Dictionary<(int gpc, int tpc, int sm), int>();
        int withCoords = 0;
        int withoutCoords = 0;
        foreach (var e in errors)
        {
            if (e.Gpc.HasValue && e.Tpc.HasValue && e.Sm.HasValue)
            {
                withCoords++;
                var key = (e.Gpc.Value, e.Tpc.Value, e.Sm.Value);
                locations[key] = locations.TryGetValue(key, out var c) ? c + 1 : 1;
            }
            else
            {
                withoutCoords++;
            }
        }

        var ordered = locations
            .Select(kv => new SmLocationCount(kv.Key.gpc, kv.Key.tpc, kv.Key.sm, kv.Value))
            .OrderByDescending(l => l.Count)
            .ToList();

        bool smCountKnown = gpu.SmCount > 0;
        bool singleGpu = gpu.NvidiaDeviceCount <= 1;

        var errorsPerLocation = ordered.Count > 0 ? (double)withCoords / ordered.Count : 0.0;
        var affectedSmFraction = smCountKnown ? (double)ordered.Count / gpu.SmCount : 0.0;

        var verdict = ClassifyVerdict(withCoords, ordered.Count, singleGpu, gpu.SmCount);

        return new ErrorConcentration(
            ordered, withCoords, withoutCoords,
            errorsPerLocation, affectedSmFraction,
            smCountKnown, singleGpu, verdict);
    }

    private static LocalizationVerdict ClassifyVerdict(
        int withCoords, int locationCount, bool singleGpu, int smCount)
    {
        if (!singleGpu) return LocalizationVerdict.MultiGpu;
        if (smCount <= 0) return LocalizationVerdict.SmCountUnknown;
        if (withCoords < StrongEvidenceMinErrors) return LocalizationVerdict.SmallSample;

        var errorsPerLocation = locationCount > 0 ? (double)withCoords / locationCount : 0.0;
        if (errorsPerLocation < StrongEvidenceMinErrorsPerLocation)
            return LocalizationVerdict.TooSpread;

        return HasTightLocationFootprint(locationCount, smCount)
            ? LocalizationVerdict.Strong
            : LocalizationVerdict.TooManyLocations;
    }

    private static bool HasTightLocationFootprint(int locationCount, int smCount) =>
        locationCount <= StrongEvidenceMaxLocations &&
        (double)locationCount / smCount <= StrongEvidenceMaxSmFraction;
}
