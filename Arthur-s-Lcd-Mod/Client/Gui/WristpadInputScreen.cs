using Acrux.ScreenSpace;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Modules.Wristpad;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Utility;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;

namespace LcdMod.Client.Gui
{
    /// <summary>
    /// An invisible native screen opened while the Wristpad Framework's pad is raised on one of our screens. The
    /// framework locks the camera on the pad, so eye tracking cannot aim; this screen drives the LCD cursor from mouse
    /// movement instead and forwards the mouse buttons and wheel. Reload or Escape lowers the pad, Ctrl+Reload opens the
    /// panel's terminal (the framework ignores its own Reload toggle while this screen holds the input). Text boxes
    /// still open <see cref="LcdTextEntryScreen"/> on top, which hands the cursor back when it closes.
    /// </summary>
    internal sealed class WristpadInputScreen : MyModScreen
    {
        static WristpadInputScreen _current;

        readonly IMyTerminalBlock _panel;
        InteractiveSurfaceScript _surface;
        Vector2 _pointer;
        bool _hadFocus = true;

        WristpadInputScreen(IMyTerminalBlock panel, InteractiveSurfaceScript surface)
            : base("LcdWristpadInput")
        {
            _panel = panel;
            _surface = surface;
            _pointer = new Vector2(float.NaN, float.NaN);
            CloseOnEscape = false;
            CloseButtonEnabled = false;
            EnabledBackgroundFade = false;
            PausesGameInSinglePlayer = false;
            DrawMouseCursor = false;
            SkipTransition = true;
        }

        // True while the screen owns the pad's cursor; eye tracking stands aside.
        public static bool IsActive => _current != null && _current.IsOpen;

        // Tells other Acrux mods (map hotkeys...) that the keyboard is ours while the pad is raised: this screen
        // hides the cursor, so they cannot see it any other way.
        const string KeyboardOwner = "LcdMod.WristpadInput";

        public static void TryOpenFor(IMyTerminalBlock panel)
        {
            if (IsActive)
            {
                if (ReferenceEquals(_current._panel, panel))
                    return;
                _current.CloseNow();
            }

            // The terminal (Ctrl+Reload), chat or a text entry has the input: retry once it is gone.
            var gui = MyAPIGateway.Gui;
            if (gui.IsCursorVisible || gui.ChatEntryVisible || LcdTextEntryScreen.IsActive)
                return;

            var surface = FindSurface(panel);
            if (surface == null)
                return;

            _current = new WristpadInputScreen(panel, surface);
            _current.Open();
        }

        public static void CloseCurrent()
        {
            if (IsActive)
                _current.CloseNow();
        }

        protected override void OnOpened()
        {
            WristpadLink.CaptureInput(_panel.EntityId);
            KeyboardCapture.Acquire(KeyboardOwner);
        }

        protected override void OnUpdate(bool hasFocus)
        {
            if (hasFocus == _hadFocus)
                return;
            _hadFocus = hasFocus;

            if (hasFocus)
            {
                // A text entry may have moved the cursor while it was on top.
                _pointer = _surface.CursorPosition;
            }
            else if (!LcdTextEntryScreen.IsActive)
            {
                // The terminal took over: drop held buttons. A text entry keeps driving the same surface.
                _surface.PointerInputOwner?.ResetPointerButtonInput();
            }
        }

        protected override void OnHandleInput()
        {
            if (_panel.MarkedForClose || _panel.Closed)
            {
                CloseNow();
                return;
            }

            // The panel switched scripts (e.g. from the terminal): reopened by WristpadLink if the new one is ours.
            var surface = FindSurface(_panel);
            if (!ReferenceEquals(surface, _surface))
            {
                CloseNow();
                return;
            }

            var input = MyAPIGateway.Input;
            if (IsReloadPressed(input))
            {
                if (input.IsAnyCtrlKeyPressed())
                    OpenTerminal();
                else
                    WristpadLink.RequestClose();
                return;
            }

            if (input.IsNewKeyPressed(MyKeys.Escape))
            {
                WristpadLink.RequestClose();
                return;
            }

            SurfacePointer.Move(_surface, ref _pointer, input);

            var owner = _surface.PointerInputOwner;
            if (owner != null && owner.HandlesPointerButtonInput)
            {
                owner.SetPointerButtonState(ControlClickButton.Primary, input.IsLeftMousePressed());
                owner.SetPointerButtonState(ControlClickButton.Secondary, input.IsRightMousePressed());
                owner.SetPointerButtonState(ControlClickButton.Middle, input.IsMiddleMousePressed());
                owner.SetPointerButtonState(ControlClickButton.Back, input.IsXButton1MousePressed());
                owner.SetPointerButtonState(ControlClickButton.Forward, input.IsXButton2MousePressed());
            }

            int scroll = input.DeltaMouseScrollWheelValue();
            if (scroll != 0)
                _surface.MouseScroll(scroll);
        }

        protected override void OnClosed(bool isUnloading)
        {
            _surface.PointerInputOwner?.ResetPointerButtonInput();
            if (!isUnloading)
                WristpadLink.ReleaseInput(_panel.EntityId);
            if (ReferenceEquals(_current, this))
            {
                _current = null;
                KeyboardCapture.Release(KeyboardOwner);
            }
        }

        void OpenTerminal()
        {
            var character = MyAPIGateway.Session?.Player?.Character;
            if (character == null)
                return;
            MyAPIGateway.Gui.ShowTerminalPage(MyTerminalPageEnum.ControlPanel, character, _panel);
        }

        static InteractiveSurfaceScript FindSurface(IMyTerminalBlock panel)
        {
            var instances = SurfaceScriptBase.Instances.GetInstances(panel) as SurfaceTssInstancesBase;
            if (instances == null)
                return null;

            foreach (var instance in instances.GetInstances())
            {
                var surface = instance as InteractiveSurfaceScript;
                if (surface != null && surface.Block != null && !surface.Block.MarkedForClose)
                    return surface;
            }

            return null;
        }

        // The Reload binding, read from its keyboard keys like the framework does (the control state is not reliable
        // with empty hands). Alt/Shift+Reload are left alone, as in the framework.
        static bool IsReloadPressed(VRage.ModAPI.IMyInput input)
        {
            if (input.IsAnyAltKeyPressed() || input.IsAnyShiftKeyPressed())
                return false;
            var control = input.GetGameControl(MyControlsSpace.RELOAD);
            if (control == null)
                return false;
            var key = control.GetKeyboardControl();
            if (key != MyKeys.None && input.IsNewKeyPressed(key))
                return true;
            var second = control.GetSecondKeyboardControl();
            return second != MyKeys.None && input.IsNewKeyPressed(second);
        }
    }
}
