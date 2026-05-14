using System.Collections.Generic;
using System.IO;
using FLARE.Core;

namespace FLARE.Tests;

public class CdbFixtureTests
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Cdb");

    public static IEnumerable<object[]> Fixtures()
    {
        if (!Directory.Exists(FixturesDir)) yield break;
        foreach (var fixturePath in Directory.GetFiles(FixturesDir, "*.cdb.txt"))
        {
            if (fixturePath.EndsWith(".expected.txt")) continue;
            yield return new object[] { Path.GetFileName(fixturePath) };
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ExtractCdbSummary_RealWorldTranscript_MatchesExpected(string fixtureName)
    {
        var fixturePath = Path.Combine(FixturesDir, fixtureName);
        var expectedPath = fixturePath + ".expected.txt";
        var input = File.ReadAllText(fixturePath);
        var expected = File.ReadAllText(expectedPath);

        var actual = CdbRunner.ExtractCdbSummary(input, log: null, health: null);

        Assert.Equal(expected, actual ?? "");
    }
}
