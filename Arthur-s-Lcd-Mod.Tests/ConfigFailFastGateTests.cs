using System.Text.RegularExpressions;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class ConfigFailFastGateTests
{
    static readonly Regex ConfigFallback = new(
        @"\bConfig\s*(?:==|!=)\s*null|\bConfig\?\.|\b(?:General|Color|Interaction|Filter|BlockSelection|ItemSelection|Power|Radar|StarMap|Diagnostic|Raycast|RenderProxy|Markdown|ButtonPanel|DigitalPictureFrames|CargoActions|NpcMarket|VisibleTreeDebug|ClockDashboard|TabContainer)Component\s*(?:==|!=)\s*null",
        RegexOptions.Compiled);

    [Fact]
    public void AppsAndConcreteSurfaces_DoNotHideMissingConfigOrRequiredComponents()
    {
        var root = FindRepositoryRoot();
        var appRoot = Path.Combine(root, "Arthur-s-Lcd-Mod", "Client", "Apps");
        var surfaceRoot = Path.Combine(root, "Arthur-s-Lcd-Mod", "Client", "SurfaceScripts");

        var files = Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(surfaceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(
                    Path.Combine("Abstract", "SurfaceScriptBase.cs"),
                    StringComparison.Ordinal)));

        var violations = files
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 })
                .Where(item => ConfigFallback.IsMatch(item.Line)))
            .Select(item => Path.GetRelativePath(root, item.Path) + ":" + item.Number + ": " + item.Line.Trim())
            .ToArray();

        Assert.True(violations.Length == 0,
            "Config fallbacks found in app/surface runtime code:\n" + string.Join("\n", violations));
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LcdMod.sln")) ||
                (Directory.Exists(Path.Combine(directory.FullName, "Arthur-s-Lcd-Mod")) &&
                 Directory.Exists(Path.Combine(directory.FullName, "LcdModCodeGenerator"))))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
