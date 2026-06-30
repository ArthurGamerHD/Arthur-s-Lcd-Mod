using System.Threading;
using LcdMod.Client.Apps.Abstract;
using ManagedDoom.UserInput;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.ModAPI;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Captures Space Engineers input on the main thread and exposes only
    /// atomically published primitive state to the Doom parallel worker.
    /// </summary>
    public sealed class SECockpitUserInput : IUserInput
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

        // Doom's command stores only a three-bit weapon number. Doom 1's
        // selectable weapons occupy indices 0 through 7.
        const int EncodableWeaponCount = 8;

        readonly IAppHost _host;

        // Main-thread-only engine/capture state.
        long _resolvedEntityId;
        IMyCockpit _cockpit;
        bool _weaponPreviousDown;
        bool _weaponNextDown;

        // Atomically published main-to-worker state.
        int _controlled;
        int _heldKeys;
        int _accumulatedMouseX;
        int _queuedWeaponStep;
        int _keyboardTurnSensitivity = 100;
        int _mouseTurningEnabled;
        int _mouseSensitivity = 3;
        int _disposed;

        // Parallel-worker-only Doom state.
        int _pendingWeaponNumber = -1;
        int _turnHeldTics;
        bool _menuUp;
        bool _menuDown;
        bool _menuLeft;
        bool _menuRight;
        bool _menuAccept;
        bool _menuBack;

        public SECockpitUserInput(IAppHost host)
        {
            _host = host;
            DoomInputDispatcher.Register(this);
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

            UpdateKey(doom, DoomKey.Up, menuInput && IsDown(heldKeys, ForwardMask), ref _menuUp);
            UpdateKey(doom, DoomKey.Down, menuInput && IsDown(heldKeys, BackMask), ref _menuDown);
            UpdateKey(doom, DoomKey.Left, menuInput && IsDown(heldKeys, LeftMask), ref _menuLeft);
            UpdateKey(doom, DoomKey.Right, menuInput && IsDown(heldKeys, RightMask), ref _menuRight);

            // Ctrl is the classic fire key, but acts as Enter in menus. Space
            // also confirms because it is the classic Use/Open key.
            UpdateKey(
                doom,
                DoomKey.Enter,
                menuInput &&
                    (IsDown(heldKeys, FireMask) || IsDown(heldKeys, UseMask)),
                ref _menuAccept);
            UpdateKey(
                doom,
                DoomKey.Escape,
                menuInput && IsDown(heldKeys, EscapeMask),
                ref _menuBack);

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
                AtomicRead(ref _controlled) == 0)
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
            AtomicWrite(ref _heldKeys, 0);
            AtomicWrite(ref _controlled, 0);
            Interlocked.Exchange(ref _queuedWeaponStep, 0);
            Interlocked.Exchange(ref _accumulatedMouseX, 0);
        }

        /// <summary>
        /// Called only when the LCD app itself is destroyed or replaced. This
        /// method is main-thread-only because it unregisters from the session
        /// input dispatcher.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            DoomInputDispatcher.Unregister(this);
            ClearCapturedInput();
            _resolvedEntityId = 0L;
            _cockpit = null;
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
        internal bool TryCaptureFrameInput()
        {
            if (AtomicRead(ref _disposed) != 0)
                return false;

            var config = _host == null ? null : _host.Config;
            var mouseTurningEnabled = DoomInputSettings.GetMouseTurningEnabled(config);
            AtomicWrite(ref _mouseTurningEnabled, mouseTurningEnabled ? 1 : 0);
            AtomicWrite(
                ref _keyboardTurnSensitivity,
                DoomInputSettings.GetKeyboardTurnSensitivity(config));

            var cockpit = ResolveCockpit();
            if (!IsLocallyControlled(cockpit))
                return false;

            var input = MyAPIGateway.Input;
            if (input == null)
                return false;

            var heldKeys = 0;
            if (input.IsKeyPress(MyKeys.W))
                heldKeys |= ForwardMask;
            if (input.IsKeyPress(MyKeys.S))
                heldKeys |= BackMask;
            if (input.IsKeyPress(MyKeys.A))
                heldKeys |= LeftMask;
            if (input.IsKeyPress(MyKeys.D))
                heldKeys |= RightMask;
            if (input.IsAnyCtrlKeyPressed())
                heldKeys |= FireMask;
            if (input.IsKeyPress(MyKeys.Space))
                heldKeys |= UseMask;
            if (input.IsAnyAltKeyPressed())
                heldKeys |= StrafeMask;
            if (input.IsAnyShiftKeyPressed())
                heldKeys |= RunMask;
            if (input.IsKeyPress(MyKeys.Escape))
                heldKeys |= EscapeMask;

            if (mouseTurningEnabled)
                Interlocked.Add(ref _accumulatedMouseX, input.GetMouseXForGamePlay());
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
            return true;
        }

        internal void ClearCapturedInput()
        {
            AtomicWrite(ref _controlled, 0);
            AtomicWrite(ref _heldKeys, 0);
            Interlocked.Exchange(ref _accumulatedMouseX, 0);
            Interlocked.Exchange(ref _queuedWeaponStep, 0);
            _weaponPreviousDown = false;
            _weaponNextDown = false;
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

        IMyCockpit ResolveCockpit()
        {
            var entityId = DoomInputSettings.GetCockpitEntityId(
                _host == null ? null : _host.Config);
            if (entityId == 0L)
            {
                _resolvedEntityId = 0L;
                _cockpit = null;
                return null;
            }

            if (_cockpit != null &&
                _resolvedEntityId == entityId &&
                !_cockpit.MarkedForClose)
                return _cockpit;

            _resolvedEntityId = entityId;
            _cockpit = null;

            IMyEntity entity;
            if (MyAPIGateway.Entities == null ||
                !MyAPIGateway.Entities.TryGetEntityById(entityId, out entity))
                return null;

            var cockpit = entity as IMyCockpit;
            if (cockpit == null || cockpit.MarkedForClose)
                return null;

            _cockpit = cockpit;
            return _cockpit;
        }

        static bool IsLocallyControlled(IMyCockpit cockpit)
        {
            if (cockpit == null || !cockpit.IsOccupied || !cockpit.IsUnderControl)
                return false;

            var session = MyAPIGateway.Session;
            var controlled = session == null ? null : session.ControlledObject;
            return controlled != null &&
                controlled.Entity != null &&
                controlled.Entity.EntityId == cockpit.EntityId;
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
