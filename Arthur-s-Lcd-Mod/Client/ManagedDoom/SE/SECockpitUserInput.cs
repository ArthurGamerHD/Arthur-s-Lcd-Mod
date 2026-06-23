using LcdMod.Client.Apps.Abstract;
using ManagedDoom.UserInput;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.ModAPI;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Converts classic keyboard controls, optionally augmented by mouse yaw,
    /// into Doom tic commands while the local player controls the cockpit
    /// selected for this LCD surface.
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

        // Doom's command stores only a three-bit weapon number. Doom 1's
        // selectable weapons occupy indices 0 through 7.
        const int EncodableWeaponCount = 8;

        readonly IAppHost _host;

        long _resolvedEntityId;
        IMyCockpit _cockpit;
        int _mouseSensitivity = 3;
        int _accumulatedMouseX;
        int _queuedWeaponStep;
        int _pendingWeaponNumber = -1;
        int _turnHeldTics;
        bool _forwardDown;
        bool _backDown;
        bool _leftDown;
        bool _rightDown;
        bool _fireDown;
        bool _useDown;
        bool _strafeDown;
        bool _runDown;
        bool _escapeDown;
        bool _weaponPreviousDown;
        bool _weaponNextDown;
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
        /// Posts edge-triggered keyboard events for Doom's opening sequence and
        /// menu system. BuildTicCmd is only called after gameplay has started.
        /// </summary>
        public void UpdateEvents(Doom doom)
        {
            if (doom == null)
                return;

            var cockpit = ResolveCockpit();
            bool controlled = IsLocallyControlled(cockpit);
            bool menuInput = controlled &&
                (doom.State == DoomState.Opening || doom.Menu.Active);

            UpdateKey(doom, DoomKey.Up, menuInput && _forwardDown, ref _menuUp);
            UpdateKey(doom, DoomKey.Down, menuInput && _backDown, ref _menuDown);
            UpdateKey(doom, DoomKey.Left, menuInput && _leftDown, ref _menuLeft);
            UpdateKey(doom, DoomKey.Right, menuInput && _rightDown, ref _menuRight);

            // Ctrl is the classic fire key, but acts as Enter in menus. Space
            // also confirms because it is the classic Use/Open key.
            UpdateKey(
                doom,
                DoomKey.Enter,
                menuInput && (_fireDown || _useDown),
                ref _menuAccept);
            UpdateKey(doom, DoomKey.Escape, menuInput && _escapeDown, ref _menuBack);

            if (menuInput)
            {
                // Do not carry mouse motion or keyboard-turn acceleration from
                // the title/menu into the first gameplay tic.
                _accumulatedMouseX = 0;
                _turnHeldTics = 0;
            }

            int weaponStep = _queuedWeaponStep;
            _queuedWeaponStep = 0;
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

            var cockpit = ResolveCockpit();
            if (!IsLocallyControlled(cockpit))
            {
                ClearCapturedInput();
                _pendingWeaponNumber = -1;
                return;
            }

            int forwardDirection = (_forwardDown ? 1 : 0) - (_backDown ? 1 : 0);
            int horizontalDirection = (_rightDown ? 1 : 0) - (_leftDown ? 1 : 0);
            bool turning = horizontalDirection != 0 && !_strafeDown;

            int forwardSpeed = _runDown ? RunForwardMove : WalkForwardMove;
            int sideSpeed = _runDown ? RunSideMove : WalkSideMove;

            cmd.ForwardMove = (sbyte)(forwardDirection * forwardSpeed);
            if (_strafeDown)
                cmd.SideMove = (sbyte)(horizontalDirection * sideSpeed);

            int angleTurn = 0;
            if (turning)
            {
                _turnHeldTics++;
                int baseTurn = _turnHeldTics < SlowTurnTics
                    ? SlowAngleTurn
                    : (_runDown ? RunAngleTurn : WalkAngleTurn);

                angleTurn = -horizontalDirection * ScaleKeyboardTurn(baseTurn);
            }
            else
            {
                _turnHeldTics = 0;
            }

            if (DoomInputSettings.GetMouseTurningEnabled(
                _host == null ? null : _host.Config))
            {
                int mouseX = _accumulatedMouseX;
                int mouseScale = MouseTurnPerPixel * (_mouseSensitivity + 1);
                angleTurn -= mouseX * mouseScale / 4;
            }

            _accumulatedMouseX = 0;
            cmd.AngleTurn = ClampShort(angleTurn);

            byte buttons = 0;
            if (_fireDown)
                buttons |= TicCmdButtons.Attack;
            if (_useDown)
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
        /// Managed Doom calls Reset when a level starts. It clears transient
        /// state without unregistering the still-active LCD app.
        /// </summary>
        public void Reset()
        {
            _resolvedEntityId = 0L;
            _cockpit = null;
            _pendingWeaponNumber = -1;
            _queuedWeaponStep = 0;
            _turnHeldTics = 0;
            _weaponPreviousDown = false;
            _weaponNextDown = false;
            _menuUp = false;
            _menuDown = false;
            _menuLeft = false;
            _menuRight = false;
            _menuAccept = false;
            _menuBack = false;
            ClearCapturedInput();
        }

        /// <summary>
        /// Called only when the LCD app itself is destroyed or replaced.
        /// </summary>
        public void Dispose()
        {
            DoomInputDispatcher.Unregister(this);
            Reset();
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
            get { return _mouseSensitivity; }
            set
            {
                if (value < 0)
                    _mouseSensitivity = 0;
                else if (value > MaxMouseSensitivity)
                    _mouseSensitivity = MaxMouseSensitivity;
                else
                    _mouseSensitivity = value;
            }
        }

        internal bool TryCaptureFrameInput()
        {
            var cockpit = ResolveCockpit();
            if (!IsLocallyControlled(cockpit))
                return false;

            var input = MyAPIGateway.Input;
            if (input == null)
                return false;

            _forwardDown = input.IsKeyPress(MyKeys.W);
            _backDown = input.IsKeyPress(MyKeys.S);
            _leftDown = input.IsKeyPress(MyKeys.A);
            _rightDown = input.IsKeyPress(MyKeys.D);
            _fireDown = input.IsAnyCtrlKeyPressed();
            _useDown = input.IsKeyPress(MyKeys.Space);
            _strafeDown = input.IsAnyAltKeyPressed();
            _runDown = input.IsAnyShiftKeyPressed();
            _escapeDown = input.IsKeyPress(MyKeys.Escape);

            if (DoomInputSettings.GetMouseTurningEnabled(
                _host == null ? null : _host.Config))
            {
                _accumulatedMouseX += input.GetMouseXForGamePlay();
            }
            else
            {
                _accumulatedMouseX = 0;
            }

            bool weaponPrevious = input.IsKeyPress(MyKeys.Q);
            bool weaponNext = input.IsKeyPress(MyKeys.E);

            if (weaponPrevious && !_weaponPreviousDown)
                _queuedWeaponStep--;
            if (weaponNext && !_weaponNextDown)
                _queuedWeaponStep++;

            _weaponPreviousDown = weaponPrevious;
            _weaponNextDown = weaponNext;
            return true;
        }

        internal void ClearCapturedInput()
        {
            _forwardDown = false;
            _backDown = false;
            _leftDown = false;
            _rightDown = false;
            _fireDown = false;
            _useDown = false;
            _strafeDown = false;
            _runDown = false;
            _escapeDown = false;
            _weaponPreviousDown = false;
            _weaponNextDown = false;
            _accumulatedMouseX = 0;
            _turnHeldTics = 0;
        }

        int ScaleKeyboardTurn(int value)
        {
            int sensitivity = DoomInputSettings.GetKeyboardTurnSensitivity(
                _host == null ? null : _host.Config);
            return value * sensitivity / 100;
        }

        void QueueWeaponChange(Doom doom, int direction)
        {
            var game = doom.Game;
            var world = game == null ? null : game.World;
            var player = world == null ? null : world.ConsolePlayer;
            if (player == null || player.WeaponOwned == null)
                return;

            WeaponType current = player.PendingWeapon != WeaponType.NoChange
                ? player.PendingWeapon
                : player.ReadyWeapon;

            int start = (int)current;
            if (start < 0 || start >= EncodableWeaponCount)
                start = (int)player.ReadyWeapon;
            if (start < 0 || start >= EncodableWeaponCount)
                start = 0;

            for (int offset = 1; offset <= EncodableWeaponCount; offset++)
            {
                int weapon = start + direction * offset;
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
            long entityId = DoomInputSettings.GetCockpitEntityId(
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
