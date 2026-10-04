using Adk.Gui.Controls;
using Adk.Gui.Markup;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Client.Apps.Views.Extensions;
using LcdMod.Client.Helpers;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class LocalizationMarkupExtensionTests
{
    [Fact]
    public void RuntimeXamlResolvesNamespacedLocalizationExtension()
    {
        const string key = "LcdMod_Common_Button_Cancel";
        var types = new TypeRegistry();
        types.Register("TextBlock", () => new TextBlock());
        types.RegisterProperty<TextBlock, string>("TextBlock", "Text", (block, value) => block.Text = value);
        types.RegisterMarkupExtension("LcdMod.Client.Apps.Views.Extensions.loc",
            value => new locExtension(value).ProvideValue());

        var block = (TextBlock)new UiXmlLoader(types).Load(
            "<TextBlock xmlns:ext=\"using:LcdMod.Client.Apps.Views.Extensions\" " +
            "Text=\"{ext:loc " + key + "}\" />");

        Assert.Equal(LocHelper.GetLoc(key), block.Text);
    }

    [Fact]
    public void HotReloadReevaluatesLocalizationExtension()
    {
        using var harness = new DialogHarness();
        using var view = new SpritePickerView(SpritePickerViewModel.Single(Array.Empty<string>(), _ => { }));
        HotReload.Apply((IHotReloadableView)view,
            "<Window xmlns=\"https://github.com/ArthurGamerHD/Adk\" " +
            "xmlns:ext=\"using:LcdMod.Client.Apps.Views.Extensions\">" +
            "<TextBlock Text=\"{ext:loc LcdMod_Common_Button_Cancel}\" " +
            "ToolTip.Tip=\"{ext:loc LcdMod_Common_Button_Cancel}\" /></Window>");

        var text = Assert.IsType<TextBlock>(view.Content);
        Assert.Equal(LocHelper.GetLoc("LcdMod_Common_Button_Cancel"), text.Text);
        Assert.Equal(text.Text, ToolTip.GetTip(text));
    }
}
