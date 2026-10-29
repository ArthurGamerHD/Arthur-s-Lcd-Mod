using System.Text.RegularExpressions;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class Bc7DeterminismTests
{
    [Fact]
    public void Bc7Encoder_UsesOnlyIntegerMathAndNoThreads()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "External", "Adk", "Adk.Image", "Dds", "Bc7Encoder.cs"));

        string[] forbidden = { "float", "double", "decimal", "Math.Sqrt", "Math.Pow", "Parallel", "Thread", "Task", "Random" };
        foreach (string token in forbidden)
            Assert.False(Regex.IsMatch(source, $@"\b{Regex.Escape(token)}\b"), $"Bc7Encoder uses '{token}'");
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
