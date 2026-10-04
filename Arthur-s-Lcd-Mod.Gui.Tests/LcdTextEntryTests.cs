using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using Adk.Gui.Layout;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class LcdTextEntryTests
{
    sealed class Keyboard : IInputSource
    {
        public string Typed = string.Empty;
        public Key Held = Key.None;
        public bool NewlyPressed;
        public IEnumerable<char> TextInput => Typed;
        public bool TryGetPointerPosition(out Vector2 position) { position = default; return false; }
        public int DeltaMouseScrollWheelValue() => 0;
        public bool IsPrimaryButtonPressed() => false;
        public bool IsNewPrimaryButtonPressed() => false;
        public bool IsNewPrimaryButtonReleased() => false;
        public bool IsMiddleButtonPressed() => false;
        public bool IsNewMiddleButtonPressed() => false;
        public bool IsNewMiddleButtonReleased() => false;
        public bool IsSecondaryButtonPressed() => false;
        public bool IsNewSecondaryButtonPressed() => false;
        public bool IsNewSecondaryButtonReleased() => false;
        public bool IsKeyPressed(Key key) => key == Held && key != Key.None;
        public bool IsNewKeyPressed(Key key) => NewlyPressed && IsKeyPressed(key);
        public void GetPressedKeys(List<Key> keys) { }
    }

    [Fact]
    public void AFocusedTextBoxKeepsFocusAndTakesTypingAfterThePointerLeavesTheSurface()
    {
        using var harness = new DialogHarness(400, 300);
        var box = new TextBox { VerticalAlignment = VerticalAlignment.Top };
        var view = new Grid();
        view.AddChild(box);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Click(box);
        Assert.True(box.IsFocused);

        // Opening the native entry screen (or releasing Alt) makes eye tracking drop the pointer.
        harness.Host.ClearPointer();
        Assert.True(box.IsFocused, "losing the pointer must not take keyboard focus from the text box");

        harness.Host.VisualTree.HandleInput(new Keyboard { Typed = "rotor" });

        Assert.Equal("rotor", box.Text);
    }

    [Fact]
    public void TheCapsLockIndicatorIsDrawnBesideTheCaretOnlyWhileShown()
    {
        using var textures = new FakeGeneratedTextures();
        using var harness = new DialogHarness(400, 300);
        var box = new TextBox { Text = "ab", VerticalAlignment = VerticalAlignment.Top };
        var view = new Grid();
        view.AddChild(box);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Click(box);
        Assert.DoesNotContain(harness.Draw(), s => s.Data != null && s.Data.Contains("_Maximize"));

        box.CapsLockShown = true;
        harness.Draw();
        var chevron = harness.Draw().Single(s => s.Data != null && s.Data.Contains("_Maximize"));
        Assert.True(box.Bounds.Contains(chevron.Position!.Value), "the indicator sits inside the text box");
        Assert.True(chevron.Position!.Value.Y < box.Bounds.Center.Y, "the indicator sits in the caret's upper half");

        box.CapsLockShown = false;
        Assert.DoesNotContain(harness.Draw(), s => s.Data != null && s.Data.Contains("_Maximize"));
    }

    [Fact]
    public void AHeldBackspaceRepeatsWhilePointerPassesRunBetweenKeyboardPasses()
    {
        using var harness = new DialogHarness(400, 300);
        var box = new TextBox { Text = "abcdefghijklmnopqrstuvwxyz", VerticalAlignment = VerticalAlignment.Top };
        var view = new Grid();
        view.AddChild(box);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Click(box);
        harness.Host.VisualTree.HandleInput(new Keyboard { Held = Key.End, NewlyPressed = true });
        harness.Host.VisualTree.HandleInput(new Keyboard());
        harness.Host.Input.KeyboardEnabled = false;

        for (int frame = 0; frame < 40; frame++)
        {
            harness.Host.MovePointer(box.Bounds.Center);
            harness.Host.VisualTree.HandleInput(new Keyboard { Held = Key.Back, NewlyPressed = frame == 0 });
        }

        Assert.True(box.Text.Length <= 26 - 5, $"holding Backspace for 40 frames must repeat, but the text is '{box.Text}'");
    }
}
