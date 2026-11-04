using System.Collections.Generic;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts;
using ManagedDoom.SE;
using Sandbox.ModAPI;
using VRage.Input;
using VRageMath;
using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Games.ManagedDoom
{
    /// <summary>Captures Doom controls in a native input screen, releasing them with Escape or F10.</summary>
    internal sealed class DoomInputScreen : MyModScreen
    {
        static DoomInputScreen _current;
        readonly GameSurfaceScript _surface;
        readonly DoomGame _game;
        readonly SEDoomUserInput _input;
        readonly KeyTextTranslator _translator = new KeyTextTranslator();
        readonly List<MyKeys> _keys = new List<MyKeys>();
        readonly List<char> _typed = new List<char>();
        Vector2 _pointer;
        bool _cursorVisible;

        DoomInputScreen(GameSurfaceScript surface, DoomGame game, SEDoomUserInput input)
            : base("LcdDoomInput")
        {
            _surface = surface;
            _game = game;
            _input = input;
            _pointer = surface.CursorPosition;
            CloseOnEscape = false;
            CloseButtonEnabled = false;
            EnabledBackgroundFade = false;
            PausesGameInSinglePlayer = false;
            DrawMouseCursor = false;
            SkipTransition = true;
        }

        public static void OpenFor(GameSurfaceScript surface, DoomGame game, SEDoomUserInput input)
        {
            if (_current != null && _current.IsOpen)
                return;
            _current = new DoomInputScreen(surface, game, input);
            _current.Open();
        }

        public static void CloseFor(DoomGame game)
        {
            if (_current != null && ReferenceEquals(_current._game, game))
                _current.CloseNow();
        }

        protected override void OnHandleInput()
        {
            var input = MyAPIGateway.Input;
            if (!_surface.IsCurrentGame(_game) ||
                _surface.Block == null || _surface.Block.MarkedForClose ||
                input.IsNewKeyPressed(MyKeys.Escape) || input.IsNewKeyPressed(MyKeys.F10))
            {
                CloseNow();
                return;
            }
            bool cursorVisible = input.IsAnyAltKeyPressed();
            _input.CaptureScreenInput(cursorVisible);
            _keys.Clear();
            input.GetPressedKeys(_keys);
            _typed.Clear();
            _translator.Collect(_keys, input.IsAnyShiftKeyPressed(), input.IsAnyCtrlKeyPressed(), _typed);
            // Prefer the game's text stream when it is available (keyboard layouts/IME),
            // with the same key translator fallback as the LCD text-entry screen.
            if (input.TextInput.Count > 0)
            {
                _typed.Clear();
                foreach (char character in input.TextInput)
                    _typed.Add(character);
            }
            if (_input.IsSaveNameEditing)
                _input.QueueTypedCharacters(_typed);
            if (cursorVisible)
            {
                MovePointer(input);
                _surface.SetPointerButtonState(ControlClickButton.Primary, input.IsLeftMousePressed());
                _surface.SetPointerButtonState(ControlClickButton.Secondary, input.IsRightMousePressed());
                if (input.IsNewLeftMousePressed())
                {
                    int x;
                    int y;
                    if (_game.TryMapPointer(_pointer, out x, out y))
                        _input.QueuePointerClick(x, y);
                }
            }
            else if (_cursorVisible)
            {
                _surface.HideDoomCursor();
            }
            _cursorVisible = cursorVisible;
        }

        void MovePointer(VRage.ModAPI.IMyInput input)
        {
            SurfacePointer.Move(_surface, ref _pointer, input);
        }

        protected override void OnClosed(bool isUnloading)
        {
            if (_cursorVisible)
                _surface.HideDoomCursor();
            _input.ClearCapturedInput();
            if (ReferenceEquals(_current, this))
                _current = null;
        }
    }
}
