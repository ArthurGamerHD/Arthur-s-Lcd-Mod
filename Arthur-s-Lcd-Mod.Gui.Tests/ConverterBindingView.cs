using Adk.Gui.Controls;
using Adk.Gui.Core.Binding;
using LcdMod.Client.Apps.Views;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed partial class ConverterBindingView : UserControl
{
    public ConverterBindingView(ConverterBindingModel model)
    {
        Resources.Add("Multiply", new FontSizeMultiplierConverter());
        InitializeComponent();
        DataContext = model;
    }

    public TextBlock Text => ConvertedText;
}

public sealed class ConverterBindingModel : ObservableObject
{
    string _size = "2";
    public string Size
    {
        get => _size;
        set => RaiseAndSetIfChanged(ref _size, value, nameof(Size));
    }
}
