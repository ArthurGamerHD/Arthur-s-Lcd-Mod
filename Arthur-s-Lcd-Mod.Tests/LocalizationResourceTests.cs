using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class LocalizationResourceTests
{
    static readonly Regex PlaceholderPattern = new(@"\{\d+(?:[^}]*)\}", RegexOptions.Compiled);
    static readonly Regex PrefixedLookupPattern = new(
        @"(?:LocHelper\.GetLoc|MyStringId\.GetOrCompute)\s*\(\s*MOD_PREFIX\s*\+\s*""(?<suffix>[A-Za-z0-9_]+)""",
        RegexOptions.Compiled);
    static readonly Regex FullLookupPattern = new(
        @"(?:LocHelper\.GetLoc|MyStringId\.GetOrCompute)\s*\(\s*""(?<key>LcdMod_[A-Za-z0-9_]+)""",
        RegexOptions.Compiled);
    static readonly Regex ConfigMetadataPattern = new(
        @"MOD_PREFIX\s*\+\s*""(?<suffix>[A-Za-z0-9_]+)""",
        RegexOptions.Compiled);
    static readonly Regex DocumentationLocValuePattern = new(
        @"<loc>\s*(?<key>LcdMod_[A-Za-z0-9_]+)\s*</loc>",
        RegexOptions.Compiled);
    static readonly Regex DocumentationLocAttributePattern = new(
        @"<loc\s+key\s*=\s*""(?<key>LcdMod_[A-Za-z0-9_]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void EveryLocale_HasExactlyTheBaseKeySet()
    {
        string localizationDirectory = FindLocalizationDirectory();
        ResourceFile baseResource = LoadResource(Path.Combine(localizationDirectory, "MyTexts.resx"));

        foreach (string localePath in Directory.EnumerateFiles(localizationDirectory, "MyTexts.*.resx"))
        {
            ResourceFile locale = LoadResource(localePath);
            Assert.Empty(locale.DuplicateKeys);
            Assert.Equal(
                baseResource.Values.Keys.OrderBy(key => key),
                locale.Values.Keys.OrderBy(key => key));
        }
    }

    [Fact]
    public void EveryLocale_PreservesFormatPlaceholders()
    {
        string localizationDirectory = FindLocalizationDirectory();
        ResourceFile baseResource = LoadResource(Path.Combine(localizationDirectory, "MyTexts.resx"));

        foreach (string localePath in Directory.EnumerateFiles(localizationDirectory, "MyTexts.*.resx"))
        {
            ResourceFile locale = LoadResource(localePath);
            foreach ((string key, string baseValue) in baseResource.Values)
            {
                Assert.Equal(
                    GetPlaceholders(baseValue),
                    GetPlaceholders(locale.Values[key]));
            }
        }
    }


    [Fact]
    public void StaticLocalizationLookups_ExistInBaseResource()
    {
        string repositoryRoot = FindRepositoryRoot();
        ResourceFile baseResource = LoadResource(Path.Combine(
            repositoryRoot,
            "Arthur-s-Lcd-Mod",
            "Content",
            "Data",
            "Localization",
            "MyTexts.resx"));

        var references = new List<(string Key, string Path)>();
        string sourceRoot = Path.Combine(repositoryRoot, "Arthur-s-Lcd-Mod");
        foreach (string sourcePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                                    !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            string source = File.ReadAllText(sourcePath);
            references.AddRange(PrefixedLookupPattern.Matches(source)
                .Select(match => ("LcdMod_" + match.Groups["suffix"].Value, sourcePath)));
            references.AddRange(FullLookupPattern.Matches(source)
                .Select(match => (match.Groups["key"].Value, sourcePath)));
            references.AddRange(DocumentationLocValuePattern.Matches(source)
                .Select(match => (match.Groups["key"].Value, sourcePath)));
            references.AddRange(DocumentationLocAttributePattern.Matches(source)
                .Select(match => (match.Groups["key"].Value, sourcePath)));
        }

        string configModelsPath = Path.Combine(
            sourceRoot,
            "Common",
            "Config",
            "Components",
            "ConfigComponentModels.cs");
        string configModels = File.ReadAllText(configModelsPath);
        references.AddRange(ConfigMetadataPattern.Matches(configModels)
            .Select(match => ("LcdMod_" + match.Groups["suffix"].Value, configModelsPath)));

        string[] missing = references
            .Where(reference => !baseResource.Values.ContainsKey(reference.Key))
            .Select(reference => $"{reference.Key} ({Path.GetRelativePath(repositoryRoot, reference.Path)})")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(reference => reference)
            .ToArray();

        Assert.Empty(missing);
    }


    [Fact]
    public void ChatCommands_DoNotUseHardcodedShowMessageText()
    {
        string chatCommandsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "Arthur-s-Lcd-Mod",
            "Client",
            "ChatCommands");

        var hardcodedMessages = new List<string>();
        var pattern = new Regex(
            @"MyAPIGateway\.Utilities\.ShowMessage\s*\(\s*[^,]+,\s*\$?\x22",
            RegexOptions.Compiled | RegexOptions.Singleline);

        foreach (string sourcePath in Directory.EnumerateFiles(chatCommandsDirectory, "*.cs"))
        {
            string source = File.ReadAllText(sourcePath);
            if (pattern.IsMatch(source))
                hardcodedMessages.Add(Path.GetFileName(sourcePath));
        }

        Assert.Empty(hardcodedMessages);
    }

    [Fact]
    public void BaseResource_HasNoDuplicateKeys()
    {
        ResourceFile baseResource = LoadResource(
            Path.Combine(FindLocalizationDirectory(), "MyTexts.resx"));

        Assert.Empty(baseResource.DuplicateKeys);
    }

    static string[] GetPlaceholders(string value)
    {
        return PlaceholderPattern.Matches(value ?? string.Empty)
            .Select(match => match.Value)
            .OrderBy(v => v)
            .ToArray();
    }

    static ResourceFile LoadResource(string path)
    {
        XDocument document = XDocument.Load(path);
        var entries = document.Root!
            .Elements("data")
            .Select(element => new
            {
                Key = (string)element.Attribute("name")!,
                Value = (string)element.Element("value") ?? string.Empty
            })
            .ToArray();

        string[] duplicateKeys = entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(key => key)
            .ToArray();

        return new ResourceFile(
            entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            duplicateKeys);
    }

    static string FindLocalizationDirectory()
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "Arthur-s-Lcd-Mod",
            "Content",
            "Data",
            "Localization");
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LcdMod.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    sealed record ResourceFile(
        IReadOnlyDictionary<string, string> Values,
        IReadOnlyList<string> DuplicateKeys);
}
