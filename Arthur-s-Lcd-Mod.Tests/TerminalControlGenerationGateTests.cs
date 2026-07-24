using System.Text.RegularExpressions;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class TerminalControlGenerationGateTests
{
    [Fact]
    public void BasicPropertyControls_AreAttributeGeneratedAndNotHandRegistered()
    {
        var root = FindRepositoryRoot();
        var models = File.ReadAllText(Path.Combine(
            root,
            "Arthur-s-Lcd-Mod",
            "Common",
            "Config",
            "Components",
            "ConfigComponentModels.cs"));
        var manager = File.ReadAllText(Path.Combine(
            root,
            "Arthur-s-Lcd-Mod",
            "Client",
            "Terminal",
            "TerminalManager.cs"));

        Assert.Equal(5, Regex.Matches(models, @"\[TerminalControlSlider\(").Count);
        Assert.Equal(6, Regex.Matches(models, @"\[TerminalControlSwitch\(").Count);
        Assert.Equal(3, Regex.Matches(models, @"\[TerminalControlColor\(").Count);
        Assert.Equal(3, Regex.Matches(models, @"RequiresCustomColor = true").Count);
        Assert.Contains("RefreshTerminalOnSet = true", models);
        Assert.Contains("GeneratedTerminalControlRegistry.AddTo(registrations);", manager);
        Assert.Contains("Controls.Where(control => control.Visible(block))", manager);

        var removedWrappers = new[]
        {
            "Client/Terminal/Controls/Interactive/SliderCursorScale.cs",
            "Client/Terminal/Controls/Scale/RaysPerTick.cs",
            "Client/Terminal/Controls/Proxy/SliderProxyX.cs",
            "Client/Terminal/Controls/Proxy/SliderProxyY.cs",
            "Client/Terminal/Controls/Proxy/SwitchProxyAutoAdjust.cs",
            "Client/Terminal/Controls/Generic/SliderImageChangeInterval.cs",
            "Client/Terminal/Controls/Cargo/SwitchShowConfigButton.cs",
            "Client/Terminal/Controls/Color/SwitchToggleColors.cs",
            "Client/Terminal/Controls/Color/ColorPickerHeader.cs",
            "Client/Terminal/Controls/Color/ColorPickerWarning.cs",
            "Client/Terminal/Controls/Color/ColorPickerError.cs",
            "Client/Terminal/Controls/Generic/SwitchToggleHeader.cs"
        };

        foreach (var relative in removedWrappers)
            Assert.False(File.Exists(Path.Combine(root, "Arthur-s-Lcd-Mod", relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void TerminalRegistrationIds_AreUniqueAcrossHandwrittenAndGeneratedControls()
    {
        var root = FindRepositoryRoot();
        var manager = File.ReadAllText(Path.Combine(
            root,
            "Arthur-s-Lcd-Mod",
            "Client",
            "Terminal",
            "TerminalManager.cs"));
        var models = File.ReadAllText(Path.Combine(
            root,
            "Arthur-s-Lcd-Mod",
            "Common",
            "Config",
            "Components",
            "ConfigComponentModels.cs"));

        var ids = Regex.Matches(manager, @"AddRegistration\(registrations,\s*(\d+)")
            .Cast<Match>()
            .Select(match => int.Parse(match.Groups[1].Value))
            .Concat(Regex.Matches(models, @"\[TerminalControl_(?:Slider|Switch|Color)\(\s*(\d+)")
                .Cast<Match>()
                .Select(match => int.Parse(match.Groups[1].Value)))
            .ToArray();

        var duplicates = ids.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        Assert.True(duplicates.Length == 0, "Duplicate terminal RegistrationIds: " + string.Join(", ", duplicates));
    }


    [Fact]
    public void ScriptScopedBooleanControl_RemainsHandwritten()
    {
        var root = FindRepositoryRoot();
        var drawLinesControl = Path.Combine(
            root,
            "Arthur-s-Lcd-Mod",
            "Client",
            "Terminal",
            "Controls",
            "Generic",
            "SwitchToggleLines.cs");
        var antenna = File.ReadAllText(Path.Combine(
            root,
            "Arthur-s-Lcd-Mod",
            "Client",
            "SurfaceScripts",
            "Antenna.cs"));

        Assert.True(File.Exists(drawLinesControl));
        Assert.Contains("IUsesTerminalControl<SwitchToggleLines>", antenna);
    }

    [Fact]
    public void SearchAndVanillaShadowPipeline_RemainStandalone()
    {
        var root = FindRepositoryRoot();
        var manager = File.ReadAllText(Path.Combine(
            root,
            "Arthur-s-Lcd-Mod",
            "Client",
            "Terminal",
            "TerminalManager.cs"));

        Assert.Contains("SearchScriptTextBox = CreateSearchScriptTextBox();", manager);
        Assert.Contains("InsertSearchScriptTextBox(block, controls);", manager);
        Assert.Contains("InsertScriptListShadow(block, controls);", manager);
        Assert.Contains("ReorderSurfaceControls(controls, script);", manager);
        Assert.DoesNotContain("SearchScriptTextBox", File.ReadAllText(Path.Combine(
            root,
            "LcdModCodeGenerator",
            "TerminalSettingControlsGenerator.cs")));
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
