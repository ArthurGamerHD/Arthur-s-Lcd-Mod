using System.Globalization;
using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Data;
using Adk.Gui.Lcd;
using Adk.Gui.Markup;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Thrust;
using LcdMod.Client.Apps.Views;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class FontSizeConverterTests
{
    [Fact]
    public void CompiledBindingConvertsTypesAndPreservesValueOnDoNothing()
    {
        var model = new ConverterBindingModel();
        using var view = new ConverterBindingView(model);
        Assert.Equal(0.92f, view.Text.FontSize);
        model.Size = "4";
        Assert.Equal(1.84f, view.Text.FontSize);
        model.Size = "bad";
        Assert.Equal(1.84f, view.Text.FontSize);
        model.Size = "1,5";
        Assert.Equal(0.69f, view.Text.FontSize);
    }

    [Fact]
    public void ConverterSupportsFloatDoubleCultureAndReverseConversion()
    {
        var converter = new FontSizeMultiplierConverter();
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        Assert.Equal(0.92f, converter.Convert(2f, typeof(float), "0,46", culture));
        Assert.Equal(0.92d, converter.Convert(2d, typeof(double), "0,46", culture));
        Assert.Equal(2f, converter.ConvertBack(0.92d, typeof(float), "0,46", culture));
        foreach (object invalid in new object[] { null, "bad", 0, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.Same(BindingOperations.DoNothing, converter.Convert(invalid, typeof(float), 1, culture));
            Assert.Same(BindingOperations.DoNothing, converter.Convert(1, typeof(float), invalid, culture));
        }
        Assert.Same(BindingOperations.DoNothing, converter.Convert(double.MaxValue, typeof(float), 2, culture));
        Assert.Same(BindingOperations.DoNothing, converter.Convert(1, typeof(int), 2, culture));
    }

    [Fact]
    public void BindingParserUnderstandsNestedResourcesAndQuotedParameters()
    {
        Assert.True(TemplateBindingExtension.TryParse(
            "{TemplateBinding FontSize, Converter={StaticResource Multiply}, ConverterParameter='0,46', ConverterCulture=pt-BR}", out var binding));
        Assert.Equal("FontSize", binding.Property);
        Assert.Equal("0,46", binding.ConverterParameter);
        Assert.Equal("pt-BR", binding.ConverterCulture.Name);
        Assert.False(TemplateBindingExtension.TryParse("{TemplateBinding FontSize, Unsupported=foo}", out _));
        Assert.False(TemplateBindingExtension.TryParse("{TemplateBinding FontSize, Converter={StaticResource Multiply}", out _));
        Assert.False(TemplateBindingExtension.TryParse("{TemplateBinding FontSize, ConverterParameter=1, ConverterParameter=2}", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicFontResourceUpdatesTemplateTextMeasurementWithoutReplacingCells(bool useContextProvider)
    {
        var root = new Window { WindowState = WindowState.FullScreen };
        var provider = new FontResourceProvider();
        if (useContextProvider) root.DataContext = provider;
        else root.Resources.Add("fontScale", 1f);
        root.Resources.Add("FontSizeMultiplier", new FontSizeMultiplierConverter());
        root.Styles = StyleSheet.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Styles/ThrustLegendCellControl.xaml")));
        var cell = new ThrustLegendCellControl { Label = "Forward", Value = "100", DataContext = new object() };
        root.Content = cell;
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), root);
        host.Arrange(new RectangleF(0, 0, 512, 256));
        var text = cell.VisualChildren[0].VisualChildren.OfType<TextBlock>().First();
        var before = text.DesiredSize;
        Assert.Equal(0.46f, text.FontSize);
        if (useContextProvider) provider.SetFontSize(2);
        else root.Resources.Add("fontScale", 2f);
        host.EnsureLayout();
        Assert.Equal(0.92f, text.FontSize);
        Assert.Equal(before * 2, text.DesiredSize);
        Assert.Same(text, cell.VisualChildren[0].VisualChildren.OfType<TextBlock>().First());
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        Assert.Equal(0.92f, sprites.First(s => s.Data == "Forward").RotationOrScale);

        // Local overrides retain precedence over a dynamic style setter.
        cell.FontSize = 3;
        if (useContextProvider) provider.SetFontSize(4);
        else root.Resources.Add("fontScale", 4f);
        host.EnsureLayout();
        Assert.Equal(1.38f, text.FontSize);

        cell.ClearValue(ThrustLegendCellControl.FontSizeProperty);
        root.Content = null;
        Assert.Equal(0.46f, text.FontSize);
        root.Content = cell;
        host.EnsureLayout();
        Assert.Equal(1.84f, text.FontSize);
    }

    sealed class FontResourceProvider : IResourceProvider
    {
        float _size = 1;
        public event Action<string> ResourceChanged;
        public void SetFontSize(float size) { _size = size; ResourceChanged?.Invoke("fontScale"); }
        public bool TryGetResource<T>(string key, out T value)
        {
            if (key == "fontScale" && typeof(T) == typeof(float))
            {
                value = (T)(object)_size;
                return true;
            }
            value = default;
            return false;
        }
    }
}
