using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Arthur_s_Lcd_Mod.Tests;

public class SceneLayerTests
{
    static string Repo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        return directory!.FullName;
    }

    static string AppSources(string repo) => string.Join("\n",
        Directory.EnumerateFiles(Path.Combine(repo, "Arthur-s-Lcd-Mod/Client/Apps"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/obj/") && !f.Contains("/bin/"))
            .Select(File.ReadAllText));

    // 3D scenes fill the whole surface under the title; installing one with SetContent puts it in the row below.
    [Fact]
    public void EveryMapSceneIsInstalledAsTheBackgroundLayer()
    {
        string repo = Repo();
        string sources = AppSources(repo);
        var scenes = Directory.EnumerateFiles(Path.Combine(repo, "Arthur-s-Lcd-Mod/Client/Apps"), "*.xaml", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Where(x => x.Contains("CartographyMapView"))
            .Select(x => Regex.Match(x, @"x:Class=""[\w.]*\.(\w+)""").Groups[1].Value)
            .ToList();

        Assert.Equal(new[] { "PlanetaryMapSceneView", "RadarSceneView", "StarMapSceneView" }, scenes.OrderBy(s => s));
        foreach (string scene in scenes)
        {
            Assert.Matches(new Regex(@"SetBackgroundContent\(new (\w+\.)*" + scene + @"\b"), sources);
            Assert.DoesNotMatch(new Regex(@"SetContent\(new (\w+\.)*" + scene + @"\b"), sources);
        }
    }

    [Fact]
    public void TheMediaVisualizerIsDrawnOnTheBackgroundLayer()
    {
        string repo = Repo();
        Assert.Matches(new Regex(@"SetBackgroundContent\(new (\w+\.)*MediaVisualizerView\b"), AppSources(repo));
        Assert.DoesNotContain("MediaVisualizer ", File.ReadAllText(
            Path.Combine(repo, "Arthur-s-Lcd-Mod/Client/Apps/MediaPlayer/MediaPlayerView.xaml")));
    }

    [Fact]
    public void PictureFramesFillTheSurfaceBeneathTheTitle()
    {
        string sources = AppSources(Repo());
        Assert.Matches(new Regex(@"SetBackgroundContent\(new (\w+\.)*DigitalPictureFramesView\b"), sources);
        Assert.DoesNotMatch(new Regex(@"SetContent\(new (\w+\.)*DigitalPictureFramesView\b"), sources);
    }

    // The menu bar used to overlap the top of the game; a docked menu now takes its own row above it.
    [Fact]
    public void GamesSitBelowTheDockedAdkMenuBar()
    {
        string repo = Repo();
        string sources = AppSources(repo);
        Assert.DoesNotMatch(new Regex(@"SetBackgroundContent\(new Games\.GamesView\b"), sources);
        Assert.Matches(new Regex(@"SetContent\(new Games\.GamesMenuView\b"), sources);
        string menu = File.ReadAllText(Path.Combine(repo, "Arthur-s-Lcd-Mod/Client/Apps/Games/GamesMenuView.xaml"));
        Assert.Matches(new Regex(@"<apps:GamesView Grid\.Row=""\{Binding GameRow\}"""), menu);
        string surface = File.ReadAllText(Path.Combine(repo, "Arthur-s-Lcd-Mod/Client/SurfaceScripts/Abstract/InteractiveSurfaceScript.cs"));
        Assert.DoesNotContain("reservedHeight", surface);
        Assert.DoesNotContain("GlobalMenu", surface);
    }

    [Fact]
    public void DoomFooterUsesBorderedKeysInARealWasdGrid()
    {
        string repo = Repo();
        var view = XDocument.Load(Path.Combine(repo, "Arthur-s-Lcd-Mod/Client/Apps/Games/GamesMenuView.xaml"));
        XNamespace controls = "https://github.com/ArthurGamerHD/Adk";

        var keycaps = view.Descendants(controls + "Border")
            .Where(border => (string)border.Attribute("Classes") == "doom-keycap")
            .ToList();
        Assert.Equal(15, keycaps.Count);
        Assert.All(keycaps, border => Assert.Single(border.Elements(controls + "TextBlock")));
        Assert.Contains(keycaps, border => (string)border.Element(controls + "TextBlock")?.Attribute("Text") == "P");
        Assert.Contains(view.Descendants(controls + "TextBlock"), text =>
            (string)text.Attribute("Text") == "Pause/Resume");

        var wasd = view.Descendants(controls + "Grid")
            .Single(grid => (string)grid.Attribute("ColumnDefinitions") == "Auto Auto Auto");
        var positions = wasd.Elements(controls + "Border").ToDictionary(
            border => (string)border.Element(controls + "TextBlock")?.Attribute("Text"),
            border => ((string)border.Attribute("Grid.Row"), (string)border.Attribute("Grid.Column")));
        Assert.Equal(("0", "1"), positions["W"]);
        Assert.Equal(("1", "0"), positions["A"]);
        Assert.Equal(("1", "1"), positions["S"]);
        Assert.Equal(("1", "2"), positions["D"]);

        string style = File.ReadAllText(Path.Combine(repo, "Arthur-s-Lcd-Mod/Content/Data/Gui/Styles/Fluent/Controls/Games.xaml"));
        Assert.Contains("Style Selector=\"Border.doom-keycap\"", style);
        Assert.Contains("Property=\"BorderThickness\" Value=\"1\"", style);
    }
}
