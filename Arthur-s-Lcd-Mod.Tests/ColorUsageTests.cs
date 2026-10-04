using System.Text.RegularExpressions;

namespace Arthur_s_Lcd_Mod.Tests;

public class ColorUsageTests
{
    // VRageMath has Color(Color, float alpha) with alpha in 0..1 but no (Color, int) overload, so an integer
    // alpha such as 64 converts to 64f and clamps to fully opaque.
    [Fact]
    public void ColorAlphaOverloadIsNeverGivenAByteValue()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        var pattern = new Regex(@"new Color\((?:[^,()]|\([^()]*\))+,\s*\d+\s*\)");
        var offenders = new List<string>();
        foreach (string folder in new[] { "Arthur-s-Lcd-Mod/Client", "Arthur-s-Lcd-Mod/Common", "External/Adk" })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(repo, folder), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains("/obj/") || file.Contains("/bin/"))
                    continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    if (pattern.IsMatch(lines[i]))
                        offenders.Add(Path.GetFileName(file) + ":" + (i + 1) + " " + lines[i].Trim());
            }
        }
        Assert.Empty(offenders);
    }
}
