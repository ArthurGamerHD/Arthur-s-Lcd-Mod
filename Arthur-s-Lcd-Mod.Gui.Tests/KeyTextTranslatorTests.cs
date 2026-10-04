using LcdMod.Client.Gui;
using VRage.Input;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class KeyTextTranslatorTests
{
    static string Frame(KeyTextTranslator translator, bool shift = false, bool ctrl = false, params MyKeys[] held)
    {
        var typed = new List<char>();
        translator.Collect(held, shift, ctrl, typed);
        return new string(typed.ToArray());
    }

    [Fact]
    public void KeysTypeTheirCharactersOncePerPressWithShiftForCapitalsAndSymbols()
    {
        var translator = new KeyTextTranslator();
        Assert.Equal("r", Frame(translator, false, false, MyKeys.R));
        Assert.Equal(string.Empty, Frame(translator, false, false, MyKeys.R));
        Assert.Equal(string.Empty, Frame(translator));
        Assert.Equal("R!", Frame(translator, true, false, MyKeys.R, MyKeys.D1));
        Assert.Equal(string.Empty, Frame(translator));
        Assert.Equal("7 -", Frame(translator, false, false, MyKeys.NumPad7, MyKeys.Space, MyKeys.OemMinus));
    }

    [Fact]
    public void ControlChordsTypeNothing()
    {
        var translator = new KeyTextTranslator();
        Assert.Equal(string.Empty, Frame(translator, false, true, MyKeys.A));
    }

    [Fact]
    public void AHeldKeyRepeatsAfterADelay()
    {
        var translator = new KeyTextTranslator();
        string typed = string.Empty;
        for (int frame = 0; frame < 40; frame++)
            typed += Frame(translator, false, false, MyKeys.A);
        Assert.Equal("aaaaa", typed);
    }

    [Fact]
    public void DoubleTappingShiftLocksCapitalsUntilTheNextShiftPress()
    {
        var translator = new KeyTextTranslator();
        Frame(translator, true);
        Frame(translator);
        Frame(translator, true);
        Frame(translator);
        Assert.True(translator.CapsLock);
        Assert.Equal("A", Frame(translator, false, false, MyKeys.A));
        Frame(translator);
        Assert.Equal("1", Frame(translator, false, false, MyKeys.D1));
        Frame(translator);

        Frame(translator, true);
        Assert.False(translator.CapsLock);
        Assert.Equal("A", Frame(translator, true, false, MyKeys.A));
        Frame(translator);
        Assert.Equal("a", Frame(translator, false, false, MyKeys.A));
    }

    [Fact]
    public void ShiftTapsTooFarApartDoNotLockCapitals()
    {
        var translator = new KeyTextTranslator();
        Frame(translator, true);
        for (int frame = 0; frame < 30; frame++)
            Frame(translator);
        Frame(translator, true);
        Assert.False(translator.CapsLock);
    }

    [Fact]
    public void TheCapsLockKeyTogglesCapitalsOncePerPress()
    {
        var translator = new KeyTextTranslator();
        Frame(translator, false, false, MyKeys.CapsLock);
        Frame(translator, false, false, MyKeys.CapsLock);
        Assert.True(translator.CapsLock);
        Frame(translator);
        Assert.Equal("Q", Frame(translator, false, false, MyKeys.Q));
        Frame(translator);
        Frame(translator, false, false, MyKeys.CapsLock);
        Assert.False(translator.CapsLock);
    }
}
