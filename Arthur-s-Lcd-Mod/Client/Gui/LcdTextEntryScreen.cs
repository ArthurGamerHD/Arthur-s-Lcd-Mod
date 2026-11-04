using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Lcd;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.ModAPI;
using VRage.Input;
using VRageMath;

namespace LcdMod.Client.Gui
{
    /// <summary>
    /// An invisible native screen opened while a text box on an LCD has focus. It takes keyboard focus from the
    /// character, so typing does not move the player or trigger hotkeys, and forwards the keystrokes to the box.
    /// The camera is frozen while it is open, so the screen drives the LCD cursor itself from mouse movement, and
    /// clicks land where that cursor is. It stays open until Enter or Escape, typing into whichever text box has
    /// focus; it also closes if the LCD it drives goes away.
    /// </summary>
    internal sealed class LcdTextEntryScreen : MyModScreen
    {
        static LcdTextEntryScreen _current;

        readonly InteractiveSurfaceScript _surface;
        readonly LcdGuiHost _host;
        readonly KeyTextTranslator _translator = new KeyTextTranslator();
        readonly List<MyKeys> _keys = new List<MyKeys>();
        readonly List<char> _typed = new List<char>();
        Vector2 _pointer;
        TextBox _box;

        LcdTextEntryScreen(InteractiveSurfaceScript surface, LcdGuiHost host)
            : base("LcdTextEntry")
        {
            _surface = surface;
            _host = host;
            _pointer = surface.CursorPosition;
            CloseOnEscape = false;
            CloseButtonEnabled = false;
            EnabledBackgroundFade = false;
            PausesGameInSinglePlayer = false;
            DrawMouseCursor = false;
            SkipTransition = true;
        }

        // True while an entry owns the keyboard and the LCD cursor; eye tracking stands aside.
        public static bool IsActive => _current != null && _current.IsOpen;

        public static void TrackFocus(InteractiveSurfaceScript surface, LcdGuiHost host)
        {
            if (surface == null || host == null || !(host.VisualTree.FocusedElement is TextBox))
                return;
            if (IsActive)
            {
                if (ReferenceEquals(_current._host, host))
                    return;
                _current.End(false);
            }
            _current = new LcdTextEntryScreen(surface, host);
            _current.Open();
        }

        protected override void OnOpened()
        {
            _host.Input.KeyboardEnabled = false;
        }

        protected override void OnHandleInput()
        {
            var input = MyAPIGateway.Input;
            if (_host.IsDisposed || _surface.Block == null || _surface.Block.MarkedForClose)
            {
                CloseNow();
                return;
            }
            var focused = _host.VisualTree.FocusedElement as TextBox;
            if (focused == null && _box != null)
            {
                _box.CapsLockShown = false;
                _box = null;
            }
            if (input.IsNewKeyPressed(MyKeys.Enter) || input.IsNewKeyPressed(MyKeys.Escape))
            {
                _host.Input.SuppressUntilReleased(Acrux.Core.Input.Key.Enter);
                _host.Input.SuppressUntilReleased(Acrux.Core.Input.Key.Escape);
                End(true);
                return;
            }
            if (focused != null)
                TypeInto(focused, input);
            MovePointer(input);
        }

        void TypeInto(TextBox box, VRage.ModAPI.IMyInput input)
        {
            if (!ReferenceEquals(box, _box))
            {
                if (_box != null)
                    _box.CapsLockShown = false;
                _box = box;
            }
            _keys.Clear();
            input.GetPressedKeys(_keys);
            _typed.Clear();
            if (input.TextInput.Count > 0)
                foreach (char character in input.TextInput)
                    _typed.Add(character);
            else
                _translator.Collect(_keys, input.IsAnyShiftKeyPressed(), input.IsAnyCtrlKeyPressed(), _typed);
            _box.CapsLockShown = _translator.CapsLock;
            _host.Input.KeyboardEnabled = true;
            _host.Input.TextInputOverride = _typed;
            try
            {
                _host.UpdateKeyboardInput();
            }
            finally
            {
                _host.Input.KeyboardEnabled = false;
                _host.Input.TextInputOverride = null;
            }
        }

        void MovePointer(VRage.ModAPI.IMyInput input)
        {
            SurfacePointer.Move(_surface, ref _pointer, input);
            _surface.SetPointerButtonState(ControlClickButton.Primary, input.IsLeftMousePressed());
            _surface.SetPointerButtonState(ControlClickButton.Secondary, input.IsRightMousePressed());
            _surface.SetPointerButtonState(ControlClickButton.Middle, input.IsMiddleMousePressed());
        }

        protected override void OnClosed(bool isUnloading)
        {
            if (_box != null)
                _box.CapsLockShown = false;
            _host.Input.KeyboardEnabled = true;
            if (ReferenceEquals(_current, this))
                _current = null;
        }

        void End(bool releaseFocus)
        {
            if (_box != null)
                _box.CapsLockShown = false;
            if (releaseFocus)
                _host.VisualTree.Focus(null);
            _host.Input.KeyboardEnabled = true;
            _surface.RenderSprites();
            CloseNow();
        }
    }
}
