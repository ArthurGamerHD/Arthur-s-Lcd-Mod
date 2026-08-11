using System.Text.RegularExpressions;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class LegacyRuntimeConfigGateTests
{
    static readonly Regex LegacySymbol = new(
        @"\b(ScreenConfig[A-Za-z0-9_]*|ComponentConfigAdapter|AppConfigContext|AppConfigBinding|AppConfig|AppConfigKindAttribute|AppConfigComponentAttribute|IConfigGenerator|ConfigGenerator|ConfigKind|IAppConfigKind[A-Za-z0-9_]*|IUses(?:General|Color|Interactive|Power)ConfigComponent|AppConfigMarkers|TryNormalizeKind|ChangeAppKind|EnsureSurfaceAppKind)\b",
        RegexOptions.Compiled);

    [Fact]
    public void RuntimeSource_DoesNotReferenceLegacyConfigHierarchy()
    {
        var root = FindRepositoryRoot();
        var violations = Directory.EnumerateFiles(Path.Combine(root, "Arthur-s-Lcd-Mod"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "LcdMod.CodeGenerator"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsFrozenMigrationFile(root, path))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { Path = path, Line = line, Number = index + 1 })
                .Where(item => LegacySymbol.IsMatch(item.Line)))
            .Select(item => Path.GetRelativePath(root, item.Path) + ":" + item.Number + ": " + item.Line.Trim())
            .ToArray();

        Assert.True(violations.Length == 0,
            "Legacy runtime config references found:\n" + string.Join("\n", violations));
    }

    static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LcdMod.sln")) ||
                (Directory.Exists(Path.Combine(directory.FullName, "Arthur-s-Lcd-Mod")) &&
                 Directory.Exists(Path.Combine(directory.FullName, "LcdMod.CodeGenerator"))))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    static bool IsFrozenMigrationFile(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative.StartsWith("Arthur-s-Lcd-Mod/Migration/", StringComparison.Ordinal);
    }
}
