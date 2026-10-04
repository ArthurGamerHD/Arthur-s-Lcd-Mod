using System.Text.RegularExpressions;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class LegacyGuiGateTests
{
    static readonly Regex LegacySymbol = new(
        @"\b(LcdMod\.Client\.Gui\.Tooltip|InteractiveTooltip|ControlModelBase|RectangleControl|StyleTree|DefaultStyleBuilder|ControlAnimationExtensions|InteractiveList|InteractiveEntries|RendersInteractiveEntriesInGetSprites|RenderInteractiveEntryVisuals|OnCollectOverlayEntries|OnControlPointerEnter|OnControlPointerLeave|OnControlClick|TryResolveClickableAtCursor|TryClickAtCursor|HasTooltipInputAtCursor|AppControlHintFtueTip|AddLogicalChild)\b|ControlsTemplates\.(Basic|Panels|Inputs|Lists|Progress|Custom)\b",
        RegexOptions.Compiled);

    [Fact]
    public void ClientSource_DoesNotReferenceLegacyGui()
    {
        var root = FindRepositoryRoot();
        var violations = Directory.EnumerateFiles(Path.Combine(root, "Arthur-s-Lcd-Mod", "Client"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 })
                .Where(item => LegacySymbol.IsMatch(item.Line)))
            .Select(item => Path.GetRelativePath(root, item.Path) + ":" + item.Number + ": " + item.Line.Trim())
            .ToArray();

        Assert.True(violations.Length == 0,
            "Legacy GUI references found; route input and drawing through the retained view instead:\n" +
            string.Join("\n", violations));
    }

    static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.Ordinal) ||
               normalized.Contains("/bin/", StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
