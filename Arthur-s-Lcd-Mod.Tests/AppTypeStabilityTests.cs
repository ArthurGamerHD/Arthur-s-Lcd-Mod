using System.Text.RegularExpressions;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class AppTypeStabilityTests
{
    static readonly IReadOnlyDictionary<string, int> StableAppIds = new Dictionary<string, int>
    {
        ["Antenna"] = 1,
        ["CargoFilled"] = 2,
        ["Gas"] = 3,
        ["Inventory"] = 4,
        ["InputOutput"] = 5,
        ["Generators"] = 6,
        ["EnergyDashboard"] = 7,
        ["PowerFilled"] = 8,
        ["Farm"] = 9,
        ["IntegrityMonitor"] = 10,
        ["DockingAlignment"] = 11,
        ["Radar"] = 12,
        ["StarMap"] = 13,
        ["Markdown"] = 14,
        ["Raycast"] = 15,
        ["DigitalPictureFrames"] = 16,
        ["Projector"] = 17,
        ["CargoActions"] = 18,
        ["NpcMarket"] = 19,
        ["ClockDashboard"] = 20,
        ["SessionDebug"] = 21,
        ["Thrust"] = 22,
        ["ButtonPanel"] = 23,
        ["VisibleTreeDebug"] = 24,
        ["RenderProxy"] = 25,
        ["Games"] = 26,
        ["BSoDTest"] = 27,
        ["MediaPlayer"] = 28,
        ["PlanetaryMap"] = 29,
        ["DefenseDashboard"] = 30
    };

    [Fact]
    public void AppIds_MatchFrozenManifestAndRemainUnique()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "Arthur-s-Lcd-Mod", "Client", "Apps");
        var pattern = new Regex(
            @"\[LcdApp\((?<id>\d+)(?:,\s*Name\s*=\s*""(?<override>[^""]+)"")?\)\][\s\S]*?\bclass\s+(?<class>[A-Za-z_][A-Za-z0-9_]*)\b",
            RegexOptions.Compiled);

        var discovered = new Dictionary<string, int>();
        foreach (var path in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in pattern.Matches(File.ReadAllText(path)))
            {
                var className = match.Groups["class"].Value;
                var name = match.Groups["override"].Success
                    ? match.Groups["override"].Value
                    : className.EndsWith("App", StringComparison.Ordinal)
                        ? className[..^3]
                        : className;
                discovered.Add(name, int.Parse(match.Groups["id"].Value));
            }
        }

        Assert.Equal(StableAppIds.Count, discovered.Count);
        foreach (var expected in StableAppIds)
            Assert.True(discovered.TryGetValue(expected.Key, out var actual) && actual == expected.Value,
                $"Stable app ID mismatch for {expected.Key}: expected {expected.Value}, got {(discovered.TryGetValue(expected.Key, out actual) ? actual : -1)}.");
        Assert.Equal(discovered.Count, discovered.Values.Distinct().Count());
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LcdMod.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
