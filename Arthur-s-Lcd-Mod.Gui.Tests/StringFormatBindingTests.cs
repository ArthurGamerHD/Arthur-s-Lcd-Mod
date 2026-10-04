using System.Globalization;
using Adk.Gui.Controls;
using Adk.Gui.Core.Binding;
using Adk.Gui.Data;
using Adk.Gui.Data.Converters;
using Adk.Gui.Markup;
using Adk.Gui.Styling;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed partial class StringFormatBindingView : UserControl
{
    public StringFormatBindingView(StringFormatBindingModel model)
    {
        Resources.Add("Double", new DoubleConverter());
        InitializeComponent();
        DataContext = model;
    }

    public string CompositeText => Composite.Text;
    public string BareText => Bare.Text;
    public string QuotedText => Quoted.Text;
    public string ConvertedText => Converted.Text;
    public string NullText => NullValue.Text;
    public float? NumericWidth => Numeric.Width;
    public object FormattedObject => ObjectValue.Content;

    sealed class DoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Assert.Equal(typeof(string), targetType);
            return value is double number && number >= 0 ? number * 2 : BindingOperations.DoNothing;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

public sealed class StringFormatBindingModel : ObservableObject
{
    double _value = 1234.567;
    object _optional;
    public double Value { get => _value; set => RaiseAndSetIfChanged(ref _value, value, nameof(Value)); }
    public object Optional { get => _optional; set => RaiseAndSetIfChanged(ref _optional, value, nameof(Optional)); }
    public float NumericValue => 1234.567f;
}

public sealed class StringFormatBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompiledAndRuntimeXmlUseTheSameFormattingPipeline(bool reload)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousLoader = StyleSheet.SourceLoader;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var model = new StringFormatBindingModel();
            using var view = new StringFormatBindingView(model);
            if (reload)
            {
                StyleSheet.SourceLoader = _ => File.ReadAllText(Path.Combine(RepoDirectory(),
                    "Arthur-s-Lcd-Mod.Gui.Tests/StringFormatBindingView.xaml"));
                Assert.True(HotReload.Reload(view));
            }
            Assert.Equal("L/R 1234.57m", view.CompositeText);
            Assert.Equal("1.234,57", view.BareText);
            Assert.Equal("  1234.6 m", view.QuotedText);
            Assert.Equal("Value 2469.13", view.ConvertedText);
            Assert.Equal("Value []", view.NullText);
            Assert.Equal(1234.567f, view.NumericWidth);
            Assert.Equal("Object 1234.6", view.FormattedObject);
            model.Optional = "present";
            Assert.Equal("Value [present]", view.NullText);
            model.Optional = BindingOperations.DoNothing;
            Assert.Equal("Value [present]", view.NullText);

            // Unspecified culture follows the current culture on each update;
            // an explicit ConverterCulture remains stable.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            model.Value = 2.5;
            Assert.Equal("L/R 2,50m", view.CompositeText);
            Assert.Equal("2,50", view.BareText);
            Assert.Equal("     2.5 m", view.QuotedText);
            Assert.Equal("Value 5.00", view.ConvertedText);
            model.Value = -1;
            Assert.Equal("Value 5.00", view.ConvertedText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            StyleSheet.SourceLoader = previousLoader;
        }
    }

    [Theory]
    [InlineData("{Binding Value, StringFormat='L/R {0:0.00}m'}", "L/R {0:0.00}m")]
    [InlineData("{Binding Value, StringFormat='{0,8:0.00} m'}", "{0,8:0.00} m")]
    [InlineData("{Binding Value, StringFormat=N2}", "N2")]
    [InlineData("{Binding Value, StringFormat=Hello \\{0\\}}", "Hello {0}")]
    public void ParserPreservesCompositeFormats(string markup, string expected)
    {
        Assert.True(BindingExtension.TryParse(markup, out var binding));
        Assert.Equal(expected, binding.StringFormat);
    }

    [Fact]
    public void StringFormatIsNotInventedForTemplateBinding()
    {
        Assert.False(TemplateBindingExtension.TryParse("{TemplateBinding Value, StringFormat=N2}", out _));
        Assert.False(BindingExtension.TryParse("{Binding Value, StringFormat=N2, StringFormat=F2}", out _));
        Assert.False(BindingExtension.TryParse("{Binding Value, StringFormat='{0:0.00}}", out _));
    }

    static string RepoDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
