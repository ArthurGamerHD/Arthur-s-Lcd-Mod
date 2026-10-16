using System;
using System.Collections.Generic;
using System.Threading;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Common.Config.Components;
using ManagedDoom.UserInput;
using Sandbox.ModAPI;
using VRage.Input;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Captures Space Engineers input on the main thread and exposes only
    /// atomically published primitive state to the Doom parallel worker.
    /// </summary>
    public sealed class SEDoomUserInput : IUserInput
    {
        const int WalkForwardMove = 25;
        const int RunForwardMove = 50;
        const int WalkSideMove = 24;
        const int RunSideMove = 40;
        const int SlowAngleTurn = 320;
        const int WalkAngleTurn = 640;
        const int RunAngleTurn = 1280;
        const int SlowTurnTics = 6;
        const int MouseTurnPerPixel = 40;

        const int ForwardMask = 1 << 0;
        const int BackMask = 1 << 1;
        const int LeftMask = 1 << 2;
        const int RightMask = 1 << 3;
        const int FireMask = 1 << 4;
        const int UseMask = 1 << 5;
        const int StrafeMask = 1 << 6;
        const int RunMask = 1 << 7;
        const int EscapeMask = 1 << 8;
        const int EnterMask = 1 << 9;
        const int BackspaceMask = 1 << 10;
        const int PauseMask = 1 << 11;
        // Leave room in Doom's 64-event buffer for Enter/Backspace and menu events.
        const int MaxQueuedCharacters = 48;

        // Doom's command stores only a three-bit weapon number. Doom 1's
        // selectable weapons occupy indices 0 through 7.
        const int EncodableWeaponCount = 8;

        readonly IAppHost _host;
        readonly object _textLock = new object();
        readonly Queue<char> _typedCharacters = new Queue<char>();

        // Main-thread-only engine/capture state.
        bool _weaponPreviousDown;
        bool _weaponNextDown;

        // Atomically published main-to-worker state.
        int _controlled;
        int _heldKeys;
        int _accumulatedMouseX;
        int _queuedPointerClick = -1;
        int _queuedWeaponStep;
        int _keyboardTurnSensitivity = 100;
        int _mouseTurningEnabled;
        int _mouseSensitivity = 3;
        int _disposed;
        int _saveNameEditing;

        // Parallel-worker-only Doom state.
        int _pendingWeaponNumber = -1;
        int _turnHeldTics;
        bool _menuUp;
        bool _menuDown;
        bool _menuLeft;
        bool _menuRight;
        bool _menuAccept;
        bool _menuBack;
        bool _menuBackspace;
        bool _pause;

        public SEDoomUserInput(IAppHost host)
        {
            _host = host;
        }

        /// <summary>
        /// Posts edge-triggered menu events from the latest atomically
        /// published input snapshot. This method is parallel-worker-only and
        /// never touches Space Engineers APIs.
        /// </summary>
        public void UpdateEvents(Doom doom)
        {
            if (doom == null || AtomicRead(ref _disposed) != 0)
                return;

            var heldKeys = AtomicRead(ref _heldKeys);
            var controlled = AtomicRead(ref _controlled) != 0;
            var menuInput = controlled &&
                (doom.State == DoomState.Opening || doom.Menu.Active);
            var save = doom.Menu.Active ? doom.Menu.Current as SaveMenu : null;
            var editingSaveName = menuInput && save != null &&
                save.Choice is TextBoxMenuItem && ((TextBoxMenuItem)save.Choice).Editing;
            AtomicWrite(ref _saveNameEditing, editingSaveName ? 1 : 0);

            // Typed characters are captured on the main thread, then posted to
            // Doom only while its save-name editor owns the keyboard.
            lock (_textLock)
            {
                while (_typedCharacters.Count > 0)
                {
                    var character = _typedCharacters.Dequeue();
                    if (editingSaveName)
                        doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.Unknown, character));
                }
            }

            UpdateKey(doom, DoomKey.Up, menuInput && !editingSaveName && IsDown(heldKeys, ForwardMask), ref _menuUp);
            UpdateKey(doom, DoomKey.Down, menuInput && !editingSaveName && IsDown(heldKeys, BackMask), ref _menuDown);
            UpdateKey(doom, DoomKey.Left, menuInput && !editingSaveName && IsDown(heldKeys, LeftMask), ref _menuLeft);
            UpdateKey(doom, DoomKey.Right, menuInput && !editingSaveName && IsDown(heldKeys, RightMask), ref _menuRight);

            // Space confirms menus except during save-name entry, when it is text.
            UpdateKey(
                doom,
                DoomKey.Enter,
                menuInput &&
                    (IsDown(heldKeys, EnterMask) ||
                     (!editingSaveName &&
                      (IsDown(heldKeys, FireMask) || IsDown(heldKeys, UseMask)))),
                ref _menuAccept);
            UpdateKey(doom, DoomKey.Backspace,
                editingSaveName && IsDown(heldKeys, BackspaceMask), ref _menuBackspace);
            UpdateKey(
                doom,
                DoomKey.Escape,
                controlled && IsDown(heldKeys, EscapeMask),
                ref _menuBack);
            UpdateKey(
                doom,
                DoomKey.Pause,
                controlled && IsDown(heldKeys, PauseMask),
                ref _pause);

            ProcessPointerClick(doom);

            if (menuInput)
            {
                Interlocked.Exchange(ref _accumulatedMouseX, 0);
                _turnHeldTics = 0;
            }

            var weaponStep = Interlocked.Exchange(ref _queuedWeaponStep, 0);
            if (controlled &&
                weaponStep != 0 &&
                doom.State == DoomState.Game &&
                !doom.Menu.Active &&
                doom.Game != null &&
                doom.Game.State == GameState.Level)
            {
                QueueWeaponChange(doom, weaponStep < 0 ? -1 : 1);
            }
        }

        public void BuildTicCmd(TicCmd cmd)
        {
            cmd.Clear();

            if (AtomicRead(ref _disposed) != 0 ||
                AtomicRead(ref _controlled) == 0 ||
                AtomicRead(ref _saveNameEditing) != 0)
            {
                Interlocked.Exchange(ref _accumulatedMouseX, 0);
                _pendingWeaponNumber = -1;
                _turnHeldTics = 0;
                return;
            }

            var heldKeys = AtomicRead(ref _heldKeys);
            var forwardDirection =
                (IsDown(heldKeys, ForwardMask) ? 1 : 0) -
                (IsDown(heldKeys, BackMask) ? 1 : 0);
            var horizontalDirection =
                (IsDown(heldKeys, RightMask) ? 1 : 0) -
                (IsDown(heldKeys, LeftMask) ? 1 : 0);
            var strafing = IsDown(heldKeys, StrafeMask);
            var running = IsDown(heldKeys, RunMask);
            var turning = horizontalDirection != 0 && !strafing;

            var forwardSpeed = running ? RunForwardMove : WalkForwardMove;
            var sideSpeed = running ? RunSideMove : WalkSideMove;

            cmd.ForwardMove = (sbyte)(forwardDirection * forwardSpeed);
            if (strafing)
                cmd.SideMove = (sbyte)(horizontalDirection * sideSpeed);

            var angleTurn = 0;
            if (turning)
            {
                _turnHeldTics++;
                var baseTurn = _turnHeldTics < SlowTurnTics
                    ? SlowAngleTurn
                    : (running ? RunAngleTurn : WalkAngleTurn);

                angleTurn = -horizontalDirection * ScaleKeyboardTurn(baseTurn);
            }
            else
            {
                _turnHeldTics = 0;
            }

            if (AtomicRead(ref _mouseTurningEnabled) != 0)
            {
                var mouseX = Interlocked.Exchange(ref _accumulatedMouseX, 0);
                var mouseScale = MouseTurnPerPixel * (MouseSensitivity + 1);
                angleTurn -= mouseX * mouseScale / 4;
            }
            else
            {
                Interlocked.Exchange(ref _accumulatedMouseX, 0);
            }

            cmd.AngleTurn = ClampShort(angleTurn);

            byte buttons = 0;
            if (IsDown(heldKeys, FireMask))
                buttons |= TicCmdButtons.Attack;
            if (IsDown(heldKeys, UseMask))
                buttons |= TicCmdButtons.Use;

            if (_pendingWeaponNumber >= 0)
            {
                buttons |= TicCmdButtons.Change;
                buttons |= (byte)(_pendingWeaponNumber << TicCmdButtons.WeaponShift);
                _pendingWeaponNumber = -1;
            }

            cmd.Buttons = buttons;
        }

        /// <summary>
        /// Managed Doom calls Reset from the worker when a level starts. Only
        /// worker state and atomic handoff values are reset here.
        /// </summary>
        public void Reset()
        {
            _pendingWeaponNumber = -1;
            _turnHeldTics = 0;
            _menuUp = false;
            _menuDown = false;
            _menuLeft = false;
            _menuRight = false;
            _menuAccept = false;
            _menuBack = false;
            _menuBackspace = false;
            _pause = false;
            AtomicWrite(ref _saveNameEditing, 0);
            AtomicWrite(ref _heldKeys, 0);
            AtomicWrite(ref _controlled, 0);
            Interlocked.Exchange(ref _queuedWeaponStep, 0);
            Interlocked.Exchange(ref _accumulatedMouseX, 0);
            Interlocked.Exchange(ref _queuedPointerClick, -1);
            lock (_textLock)
                _typedCharacters.Clear();
        }

        /// <summary>
        /// Called only when the LCD app itself is destroyed or replaced.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            ClearCapturedInput();
        }

        public void GrabMouse()
        {
        }

        public void ReleaseMouse()
        {
        }

        public int MaxMouseSensitivity
        {
            get { return 9; }
        }

        public int MouseSensitivity
        {
            get { return AtomicRead(ref _mouseSensitivity); }
            set
            {
                if (value < 0)
                    value = 0;
                else if (value > MaxMouseSensitivity)
                    value = MaxMouseSensitivity;

                AtomicWrite(ref _mouseSensitivity, value);
            }
        }

        /// <summary>
        /// Samples all Space Engineers state on the main thread and publishes
        /// only primitive values for the worker.
        /// </summary>
        internal void CaptureScreenInput(bool cursorMode)
        {
            if (AtomicRead(ref _disposed) != 0)
                return;

            var config = _host == null || _host.Config == null
                ? null : _host.Config.TryGetComponent<GeneralConfigComponent>();
            var mouseTurningEnabled = DoomInputSettings.GetMouseTurningEnabled(config);
            AtomicWrite(ref _mouseTurningEnabled, mouseTurningEnabled ? 1 : 0);
            AtomicWrite(
                ref _keyboardTurnSensitivity,
                DoomInputSettings.GetKeyboardTurnSensitivity(config));

            var input = MyAPIGateway.Input;
            var heldKeys = 0;
            if (input.IsKeyPress(MyKeys.W))
                heldKeys |= ForwardMask;
            if (input.IsKeyPress(MyKeys.S))
                heldKeys |= BackMask;
            if (input.IsKeyPress(MyKeys.A))
                heldKeys |= LeftMask;
            if (input.IsKeyPress(MyKeys.D))
                heldKeys |= RightMask;
            // Left mouse fires while steering; Alt reserves the button for
            // clicking the in-screen cursor and Doom menus.
            if (input.IsAnyCtrlKeyPressed() || (!cursorMode && input.IsLeftMousePressed()))
                heldKeys |= FireMask;
            if (input.IsKeyPress(MyKeys.Space))
                heldKeys |= UseMask;
            // Alt switches the mouse to the LCD cursor; C remains the strafe modifier.
            if (input.IsKeyPress(MyKeys.C))
                heldKeys |= StrafeMask;
            if (input.IsAnyShiftKeyPressed())
                heldKeys |= RunMask;
            // Escape releases the native input screen; Tab opens Doom's own menu.
            if (input.IsKeyPress(MyKeys.Tab))
                heldKeys |= EscapeMask;
            if (input.IsKeyPress(MyKeys.Enter))
                heldKeys |= EnterMask;
            if (input.IsKeyPress(MyKeys.Back))
                heldKeys |= BackspaceMask;
            if (input.IsKeyPress(MyKeys.P) || input.IsKeyPress(MyKeys.Pause))
                heldKeys |= PauseMask;

            if (mouseTurningEnabled && !cursorMode)
                Interlocked.Add(ref _accumulatedMouseX, input.GetMouseX());
            else
                Interlocked.Exchange(ref _accumulatedMouseX, 0);

            var weaponPrevious = input.IsKeyPress(MyKeys.Q);
            var weaponNext = input.IsKeyPress(MyKeys.E);

            if (weaponPrevious && !_weaponPreviousDown)
                Interlocked.Decrement(ref _queuedWeaponStep);
            if (weaponNext && !_weaponNextDown)
                Interlocked.Increment(ref _queuedWeaponStep);

            _weaponPreviousDown = weaponPrevious;
            _weaponNextDown = weaponNext;

            AtomicWrite(ref _heldKeys, heldKeys);
            AtomicWrite(ref _controlled, 1);
        }

        internal void ClearCapturedInput()
        {
            AtomicWrite(ref _saveNameEditing, 0);
            AtomicWrite(ref _controlled, 0);
            AtomicWrite(ref _heldKeys, 0);
            Interlocked.Exchange(ref _accumulatedMouseX, 0);
            Interlocked.Exchange(ref _queuedWeaponStep, 0);
            Interlocked.Exchange(ref _queuedPointerClick, -1);
            _weaponPreviousDown = false;
            _weaponNextDown = false;
            lock (_textLock)
                _typedCharacters.Clear();
        }

        internal void QueueTypedCharacters(IList<char> characters)
        {
            if (AtomicRead(ref _disposed) != 0)
                return;
            lock (_textLock)
            {
                for (var i = 0; i < characters.Count && _typedCharacters.Count < MaxQueuedCharacters; i++)
                {
                    var character = characters[i];
                    if (character >= ' ' && character <= '~')
                        _typedCharacters.Enqueue(character);
                }
            }
        }

        internal bool IsSaveNameEditing => AtomicRead(ref _saveNameEditing) != 0;

        internal void QueuePointerClick(int x, int y)
        {
            if (x < 0 || x >= 320 || y < 0 || y >= 200)
                return;
            Interlocked.Exchange(ref _queuedPointerClick, y * 320 + x);
        }

        void ProcessPointerClick(Doom doom)
        {
            int click = Interlocked.Exchange(ref _queuedPointerClick, -1);
            if (click < 0)
                return;

            if (!doom.Menu.Active)
            {
                doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.Escape));
                return;
            }

            IReadOnlyList<MenuItem> items;
            MenuItem choice;
            var selectable = doom.Menu.Current as SelectableMenu;
            var save = doom.Menu.Current as SaveMenu;
            var load = doom.Menu.Current as LoadMenu;
            if (selectable != null)
            {
                items = selectable.Items;
                choice = selectable.Choice;
            }
            else if (save != null)
            {
                items = save.Items;
                choice = save.Choice;
            }
            else if (load != null)
            {
                items = load.Items;
                choice = load.Choice;
            }
            else
            {
                return;
            }

            int x = click % 320;
            int y = click / 320;
            int target = -1;
            int selected = -1;
            int closest = int.MaxValue;
            for (int i = 0; i < items.Count; i++)
            {
                MenuItem item = items[i];
                if (ReferenceEquals(item, choice))
                    selected = i;
                int itemY = GetItemY(item);
                int distance = Math.Abs(y - itemY);
                if (x >= item.SkullX - 8 && x < 312 && y >= itemY - 5 && y <= itemY + 18 &&
                    distance < closest)
                {
                    target = i;
                    closest = distance;
                }
            }
            if (target < 0 || selected < 0)
                return;

            int down = (target - selected + items.Count) % items.Count;
            int up = (selected - target + items.Count) % items.Count;
            DoomKey direction = down <= up ? DoomKey.Down : DoomKey.Up;
            int steps = Math.Min(down, up);
            for (int i = 0; i < steps; i++)
                doom.PostEvent(new DoomEvent(EventType.KeyDown, direction));
            doom.PostEvent(new DoomEvent(EventType.KeyDown, DoomKey.Enter));
        }

        static int GetItemY(MenuItem item)
        {
            var simple = item as SimpleMenuItem;
            if (simple != null)
                return simple.ItemY;
            var toggle = item as ToggleMenuItem;
            if (toggle != null)
                return toggle.ItemY;
            var slider = item as SliderMenuItem;
            if (slider != null)
                return slider.ItemY;
            var textBox = item as TextBoxMenuItem;
            return textBox != null ? textBox.ItemY : item.SkullY;
        }

        int ScaleKeyboardTurn(int value)
        {
            return value * AtomicRead(ref _keyboardTurnSensitivity) / 100;
        }

        void QueueWeaponChange(Doom doom, int direction)
        {
            var game = doom.Game;
            var world = game == null ? null : game.World;
            var player = world == null ? null : world.ConsolePlayer;
            if (player == null || player.WeaponOwned == null)
                return;

            var current = player.PendingWeapon != WeaponType.NoChange
                ? player.PendingWeapon
                : player.ReadyWeapon;

            var start = (int)current;
            if (start < 0 || start >= EncodableWeaponCount)
                start = (int)player.ReadyWeapon;
            if (start < 0 || start >= EncodableWeaponCount)
                start = 0;

            for (var offset = 1; offset <= EncodableWeaponCount; offset++)
            {
                var weapon = start + direction * offset;
                while (weapon < 0)
                    weapon += EncodableWeaponCount;
                weapon %= EncodableWeaponCount;

                if (weapon < player.WeaponOwned.Length && player.WeaponOwned[weapon])
                {
                    _pendingWeaponNumber = weapon;
                    return;
                }
            }
        }

        static int AtomicRead(ref int value)
        {
            return Interlocked.CompareExchange(ref value, 0, 0);
        }

        static void AtomicWrite(ref int location, int value)
        {
            Interlocked.Exchange(ref location, value);
        }

        static bool IsDown(int heldKeys, int mask)
        {
            return (heldKeys & mask) != 0;
        }

        static void UpdateKey(Doom doom, DoomKey key, bool pressed, ref bool previous)
        {
            if (pressed == previous)
                return;

            previous = pressed;
            doom.PostEvent(new DoomEvent(
                pressed ? EventType.KeyDown : EventType.KeyUp,
                key));
        }

        static short ClampShort(int value)
        {
            if (value < short.MinValue)
                return short.MinValue;
            if (value > short.MaxValue)
                return short.MaxValue;
            return (short)value;
        }
    }
}
