using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Games.EightBallPool
{
    public sealed class EightBallPoolGame : IGame
    {
        const string CUSTOM_DATA_KEY = "EightBallPool";
        const int BALL_COUNT = 16;
        const int CUE_BALL = 0;
        const float TABLE_HEIGHT = 0.5f;
        const float BALL_RADIUS = 0.0185f;
        const float CUE_BALL_RADIUS = BALL_RADIUS * 1.10f;
        const float POCKET_RADIUS = BALL_RADIUS * 1.15f;
        const float POCKET_FUNNEL_DEPTH = BALL_RADIUS * 0.4f;
        const float POCKET_TRANSITION_ZONE_RADIUS = BALL_RADIUS * 0.35f;
        const float POCKET_FUNNEL_OPENING_ANGLE_DEGREES = 5f;
        const float FRICTION = 0.988f;
        const float MIN_SPEED = 0.00007f;
        const float MAX_SHOT_SPEED = 0.018f;
        const float POCKET_ATTRACTION_BIAS = 0.32f;
        const float POCKET_ATTRACTION_MIN_SPEED = MAX_SHOT_SPEED * 0.035f;
        const float POCKET_ATTRACTION_EXTRA_PULL = MAX_SHOT_SPEED * 0.012f;
        const float POCKET_SHRINK_STEP = 1f / 14f;
        const int MAX_PHYSICS_STEPS = 4;
        const float STRIPE_BAND_RATIO = 0.42f;
        const int PLAYER_ONE = 1;
        const int PLAYER_TWO = 2;
        const int GROUP_OPEN = 0;
        const int GROUP_SOLIDS = 1;
        const int GROUP_STRIPES = 2;
        const float CUE_SPAWN_X = 0.25f;
        const float CUE_SPAWN_Y = TABLE_HEIGHT * 0.5f;
        const float CUE_RESPAWN_RADIUS = 0.126f;

        readonly IMyTextSurface _panel;
        readonly InteractiveSurfaceScript _script;
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly Ball[] _balls = new Ball[BALL_COUNT];
        readonly int[] _capturedBy = new int[BALL_COUNT];
        readonly List<int> _captureOrder = new List<int>(BALL_COUNT);
        readonly bool[] _shotStartPocketed = new bool[BALL_COUNT];
        readonly List<int> _shotPocketedBalls = new List<int>(BALL_COUNT);
        readonly float[] _pocketShrinkScales = new float[BALL_COUNT];
        readonly Vector2[] _pocketShrinkPositions = new Vector2[BALL_COUNT];
        readonly Vector2[] _pockets =
        {
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, TABLE_HEIGHT), new Vector2(0.5f, TABLE_HEIGHT), new Vector2(1f, TABLE_HEIGHT)
        };

        readonly GameThemeContext _themeContext;

        public IVisualStyleScope StyleParent
        {
            get { return _themeContext.StyleParent; }
        }

        public StyleTree Styles
        {
            get { return _themeContext.Styles; }
        }

        public ResourceTree Resources
        {
            get { return _themeContext.Resources; }
        }

        public bool IsDirty
        {
            get { return _themeContext.IsDirty; }
        }

        public void MarkDirty()
        {
            _themeContext.MarkDirty();
        }

        readonly Color _feltColor = new Color(24, 105, 64);
        readonly Color _railColor = new Color(82, 43, 22);
        readonly Color _bevelLight = new Color(120, 127, 135);
        readonly Color _bevelDark = new Color(39, 46, 53);
        readonly Color _pocketColor = new Color(5, 5, 5);
        readonly Color _textColor = new Color(236, 240, 241);
        readonly Color _shadowColor = new Color(0, 0, 0, 160);
        readonly Color _cueWoodColor = new Color(194, 156, 102);
        readonly Color _cueWoodAccentColor = new Color(124, 84, 48);
        readonly Color _cueGripColor = new Color(42, 22, 14);
        readonly Color _cueFerruleColor = new Color(240, 240, 240);

        readonly Color[] _ballColors =
        {
            new Color(245, 245, 245), new Color(241, 196, 15), new Color(52, 152, 219),
            new Color(231, 76, 60), new Color(155, 89, 182), new Color(230, 126, 34),
            new Color(39, 174, 96), new Color(128, 42, 42), new Color(15, 15, 15),
            new Color(241, 196, 15), new Color(52, 152, 219), new Color(231, 76, 60),
            new Color(155, 89, 182), new Color(230, 126, 34), new Color(39, 174, 96),
            new Color(128, 42, 42)
        };

        RectangleF _viewBox;
        RectangleF _headerRect;
        RectangleF _tableOuterRect;
        RectangleF _playRect;
        RectangleF _footerRect;
        RectangleF _powerMeterRect;
        bool _widePlayerLayout;
        float _railThickness;
        float _ballScreenRadius;
        float _fontScale;
        long _lastPhysicsFrame;
        bool _cueBallInHand;
        bool _wasMoving;
        bool _needsSaveWhenIdle;
        bool _selfUpdateQueued;
        bool _shotInProgress;
        bool _shotCueBallPocketed;
        bool _gameOver;
        bool _cheatMode;
        int _shotCount;
        int _lastPocketedBall;
        int _firstHitBall;
        int _lastPlayer = PLAYER_ONE;
        int _currentPlayer = PLAYER_ONE;
        int _playerOneGroup = GROUP_OPEN;
        int _winner;
        string _lastStatusMessage = string.Empty;
        RectangleControl _tableControl;
        readonly object _tableContext = new object();

        public List<Control> Interactive { get; private set; }
        public GameSurfaceScript.GameEnum Id => GameSurfaceScript.GameEnum.EightBallPool;

        struct Ball
        {
            public int Number;
            public Vector2 Position;
            public Vector2 Velocity;
            public bool Pocketed;
        }

        public EightBallPoolGame(IMyTextSurface panel, InteractiveSurfaceScript script)
        {
            _panel = panel;
            _script = script;
            _themeContext = new GameThemeContext(script);
            Interactive = new List<Control>();
            InitializeBallNumbers();
            ReloadProgram();
            BuildGlobalMenu();
        }

        void InitializeBallNumbers()
        {
            for (var i = 0; i < _balls.Length; i++) _balls[i].Number = i;
        }

        void BuildGlobalMenu()
        {
            _script.SetGlobalMenu(new GlobalMenuEntry("LcdMod_EightBallPool", new List<GlobalMenuEntry>
            {
                new GlobalMenuEntry("LcdMod_NewGame", delegate { ShowNewGameMessageBox(); }),
                new GlobalMenuEntry("LcdMod_RackBalls", delegate { RackObjectBalls(); ResetMatchState(true); Save(); }),
                new GlobalMenuEntry("LcdMod_ResetCueBall", delegate { ResetCueBall(); Save(); }),
                new GlobalMenuEntry
                {
                    MenuItem = Loc(_cheatMode ? "LcdMod_EightBallPool_CheatModeOn" : "LcdMod_EightBallPool_CheatModeOff"),
                    OnClick = delegate
                    {
                        _cheatMode = !_cheatMode;
                        BuildGlobalMenu();
                        Save();
                    }
                }
            }));
        }

        void ShowNewGameMessageBox()
        {
            _script.ShowMessageBox(Loc("LcdMod_NewGame"), Loc("LcdMod_EightBallPool_NewGamePrompt"),
                Loc("LcdMod_NewGame"), Loc("LcdMod_EightBallPool_Dismiss"), delegate
            {
                NewGame();
                Save();
            });
        }

        void ReloadProgram()
        {
            Load();
            BakeLayout();
        }

        public void Update()
        {
            var frame = GetCurrentGameplayFrame();
            if (_lastPhysicsFrame <= 0L) _lastPhysicsFrame = frame;
            var elapsed = Math.Max(1L, Math.Min(MAX_PHYSICS_STEPS, frame - _lastPhysicsFrame));
            _lastPhysicsFrame = frame;

            for (long step = 0; step < elapsed; step++) StepPhysics();

            var moving = AreBallsMoving();
            if (!moving && (_wasMoving || _shotInProgress))
            {
                if (_shotInProgress) ResolveCompletedShot();
                if (_needsSaveWhenIdle)
                {
                    _needsSaveWhenIdle = false;
                    Save();
                }
            }
            _wasMoving = moving;
            if (moving) QueueSelfUpdateIfNeeded();
        }

        public List<MySprite> GetSprites()
        {
            _sprites.Clear();
            if (_viewBox != _script.ViewBox || _playRect.Width <= 0f) BakeLayout();
            DrawTable(_sprites);
            DrawAimGuide(_sprites);
            DrawBalls(_sprites);
            DrawHud(_sprites);
            DrawFooter(_sprites);
            RebuildInteractiveEntries();
            return _sprites;
        }

        public void Save()
        {
            try
            {
                if (_script == null || _script.Config == null || _script.ProviderConfig == null) return;

                var terminalBlock = _script.Block as IMyTerminalBlock;
                if (terminalBlock == null || terminalBlock.MarkedForClose || terminalBlock.Closed) return;

                _script.Config.SetCustomData(CUSTOM_DATA_KEY, MyAPIGateway.Utilities.SerializeToBinary(BuildConfig()));
                ConfigManager.Sync(terminalBlock, _script.ProviderConfig);
            }
            catch (Exception e)
            {
                LogHelper.LogInfo(e.ToString());
            }
        }

        public void Load()
        {
            try
            {
                var config = LoadConfig();
                if (config == null || config.Positions == null || config.Velocities == null || config.Pocketed == null ||
                    config.Positions.Length != BALL_COUNT * 2 || config.Velocities.Length != BALL_COUNT * 2 ||
                    config.Pocketed.Length != BALL_COUNT) throw new Exception("Corrupted 8-ball pool table.");
                InitializeBallNumbers();
                for (var i = 0; i < BALL_COUNT; i++)
                {
                    var p = i * 2;
                    _balls[i].Position = new Vector2(config.Positions[p], config.Positions[p + 1]);
                    _balls[i].Velocity = new Vector2(config.Velocities[p], config.Velocities[p + 1]);
                    _balls[i].Pocketed = config.Pocketed[i];
                    _capturedBy[i] = config.CapturedBy != null && config.CapturedBy.Length == BALL_COUNT ? config.CapturedBy[i] : 0;
                }
                LoadCaptureOrder(config.CaptureOrder);
                _cueBallInHand = config.CueBallInHand || _balls[CUE_BALL].Pocketed;
                _shotCount = Math.Max(0, config.ShotCount);
                _lastPocketedBall = config.LastPocketedBall;
                _currentPlayer = config.CurrentPlayer == PLAYER_TWO ? PLAYER_TWO : PLAYER_ONE;
                _playerOneGroup = IsValidGroup(config.PlayerOneGroup) ? config.PlayerOneGroup : GROUP_OPEN;
                _gameOver = config.GameOver;
                _winner = config.Winner == PLAYER_ONE || config.Winner == PLAYER_TWO ? config.Winner : 0;
                _cheatMode = config.CheatMode;
                _lastStatusMessage = string.IsNullOrEmpty(config.LastStatusMessage) ? BuildTurnStatus() : config.LastStatusMessage;
                ReconcileCapturedBalls();
                ClearPocketShrinkAnimations();

                _lastPlayer = config.LastPlayer == PLAYER_TWO ? PLAYER_TWO : PLAYER_ONE;
                if (config.LastPlayer != PLAYER_ONE && config.LastPlayer != PLAYER_TWO)
                    _lastPlayer = _currentPlayer;

                _shotInProgress = config.ShotInProgress;
                _shotCueBallPocketed = config.ShotCueBallPocketed;
                _firstHitBall = config.FirstHitBall;

                if (config.ShotStartPocketed != null && config.ShotStartPocketed.Length == BALL_COUNT)
                {
                    for (var i = 0; i < BALL_COUNT; i++) _shotStartPocketed[i] = config.ShotStartPocketed[i];
                }
                else
                {
                    for (var i = 0; i < BALL_COUNT; i++) _shotStartPocketed[i] = _balls[i].Pocketed;
                }

                _shotPocketedBalls.Clear();
                if (config.ShotPocketedBalls != null)
                    foreach (var t in config.ShotPocketedBalls)
                        AddShotPocketedBall(t);

                if (!_shotInProgress)
                {
                    _shotCueBallPocketed = false;
                    _firstHitBall = -1;
                    for (var i = 0; i < BALL_COUNT; i++) _shotStartPocketed[i] = _balls[i].Pocketed;
                    _shotPocketedBalls.Clear();
                }
                _wasMoving = AreBallsMoving();
                _needsSaveWhenIdle = false;
                _lastPhysicsFrame = GetCurrentGameplayFrame();
                BuildGlobalMenu();
            }
            catch (Exception e)
            {
                LogHelper.LogInfo(e.ToString());
                NewGame();
                Save();
            }
        }

        public void LayoutChanged()
        {
            BuildGlobalMenu();
            BakeLayout();
        }

        EightBallPoolGameConfig LoadConfig()
        {
            var data = _script.Config.GetCustomData(CUSTOM_DATA_KEY);
            if (data == null || data.Length == 0) throw new Exception("Missing 8-ball pool config.");
            return MyAPIGateway.Utilities.SerializeFromBinary<EightBallPoolGameConfig>(data);
        }

        EightBallPoolGameConfig BuildConfig()
        {
            var positions = new float[BALL_COUNT * 2];
            var velocities = new float[BALL_COUNT * 2];
            var pocketed = new bool[BALL_COUNT];
            for (var i = 0; i < BALL_COUNT; i++)
            {
                var p = i * 2;
                positions[p] = _balls[i].Position.X;
                positions[p + 1] = _balls[i].Position.Y;
                velocities[p] = _balls[i].Velocity.X;
                velocities[p + 1] = _balls[i].Velocity.Y;
                pocketed[i] = _balls[i].Pocketed;
            }
            var capturedBy = new int[BALL_COUNT];
            for (var i = 0; i < BALL_COUNT; i++) capturedBy[i] = _capturedBy[i];
            var captureOrder = _captureOrder.ToArray();
            var shotStartPocketed = new bool[BALL_COUNT];
            for (var i = 0; i < BALL_COUNT; i++) shotStartPocketed[i] = _shotStartPocketed[i];
            var shotPocketedBalls = _shotPocketedBalls.ToArray();
            return new EightBallPoolGameConfig
            {
                Positions = positions,
                Velocities = velocities,
                Pocketed = pocketed,
                CueBallInHand = _cueBallInHand,
                ShotCount = _shotCount,
                LastPocketedBall = _lastPocketedBall,
                CurrentPlayer = _currentPlayer,
                PlayerOneGroup = _playerOneGroup,
                GameOver = _gameOver,
                Winner = _winner,
                CapturedBy = capturedBy,
                LastStatusMessage = _lastStatusMessage,
                ShotInProgress = _shotInProgress,
                ShotCueBallPocketed = _shotCueBallPocketed,
                FirstHitBall = _firstHitBall,
                ShotStartPocketed = shotStartPocketed,
                ShotPocketedBalls = shotPocketedBalls,
                LastPlayer = _lastPlayer,
                CheatMode = _cheatMode,
                CaptureOrder = captureOrder
            };
        }

        void NewGame()
        {
            InitializeBallNumbers();
            ResetCueBall();
            RackObjectBalls();
            _cueBallInHand = false;
            _shotCount = 0;
            ResetMatchState(true);
            ClearPocketShrinkAnimations();
            _wasMoving = false;
            _needsSaveWhenIdle = false;
            _selfUpdateQueued = false;
            _lastPhysicsFrame = GetCurrentGameplayFrame();
        }

        void ResetCueBall()
        {
            _balls[CUE_BALL].Position = new Vector2(CUE_SPAWN_X, CUE_SPAWN_Y);
            _balls[CUE_BALL].Velocity = Vector2.Zero;
            _balls[CUE_BALL].Pocketed = false;
            ClearPocketShrinkAnimation(CUE_BALL);
            _cueBallInHand = false;
            MoveCueBallToNearestLegalSpot();
        }

        void RackObjectBalls()
        {
            int[] rackOrder = { 1, 10, 3, 12, 8, 14, 5, 2, 11, 6, 15, 7, 13, 4, 9 };
            var gap = BALL_RADIUS * 2.08f;
            var startX = 0.70f;
            var centerY = TABLE_HEIGHT * 0.5f;
            var order = 0;
            for (var row = 0; row < 5; row++)
            {
                for (var col = 0; col <= row; col++)
                {
                    var number = rackOrder[order++];
                    var x = startX + row * gap * 0.88f;
                    var y = centerY + (col - row * 0.5f) * gap;
                    _balls[number].Position = new Vector2(x, y);
                    _balls[number].Velocity = Vector2.Zero;
                    _balls[number].Pocketed = false;
                    ClearPocketShrinkAnimation(number);
                }
            }
        }

        void BakeLayout()
        {
            _viewBox = _script.ViewBox;
            if (_viewBox.Width <= 0f || _viewBox.Height <= 0f) return;
            var headerHeight = Math.Max(32f, Math.Min(_viewBox.Height * 0.16f, 68f));
            var gap = Math.Max(4f, _viewBox.Height * 0.015f);
            _widePlayerLayout = _viewBox.Width > _viewBox.Height * 1.5f;

            var footerHeight = Math.Max(42f, Math.Min(_viewBox.Height * 0.18f, 76f));
            var sidePanelWidth = _widePlayerLayout ? MathHelper.Clamp(_viewBox.Width * 0.22f, 118f, 240f) : 0f;
            var maxOuterWidth = Math.Max(1f, _viewBox.Width * 0.96f - (_widePlayerLayout ? sidePanelWidth + gap : 0f));
            var maxOuterHeight = _widePlayerLayout
                ? Math.Max(1f, _viewBox.Height - headerHeight - gap * 2f)
                : Math.Max(1f, _viewBox.Height - headerHeight - footerHeight - gap * 3f);
            var railMinimum = Math.Min(8f, Math.Max(2f, Math.Min(maxOuterWidth, maxOuterHeight) * 0.08f));
            var playWidth = Math.Min(maxOuterWidth / 1.08f, maxOuterHeight / (TABLE_HEIGHT + 0.08f));
            playWidth = Math.Max(1f, playWidth);
            for (var i = 0; i < 4; i++)
            {
                _railThickness = Math.Max(railMinimum, playWidth * 0.04f);
                if (playWidth + _railThickness * 2f > maxOuterWidth)
                    playWidth = Math.Max(1f, maxOuterWidth - _railThickness * 2f);
                if (playWidth * TABLE_HEIGHT + _railThickness * 2f > maxOuterHeight)
                    playWidth = Math.Max(1f, (maxOuterHeight - _railThickness * 2f) / TABLE_HEIGHT);
            }
            var playHeight = playWidth * TABLE_HEIGHT;
            var outerWidth = playWidth + _railThickness * 2f;
            var outerHeight = playHeight + _railThickness * 2f;
            var contentWidth = outerWidth + (_widePlayerLayout ? gap + sidePanelWidth : 0f);
            contentWidth = Math.Min(_viewBox.Width * 0.96f, contentWidth);
            var contentX = _viewBox.Center.X - contentWidth * 0.5f;

            _headerRect = new RectangleF(contentX, _viewBox.Y, contentWidth, headerHeight);
            var tableY = _headerRect.Bottom + gap;
            if (_widePlayerLayout && outerHeight < maxOuterHeight)
                tableY += (maxOuterHeight - outerHeight) * 0.5f;

            _tableOuterRect = new RectangleF(contentX, tableY, outerWidth, outerHeight);
            _playRect = new RectangleF(_tableOuterRect.X + _railThickness, _tableOuterRect.Y + _railThickness, playWidth, playHeight);
            _footerRect = _widePlayerLayout
                ? new RectangleF(_tableOuterRect.Right + gap, _tableOuterRect.Y, sidePanelWidth, _tableOuterRect.Height)
                : new RectangleF(_headerRect.X, _tableOuterRect.Bottom + gap, _headerRect.Width, footerHeight);
            _ballScreenRadius = BALL_RADIUS * _playRect.Width;
            var meterWidth = Math.Min(_headerRect.Width * 0.34f, 180f);
            var meterHeight = Math.Max(6f, _headerRect.Height * 0.12f);
            _powerMeterRect = new RectangleF(_headerRect.Right - meterWidth, _headerRect.Bottom - meterHeight - 4f, meterWidth, meterHeight);
            var textSize = _panel.MeasureStringInPixels(new StringBuilder("Shots: 000"), "White", 1f);
            _fontScale = textSize.Y <= 0f ? 0.6f : MathHelper.Clamp(_headerRect.Height * 0.27f / textSize.Y, 0.35f, 0.9f);
        }

        void RebuildInteractiveEntries()
        {
            Interactive.Clear();
            if (_playRect.Width <= 0f || _playRect.Height <= 0f) return;
            if (_tableControl == null)
            {
                _tableControl = new RectangleControl(_playRect, CursorType.Hand, _tableContext, ClickTableFromEntry)
                {
                    CustomRender = delegate { },
                    OnSecondaryClick = PlaceCueBallFromEntry,
                    ClickSound = AudioHelper.HudClick
                };
            }
            _tableControl.SetRect(_playRect);
            _tableControl.SetCursor(GetTableCursorType());
            _tableControl.SetDataContext(_tableContext);
            _tableControl.SetOnClick(ClickTableFromEntry);
            _tableControl.OnSecondaryClick = PlaceCueBallFromEntry;
            _tableControl.SetVisible(true);
            Interactive.Add(_tableControl);
        }

        CursorType GetTableCursorType()
        {
            if (AreBallsMoving()) return CursorType.WaitCursor;
            if (_gameOver) return CursorType.Hand;
            if (_cueBallInHand || _balls[CUE_BALL].Pocketed) return CursorType.Hand;

            Vector2 tablePoint;
            if (!TryGetCursorTablePoint(out tablePoint)) return CursorType.Hand;
            if ((tablePoint - _balls[CUE_BALL].Position).Length() >= GetBallPhysicsRadius(CUE_BALL) * 1.5f)
                return CursorType.None;

            return CursorType.Hand;
        }

        void ClickTableFromEntry(object value, object sender)
        {
            if (_gameOver)
            {
                ShowNewGameMessageBox();
                return;
            }
            if (AreBallsMoving()) return;
            Vector2 tablePoint;
            if (!TryGetCursorTablePoint(out tablePoint)) return;
            if (_cueBallInHand || _balls[CUE_BALL].Pocketed)
            {
                if (PlaceCueBall(tablePoint)) Save();
                return;
            }
            ShootCueBall(tablePoint);
        }

        void PlaceCueBallFromEntry(object value, object sender)
        {
            if (_gameOver || AreBallsMoving()) return;
            if (!_cheatMode)
            {
                _script.PlaySounds(AudioHelper.HudUnable);
                return;
            }
            Vector2 tablePoint;
            if (!TryGetCursorTablePoint(out tablePoint)) return;
            if (PlaceCueBall(tablePoint)) Save();
        }

        bool TryGetCursorTablePoint(out Vector2 tablePoint)
        {
            tablePoint = Vector2.Zero;
            var cursor = _script.CursorPosition;
            if (float.IsNaN(cursor.X) || float.IsNaN(cursor.Y) || !_playRect.Contains(cursor)) return false;
            var x = (cursor.X - _playRect.X) / Math.Max(1f, _playRect.Width);
            var y = (cursor.Y - _playRect.Y) / Math.Max(1f, _playRect.Height) * TABLE_HEIGHT;
            tablePoint = new Vector2(MathHelper.Clamp(x, BALL_RADIUS, 1f - BALL_RADIUS),
                MathHelper.Clamp(y, BALL_RADIUS, TABLE_HEIGHT - BALL_RADIUS));
            return true;
        }

        void ShootCueBall(Vector2 target)
        {
            if (_gameOver) return;
            var cue = _balls[CUE_BALL];
            if (cue.Pocketed) return;
            var delta = target - cue.Position;
            var length = delta.Length();
            if (length < GetBallPhysicsRadius(CUE_BALL) * 1.5f) return;
            var direction = delta / length;
            var power = MathHelper.Clamp(length / 0.46f, 0.18f, 1f);
            BeginShotTracking();
            _balls[CUE_BALL].Velocity = direction * (MAX_SHOT_SPEED * power);
            _shotCount++;
            _lastPocketedBall = -1;
            _needsSaveWhenIdle = true;
            _wasMoving = true;
            Save();
            QueueSelfUpdateIfNeeded();
        }

        bool PlaceCueBall(Vector2 tablePoint)
        {
            var respawnRestricted = IsCueBallRespawning();
            if (respawnRestricted && !IsInCueRespawnZone(tablePoint))
            {
                _script.PlaySounds(AudioHelper.HudUnable);
                return false;
            }

            var legalPosition = FindNearestLegalCuePosition(tablePoint, respawnRestricted);
            if (respawnRestricted && !IsInCueRespawnZone(legalPosition))
            {
                _script.PlaySounds(AudioHelper.HudUnable);
                return false;
            }

            _balls[CUE_BALL].Position = legalPosition;
            _balls[CUE_BALL].Velocity = Vector2.Zero;
            _balls[CUE_BALL].Pocketed = false;
            _cueBallInHand = false;
            return true;
        }

        void MoveCueBallToNearestLegalSpot()
        {
            _balls[CUE_BALL].Position = FindNearestLegalCuePosition(_balls[CUE_BALL].Position, false);
        }

        Vector2 FindNearestLegalCuePosition(Vector2 desired, bool restrictToRespawnZone)
        {
            var cueRadius = GetBallPhysicsRadius(CUE_BALL);
            desired = ClampToTable(desired, cueRadius);
            if (IsCuePositionLegal(desired, restrictToRespawnZone)) return desired;
            var step = cueRadius * 2.15f;
            for (var ring = 1; ring < 10; ring++)
            {
                var samples = ring * 12;
                for (var i = 0; i < samples; i++)
                {
                    var a = Math.PI * 2.0 * i / samples;
                    var candidate = desired + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * step * ring;
                    candidate = ClampToTable(candidate, cueRadius);
                    if (IsCuePositionLegal(candidate, restrictToRespawnZone)) return candidate;
                }
            }
            return restrictToRespawnZone ? new Vector2(CUE_SPAWN_X - CUE_RESPAWN_RADIUS * 0.5f, CUE_SPAWN_Y) : new Vector2(CUE_SPAWN_X, CUE_SPAWN_Y);
        }

        bool IsCuePositionLegal(Vector2 position, bool restrictToRespawnZone)
        {
            if (restrictToRespawnZone && !IsInCueRespawnZone(position)) return false;

            var cueRadius = GetBallPhysicsRadius(CUE_BALL);
            for (var i = 1; i < BALL_COUNT; i++)
            {
                if (_balls[i].Pocketed) continue;
                var minDistance = cueRadius + GetBallPhysicsRadius(i);
                if (Vector2.DistanceSquared(position, _balls[i].Position) < minDistance * minDistance * 1.08f) return false;
            }
            foreach (var t in _pockets)
            {
                var forbiddenRadius = Math.Max(POCKET_RADIUS * 0.85f, cueRadius * 0.85f);
                if (Vector2.DistanceSquared(position, t) < forbiddenRadius * forbiddenRadius) return false;
            }
            return true;
        }

        bool IsCueBallRespawning()
        {
            return _cueBallInHand || _balls[CUE_BALL].Pocketed;
        }

        float GetBallPhysicsRadius(int ballNumber)
        {
            return ballNumber == CUE_BALL ? CUE_BALL_RADIUS : BALL_RADIUS;
        }

        bool IsInCueRespawnZone(Vector2 position)
        {
            if (position.X > CUE_SPAWN_X + 0.0001f) return false;
            var spawn = new Vector2(CUE_SPAWN_X, CUE_SPAWN_Y);
            return Vector2.DistanceSquared(position, spawn) <= CUE_RESPAWN_RADIUS * CUE_RESPAWN_RADIUS;
        }

        Vector2 ClampToTable(Vector2 position, float ballRadius)
        {
            return new Vector2(MathHelper.Clamp(position.X, ballRadius, 1f - ballRadius),
                MathHelper.Clamp(position.Y, ballRadius, TABLE_HEIGHT - ballRadius));
        }

        Vector2 ClampToTableOrPocketFunnel(Vector2 position, int ballNumber)
        {
            var ballRadius = GetBallPhysicsRadius(ballNumber);
            if (IsInsideRegularTableBounds(position) || IsInsideAnyPocketOpening(position, ballRadius))
                return position;

            Vector2 clampedToFunnel;
            if (TryClampToPocketFunnel(position, ballRadius, out clampedToFunnel))
                return clampedToFunnel;

            return ClampToTable(position, ballRadius);
        }

        bool IsInsideRegularTableBounds(Vector2 position)
        {
            return position.X >= 0f && position.X <= 1f &&
                   position.Y >= 0f && position.Y <= TABLE_HEIGHT;
        }

        bool TryClampToPocketFunnel(Vector2 position, float ballRadius, out Vector2 clamped)
        {
            clamped = position;
            var found = false;
            var bestOutside = float.MaxValue;

            for (var i = 0; i < _pockets.Length; i++)
            {
                var narrowCenter = GetPhysicsPocketCenter(i);
                var insideDirection = GetPocketInsideDirection(i);
                var perpendicular = new Vector2(-insideDirection.Y, insideDirection.X);
                var delta = position - narrowCenter;
                var along = Vector2.Dot(delta, insideDirection);
                var edgeAlong = GetPocketFunnelEdgeAlong(i);
                if (edgeAlong <= 0.0001f || along < 0f || along > edgeAlong) continue;

                var slope = GetPocketFunnelOpeningSlope();
                var wallHalf = POCKET_RADIUS + slope * along;
                var centerHalf = Math.Max(0f, wallHalf - ballRadius);
                var lateral = Vector2.Dot(delta, perpendicular);
                var outsideAmount = Math.Abs(lateral) - centerHalf;
                if (outsideAmount < 0f || outsideAmount > ballRadius * 2.25f) continue;

                var side = lateral >= 0f ? 1f : -1f;
                var candidate = narrowCenter + insideDirection * along + perpendicular * (side * centerHalf);
                if (outsideAmount < bestOutside)
                {
                    bestOutside = outsideAmount;
                    clamped = candidate;
                    found = true;
                }
            }

            return found;
        }

        void StepPhysics()
        {
            var movingBalls = ArePhysicalBallsMoving();
            var pocketAnimations = ArePocketShrinkAnimationsRunning();
            if (!movingBalls && !pocketAnimations) return;

            if (pocketAnimations)
                UpdatePocketShrinkAnimations();

            if (!movingBalls) return;

            for (var i = 0; i < BALL_COUNT; i++)
            {
                if (_balls[i].Pocketed) continue;
                _balls[i].Position += _balls[i].Velocity;
                var centerInsidePocketHole = ApplyPocketCapture(i);
                if (_balls[i].Pocketed) continue;

                if (!centerInsidePocketHole)
                {
                    ApplyRailBounce(i);
                    ApplyPocketCapture(i);
                    if (_balls[i].Pocketed) continue;
                }

                _balls[i].Velocity *= FRICTION;
                if (_balls[i].Velocity.LengthSquared() < MIN_SPEED * MIN_SPEED) _balls[i].Velocity = Vector2.Zero;
            }
            for (var pass = 0; pass < 3; pass++) ResolveBallCollisions();
        }

        void ApplyRailBounce(int index)
        {
            var ball = _balls[index];
            if (ball.Pocketed) return;

            var position = ball.Position;
            var velocity = ball.Velocity;
            var ballRadius = GetBallPhysicsRadius(index);
            var bounced = false;

            for (var pass = 0; pass < 2; pass++)
            {
                ApplyStraightRailWallBounces(ref position, ref velocity, ref bounced, ballRadius);
                ApplyPocketFunnelWallBounces(ref position, ref velocity, ref bounced, ballRadius);
                ApplyPocketTransitionZoneBounces(ref position, ref velocity, ref bounced, ballRadius);
            }

            if (bounced)
            {
                _balls[index].Position = position;
                _balls[index].Velocity = velocity;
                _needsSaveWhenIdle = true;
            }
        }

        void ApplyStraightRailWallBounces(ref Vector2 position, ref Vector2 velocity, ref bool bounced, float ballRadius)
        {
            Vector2 a;
            Vector2 b;

            AddHorizontalWallFromPocketSpans(0, 1, 0f, ref position, ref velocity, ref bounced, ballRadius, new Vector2(0f, 1f));
            AddHorizontalWallFromPocketSpans(1, 2, 0f, ref position, ref velocity, ref bounced, ballRadius, new Vector2(0f, 1f));

            AddHorizontalWallFromPocketSpans(3, 4, TABLE_HEIGHT, ref position, ref velocity, ref bounced, ballRadius, new Vector2(0f, -1f));
            AddHorizontalWallFromPocketSpans(4, 5, TABLE_HEIGHT, ref position, ref velocity, ref bounced, ballRadius, new Vector2(0f, -1f));

            GetPocketMouthEndpoints(0, out a, out b);
            var leftTopEnd = Math.Max(a.Y, b.Y);
            GetPocketMouthEndpoints(3, out a, out b);
            var leftBottomStart = Math.Min(a.Y, b.Y);
            ApplyWallSegmentBounce(new Vector2(0f, leftTopEnd), new Vector2(0f, leftBottomStart), new Vector2(1f, 0f),
                ref position, ref velocity, ref bounced, ballRadius, 0.92f, 1f);

            GetPocketMouthEndpoints(2, out a, out b);
            var rightTopEnd = Math.Max(a.Y, b.Y);
            GetPocketMouthEndpoints(5, out a, out b);
            var rightBottomStart = Math.Min(a.Y, b.Y);
            ApplyWallSegmentBounce(new Vector2(1f, rightTopEnd), new Vector2(1f, rightBottomStart), new Vector2(-1f, 0f),
                ref position, ref velocity, ref bounced, ballRadius, 0.92f, 1f);
        }

        void AddHorizontalWallFromPocketSpans(int leftPocketIndex, int rightPocketIndex, float y,
            ref Vector2 position, ref Vector2 velocity, ref bool bounced, float ballRadius, Vector2 inwardNormal)
        {
            Vector2 a;
            Vector2 b;
            GetPocketMouthEndpoints(leftPocketIndex, out a, out b);
            var startX = Math.Max(a.X, b.X);
            GetPocketMouthEndpoints(rightPocketIndex, out a, out b);
            var endX = Math.Min(a.X, b.X);

            ApplyWallSegmentBounce(new Vector2(startX, y), new Vector2(endX, y), inwardNormal,
                ref position, ref velocity, ref bounced, ballRadius, 0.92f, 1f);
        }

        void ApplyPocketFunnelWallBounces(ref Vector2 position, ref Vector2 velocity, ref bool bounced, float ballRadius)
        {
            for (var i = 0; i < _pockets.Length; i++)
            {
                if (!IsOnPocketSideOfMouth(position, i, ballRadius * 0.03f))
                    continue;

                Vector2 narrowLeft;
                Vector2 narrowRight;
                Vector2 mouthLeft;
                Vector2 mouthRight;
                Vector2 centerAtMiddle;
                GetPocketFunnelWallGeometry(i, out narrowLeft, out narrowRight, out mouthLeft, out mouthRight, out centerAtMiddle);

                var leftNormal = BuildInwardNormal(narrowLeft, mouthLeft, centerAtMiddle);
                var rightNormal = BuildInwardNormal(narrowRight, mouthRight, centerAtMiddle);

                ApplyWallSegmentBounce(narrowLeft, mouthLeft, leftNormal,
                    ref position, ref velocity, ref bounced, ballRadius, 0.92f, 0.965f);
                ApplyWallSegmentBounce(narrowRight, mouthRight, rightNormal,
                    ref position, ref velocity, ref bounced, ballRadius, 0.92f, 0.965f);
            }
        }

        void ApplyPocketTransitionZoneBounces(ref Vector2 position, ref Vector2 velocity, ref bool bounced, float ballRadius)
        {
            var transitionRadius = POCKET_TRANSITION_ZONE_RADIUS;

            for (var i = 0; i < _pockets.Length; i++)
            {
                if (!IsOnPocketSideOfMouth(position, i, ballRadius * 0.03f))
                    continue;

                Vector2 transitionLeft;
                Vector2 transitionRight;
                GetPocketTransitionZoneCenters(i, out transitionLeft, out transitionRight);

                ApplyCircleWallBounce(transitionLeft, transitionRadius,
                    ref position, ref velocity, ref bounced, ballRadius, 0.92f, 0.965f);
                ApplyCircleWallBounce(transitionRight, transitionRadius,
                    ref position, ref velocity, ref bounced, ballRadius, 0.92f, 0.965f);
            }
        }

        bool IsOnPocketSideOfMouth(Vector2 position, int pocketIndex, float margin)
        {
            var narrowCenter = GetPhysicsPocketCenter(pocketIndex);
            var insideDirection = GetPocketInsideDirection(pocketIndex);
            var along = Vector2.Dot(position - narrowCenter, insideDirection);
            var edgeAlong = GetPocketFunnelEdgeAlong(pocketIndex);
            return edgeAlong > 0.0001f && along <= edgeAlong + margin;
        }

        void ApplyWallSegmentBounce(Vector2 start, Vector2 end, Vector2 inwardNormal,
            ref Vector2 position, ref Vector2 velocity, ref bool bounced, float ballRadius, float restitution, float damping)
        {
            var segment = end - start;
            var segmentLengthSquared = segment.LengthSquared();
            if (segmentLengthSquared <= 0.0000001f) return;

            if (inwardNormal.LengthSquared() <= 0.000001f) return;
            inwardNormal.Normalize();

            var t = Vector2.Dot(position - start, segment) / segmentLengthSquared;
            if (t < 0f || t > 1f) return;
            var closest = start + segment * t;

            var signedDistance = Vector2.Dot(position - closest, inwardNormal);
            if (signedDistance >= ballRadius) return;

            var penetration = ballRadius - signedDistance + 0.0001f;
            position += inwardNormal * penetration;

            var inwardSpeed = Vector2.Dot(velocity, inwardNormal);
            if (inwardSpeed < 0f)
            {
                velocity -= inwardNormal * (inwardSpeed * (1f + restitution));
                velocity *= damping;
            }

            bounced = true;
        }

        void ApplyCircleWallBounce(Vector2 center, float wallRadius,
            ref Vector2 position, ref Vector2 velocity, ref bool bounced, float ballRadius, float restitution, float damping)
        {
            var minDistance = wallRadius + ballRadius;
            var delta = position - center;
            var distanceSquared = delta.LengthSquared();
            if (distanceSquared >= minDistance * minDistance) return;

            Vector2 normal;
            var distance = (float)Math.Sqrt(Math.Max(0f, distanceSquared));
            if (distance > 0.00001f) normal = delta / distance;
            else normal = Vector2.UnitX;

            position = center + normal * (minDistance + 0.0001f);

            var outwardSpeed = Vector2.Dot(velocity, normal);
            if (outwardSpeed < 0f)
            {
                velocity -= normal * (outwardSpeed * (1f + restitution));
                velocity *= damping;
            }

            bounced = true;
        }

        void GetPocketMouthEndpoints(int pocketIndex, out Vector2 mouthLeft, out Vector2 mouthRight)
        {
            var narrowCenter = GetPhysicsPocketCenter(pocketIndex);
            var mouthCenter = GetPocketFunnelMouthCenter(pocketIndex);
            var direction = mouthCenter - narrowCenter;
            if (direction.LengthSquared() <= 0.000001f)
                direction = GetPocketInsideDirection(pocketIndex);
            else
                direction.Normalize();

            var perpendicular = new Vector2(-direction.Y, direction.X);
            var mouthHalf = GetPocketFunnelMouthHalfWidth(pocketIndex);
            mouthLeft = mouthCenter + perpendicular * mouthHalf;
            mouthRight = mouthCenter - perpendicular * mouthHalf;
        }

        void GetPocketTransitionZoneCenters(int pocketIndex, out Vector2 transitionLeft, out Vector2 transitionRight)
        {
            Vector2 mouthLeft;
            Vector2 mouthRight;
            GetPocketMouthEndpoints(pocketIndex, out mouthLeft, out mouthRight);

            var transitionRadius = POCKET_TRANSITION_ZONE_RADIUS;
            transitionLeft = mouthLeft + GetPocketTransitionOutwardNormal(pocketIndex, mouthLeft) * transitionRadius;
            transitionRight = mouthRight + GetPocketTransitionOutwardNormal(pocketIndex, mouthRight) * transitionRadius;
        }

        Vector2 GetPocketTransitionOutwardNormal(int pocketIndex, Vector2 mouthEndpoint)
        {
            const float edgeEpsilon = 0.00075f;

            if (mouthEndpoint.Y <= edgeEpsilon) return new Vector2(0f, -1f);
            if (mouthEndpoint.Y >= TABLE_HEIGHT - edgeEpsilon) return new Vector2(0f, 1f);
            if (mouthEndpoint.X <= edgeEpsilon) return new Vector2(-1f, 0f);
            if (mouthEndpoint.X >= 1f - edgeEpsilon) return new Vector2(1f, 0f);

            var fallback = -GetPocketInsideDirection(pocketIndex);
            if (fallback.LengthSquared() <= 0.000001f) return Vector2.Zero;
            fallback.Normalize();
            return fallback;
        }

        void GetPocketFunnelWallGeometry(int pocketIndex, out Vector2 narrowLeft, out Vector2 narrowRight,
            out Vector2 mouthLeft, out Vector2 mouthRight, out Vector2 centerAtMiddle)
        {
            var narrowCenter = GetPhysicsPocketCenter(pocketIndex);
            var mouthCenter = GetPocketFunnelMouthCenter(pocketIndex);
            var direction = mouthCenter - narrowCenter;
            if (direction.LengthSquared() <= 0.000001f)
                direction = GetPocketInsideDirection(pocketIndex);
            else
                direction.Normalize();

            var perpendicular = new Vector2(-direction.Y, direction.X);
            var narrowHalf = POCKET_RADIUS;
            var mouthHalf = GetPocketFunnelMouthHalfWidth(pocketIndex);

            narrowLeft = narrowCenter + perpendicular * narrowHalf;
            narrowRight = narrowCenter - perpendicular * narrowHalf;

            var nominalMouthLeft = mouthCenter + perpendicular * mouthHalf;
            var nominalMouthRight = mouthCenter - perpendicular * mouthHalf;

            Vector2 transitionLeft;
            Vector2 transitionRight;
            GetPocketTransitionZoneCenters(pocketIndex, out transitionLeft, out transitionRight);

            var transitionRadius = POCKET_TRANSITION_ZONE_RADIUS;
            mouthLeft = MoveWallEndToCircleTangent(narrowLeft, nominalMouthLeft, transitionLeft, transitionRadius);
            mouthRight = MoveWallEndToCircleTangent(narrowRight, nominalMouthRight, transitionRight, transitionRadius);

            centerAtMiddle = Vector2.Lerp(narrowCenter, mouthCenter, 0.5f);
        }

        Vector2 MoveWallEndToCircleTangent(Vector2 start, Vector2 nominalEnd, Vector2 circleCenter, float circleRadius)
        {
            var segment = nominalEnd - start;
            var a = segment.LengthSquared();
            if (a <= 0.0000001f || circleRadius <= 0.00001f) return nominalEnd;

            var fromCenter = start - circleCenter;
            var c = fromCenter.LengthSquared() - circleRadius * circleRadius;
            if (c <= 0f)
                return start;

            var b = 2f * Vector2.Dot(fromCenter, segment);
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return nominalEnd;

            var sqrt = (float)Math.Sqrt(discriminant);
            var inv = 1f / (2f * a);
            var t1 = (-b - sqrt) * inv;
            var t2 = (-b + sqrt) * inv;
            var best = 2f;

            if (t1 > 0.0001f && t1 < 0.9999f) best = t1;
            if (t2 > 0.0001f && t2 < 0.9999f && t2 < best) best = t2;

            if (best <= 1f) return start + segment * best;
            return nominalEnd;
        }

        Vector2 BuildInwardNormal(Vector2 wallStart, Vector2 wallEnd, Vector2 insidePoint)
        {
            var wall = wallEnd - wallStart;
            if (wall.LengthSquared() <= 0.000001f) return Vector2.UnitX;

            var normal = new Vector2(-wall.Y, wall.X);
            var wallMid = (wallStart + wallEnd) * 0.5f;
            if (Vector2.Dot(insidePoint - wallMid, normal) < 0f)
                normal = -normal;

            normal.Normalize();
            return normal;
        }
        
        bool ApplyPocketCapture(int index)
        {
            var ballRadius = GetBallPhysicsRadius(index);

            for (var i = 0; i < _pockets.Length; i++)
            {
                var pocketCenter = GetPhysicsPocketCenter(i);
                var toCenter = pocketCenter - _balls[index].Position;
                var distanceSquared = toCenter.LengthSquared();
                var pocketRadius = POCKET_RADIUS;
                if (distanceSquared > pocketRadius * pocketRadius)
                    continue;

                var distance = (float)Math.Sqrt(Math.Max(0f, distanceSquared));
                if (distance + ballRadius <= pocketRadius)
                {
                    PocketBallWithShrinkAnimation(index, pocketCenter);
                    return true;
                }

                ApplyPocketCenterBias(index, toCenter, distance, pocketRadius);
                return true;
            }

            return false;
        }

        void ApplyPocketCenterBias(int index, Vector2 toCenter, float distance, float pocketRadius)
        {
            if (distance <= 0.000001f) return;

            var direction = toCenter / distance;
            var velocity = _balls[index].Velocity;
            var speed = velocity.Length();
            var normalizedDepth = 1f - MathHelper.Clamp(distance / Math.Max(0.0001f, pocketRadius), 0f, 1f);
            var targetSpeed = Math.Max(speed, POCKET_ATTRACTION_MIN_SPEED + POCKET_ATTRACTION_EXTRA_PULL * normalizedDepth);
            var targetVelocity = direction * targetSpeed;
            var bias = MathHelper.Clamp(POCKET_ATTRACTION_BIAS + normalizedDepth * 0.28f, 0f, 0.72f);

            velocity = Vector2.Lerp(velocity, targetVelocity, bias);

            // Ensure there is always a small inward component once the center is in
            // the hole. This prevents a slow ball from stopping on the lip.
            var inwardSpeed = Vector2.Dot(velocity, direction);
            var minimumInwardSpeed = Math.Max(MIN_SPEED * 8f, POCKET_ATTRACTION_MIN_SPEED * 0.45f);
            if (inwardSpeed < minimumInwardSpeed)
                velocity += direction * (minimumInwardSpeed - inwardSpeed);

            _balls[index].Velocity = velocity;
            _needsSaveWhenIdle = true;
        }

        void PocketBallWithShrinkAnimation(int index, Vector2 pocketCenter)
        {
            _balls[index].Pocketed = true;
            _balls[index].Velocity = Vector2.Zero;
            _balls[index].Position = pocketCenter;
            _pocketShrinkPositions[index] = pocketCenter;
            _pocketShrinkScales[index] = 1f;
            _needsSaveWhenIdle = true;

            if (_shotInProgress)
            {
                if (index == CUE_BALL) _shotCueBallPocketed = true;
                AddShotPocketedBall(index);
            }

            if (index == CUE_BALL) _cueBallInHand = true;
            else _lastPocketedBall = index;
        }

        void UpdatePocketShrinkAnimations()
        {
            for (var i = 0; i < BALL_COUNT; i++)
            {
                if (_pocketShrinkScales[i] <= 0f) continue;
                _pocketShrinkScales[i] = Math.Max(0f, _pocketShrinkScales[i] - POCKET_SHRINK_STEP);
            }
        }

        bool ArePocketShrinkAnimationsRunning()
        {
            for (var i = 0; i < BALL_COUNT; i++)
                if (_pocketShrinkScales[i] > 0f) return true;
            return false;
        }

        void ClearPocketShrinkAnimation(int index)
        {
            if (index < 0 || index >= BALL_COUNT) return;
            _pocketShrinkScales[index] = 0f;
            _pocketShrinkPositions[index] = Vector2.Zero;
        }

        void ClearPocketShrinkAnimations()
        {
            for (var i = 0; i < BALL_COUNT; i++)
                ClearPocketShrinkAnimation(i);
        }

        bool IsInsideAnyPocketOpening(Vector2 position, float ballRadius)
        {
            for (var i = 0; i < _pockets.Length; i++)
            {
                if (IsInsidePocketHole(position, i) || IsInsidePocketFunnelWithMargin(position, i, ballRadius * 0.02f)) return true;
            }
            return false;
        }

        bool IsInsidePocketHole(Vector2 position, int pocketIndex)
        {
            var pocketCenter = GetPhysicsPocketCenter(pocketIndex);
            var radius = POCKET_RADIUS;
            return Vector2.DistanceSquared(position, pocketCenter) <= radius * radius;
        }

        bool IsInsidePocketFunnelWithMargin(Vector2 position, int pocketIndex, float margin)
        {
            var narrowCenter = GetPhysicsPocketCenter(pocketIndex);
            var insideDirection = GetPocketInsideDirection(pocketIndex);
            var delta = position - narrowCenter;
            var along = Vector2.Dot(delta, insideDirection);
            var edgeAlong = GetPocketFunnelEdgeAlong(pocketIndex);
            if (edgeAlong <= 0.0001f || along < -margin || along > edgeAlong + margin) return false;

            var clampedAlong = MathHelper.Clamp(along, 0f, edgeAlong);
            var allowedHalf = POCKET_RADIUS + GetPocketFunnelOpeningSlope() * clampedAlong + margin;
            var lateral = Math.Abs(delta.X * insideDirection.Y - delta.Y * insideDirection.X);
            return lateral <= allowedHalf;
        }

        Vector2 GetPhysicsPocketCenter(int pocketIndex)
        {
            var insideDirection = GetPocketInsideDirection(pocketIndex);
            return _pockets[pocketIndex] - insideDirection * POCKET_FUNNEL_DEPTH;
        }

        float GetPocketFunnelOpeningSlope()
        {
            return (float)Math.Tan(MathHelper.ToRadians(MathHelper.Clamp(POCKET_FUNNEL_OPENING_ANGLE_DEGREES, 0f, 35f)));
        }

        float GetPocketFunnelBaseAlong(int pocketIndex)
        {
            var narrowCenter = GetPhysicsPocketCenter(pocketIndex);
            var insideDirection = GetPocketInsideDirection(pocketIndex);
            return Math.Max(0.0001f, Vector2.Dot(_pockets[pocketIndex] - narrowCenter, insideDirection));
        }

        float GetPocketFunnelMouthHalfWidth(int pocketIndex)
        {
            var baseAlong = GetPocketFunnelBaseAlong(pocketIndex);
            var slope = GetPocketFunnelOpeningSlope();

            if (IsCornerPocket(pocketIndex))
            {
                return Math.Max(POCKET_RADIUS, (POCKET_RADIUS + slope * baseAlong) / Math.Max(0.0001f, 1f - slope));
            }

            return Math.Max(POCKET_RADIUS, POCKET_RADIUS + slope * baseAlong);
        }

        Vector2 GetPocketFunnelMouthCenter(int pocketIndex)
        {
            var pocket = _pockets[pocketIndex];
            if (!IsCornerPocket(pocketIndex))
                return pocket;

            var insideDirection = GetPocketInsideDirection(pocketIndex);
            return pocket + insideDirection * GetPocketFunnelMouthHalfWidth(pocketIndex);
        }

        float GetPocketFunnelEdgeAlong(int pocketIndex)
        {
            var narrowCenter = GetPhysicsPocketCenter(pocketIndex);
            var insideDirection = GetPocketInsideDirection(pocketIndex);
            var mouthCenter = GetPocketFunnelMouthCenter(pocketIndex);
            return Vector2.Dot(mouthCenter - narrowCenter, insideDirection);
        }

        Vector2 GetPocketInsideDirection(int pocketIndex)
        {
            var pocket = _pockets[pocketIndex];
            var left = pocket.X <= 0.001f;
            var right = pocket.X >= 1f - 0.001f;
            var top = pocket.Y <= 0.001f;
            var bottom = pocket.Y >= TABLE_HEIGHT - 0.001f;

            Vector2 direction;
            if ((left || right) && (top || bottom))
                direction = new Vector2(left ? 1f : -1f, top ? 1f : -1f);
            else if (top || bottom)
                direction = new Vector2(0f, top ? 1f : -1f);
            else
                direction = new Vector2(left ? 1f : -1f, 0f);

            direction.Normalize();
            return direction;
        }

        static bool IsCornerPocket(int pocketIndex)
        {
            return pocketIndex == 0 || pocketIndex == 2 || pocketIndex == 3 || pocketIndex == 5;
        }

        void ResolveBallCollisions()
        {
            for (var a = 0; a < BALL_COUNT - 1; a++)
            {
                if (_balls[a].Pocketed) continue;
                for (var b = a + 1; b < BALL_COUNT; b++)
                {
                    if (_balls[b].Pocketed) continue;
                    var radiusA = GetBallPhysicsRadius(a);
                    var radiusB = GetBallPhysicsRadius(b);
                    var minDistance = radiusA + radiusB;
                    var minDistanceSquared = minDistance * minDistance;
                    var delta = _balls[b].Position - _balls[a].Position;
                    var distSquared = delta.LengthSquared();
                    if (distSquared <= 0f || distSquared >= minDistanceSquared) continue;
                    if (_shotInProgress && _firstHitBall <= 0)
                    {
                        if (a == CUE_BALL && b != CUE_BALL) _firstHitBall = b;
                        else if (b == CUE_BALL && a != CUE_BALL) _firstHitBall = a;
                    }
                    var distance = (float)Math.Sqrt(distSquared);
                    var normal = distance > 0f ? delta / distance : Vector2.UnitX;
                    var overlap = minDistance - distance;
                    var correction = normal * (overlap * 0.5f + 0.0001f);
                    _balls[a].Position -= correction;
                    _balls[b].Position += correction;
                    _balls[a].Position = ClampToTableOrPocketFunnel(_balls[a].Position, a);
                    _balls[b].Position = ClampToTableOrPocketFunnel(_balls[b].Position, b);
                    var relativeSpeed = Vector2.Dot(_balls[a].Velocity - _balls[b].Velocity, normal);
                    if (relativeSpeed <= 0f) continue;
                    var impulse = normal * relativeSpeed;
                    _balls[a].Velocity -= impulse;
                    _balls[b].Velocity += impulse;
                    _balls[a].Velocity *= 0.985f;
                    _balls[b].Velocity *= 0.985f;
                    _needsSaveWhenIdle = true;
                }
            }
        }

        void ResetMatchState(bool clearCapturedBalls)
        {
            if (clearCapturedBalls)
            {
                for (var i = 0; i < _capturedBy.Length; i++) _capturedBy[i] = 0;
                _captureOrder.Clear();
            }

            _lastPocketedBall = -1;
            _currentPlayer = PLAYER_ONE;
            _playerOneGroup = GROUP_OPEN;
            _winner = 0;
            _gameOver = false;
            _shotInProgress = false;
            _shotCueBallPocketed = false;
            _firstHitBall = -1;
            _lastPlayer = PLAYER_ONE;
            for (var i = 0; i < BALL_COUNT; i++) _shotStartPocketed[i] = _balls[i].Pocketed;
            _shotPocketedBalls.Clear();
            _lastStatusMessage = FormatLoc("LcdMod_EightBallPool_PlayerBreak", PLAYER_ONE);
        }

        void LoadCaptureOrder(int[] savedOrder)
        {
            _captureOrder.Clear();
            if (savedOrder != null)
            {
                foreach (var ball in savedOrder)
                {
                    if (ball > 0 && ball < BALL_COUNT && ball != 8 && _capturedBy[ball] != 0 && !_captureOrder.Contains(ball))
                        _captureOrder.Add(ball);
                }
            }

            for (var i = 1; i < BALL_COUNT; i++)
            {
                if (i == 8) continue;
                if (_capturedBy[i] != 0 && !_captureOrder.Contains(i))
                    _captureOrder.Add(i);
            }
        }

        void RegisterCapturedBall(int ball, int player)
        {
            if (ball <= 0 || ball >= BALL_COUNT || ball == 8) return;
            if (player != PLAYER_ONE && player != PLAYER_TWO) return;
            _capturedBy[ball] = player;
            if (!_captureOrder.Contains(ball)) _captureOrder.Add(ball);
        }

        void ReconcileCapturedBalls()
        {
            for (var i = 1; i < BALL_COUNT; i++)
            {
                if (i == 8) continue;

                if (!_balls[i].Pocketed)
                {
                    _capturedBy[i] = 0;
                    _captureOrder.Remove(i);
                    continue;
                }

                if (_capturedBy[i] == PLAYER_ONE || _capturedBy[i] == PLAYER_TWO) continue;

                if (_playerOneGroup == GROUP_OPEN) continue;

                if (GetBallGroup(i) == GetPlayerGroup(PLAYER_ONE)) RegisterCapturedBall(i, PLAYER_ONE);
                else if (GetBallGroup(i) == GetPlayerGroup(PLAYER_TWO)) RegisterCapturedBall(i, PLAYER_TWO);
            }
        }

        void BeginShotTracking()
        {
            _shotInProgress = true;
            _shotCueBallPocketed = false;
            _firstHitBall = -1;
            _lastPlayer = _currentPlayer;
            _shotPocketedBalls.Clear();
            for (var i = 0; i < BALL_COUNT; i++) _shotStartPocketed[i] = _balls[i].Pocketed;
        }

        void AddShotPocketedBall(int ball)
        {
            if (ball < 0 || ball >= BALL_COUNT) return;
            if (!_shotPocketedBalls.Contains(ball)) _shotPocketedBalls.Add(ball);
        }

        void CollectPocketedBallsSinceShotStart()
        {
            for (var i = 0; i < BALL_COUNT; i++)
            {
                if (!_shotStartPocketed[i] && _balls[i].Pocketed)
                    AddShotPocketedBall(i);
            }

            if (!_shotStartPocketed[CUE_BALL] && _balls[CUE_BALL].Pocketed)
                _shotCueBallPocketed = true;
        }

        void ResolveCompletedShot()
        {
            var shooter = _lastPlayer == PLAYER_TWO ? PLAYER_TWO : PLAYER_ONE;
            var opponent = OtherPlayer(shooter);
            var foul = false;
            var capturedOwnBall = false;
            var capturedEightBall = false;
            string foulReason;

            CollectPocketedBallsSinceShotStart();
            _shotInProgress = false;

            if (_shotCueBallPocketed || _balls[CUE_BALL].Pocketed)
            {
                foul = true;
                foulReason = Loc("LcdMod_EightBallPool_Scratch");
            }
            else foulReason = null;

            string firstHitReason;
            if (IsIllegalFirstHit(shooter, out firstHitReason))
            {
                foul = true;
                if (string.IsNullOrEmpty(foulReason)) foulReason = firstHitReason;
            }

            AssignGroupsFromFirstPocketedObjectBall(shooter);

            foreach (var ball in _shotPocketedBalls)
            {
                if (ball == CUE_BALL) continue;
                if (ball == 8)
                {
                    capturedEightBall = true;
                    continue;
                }

                var owner = GetPocketedObjectBallOwner(shooter, ball);
                if (owner == shooter) capturedOwnBall = true;
                else if (owner == opponent)
                {
                    foul = true;
                    if (string.IsNullOrEmpty(foulReason)) foulReason = FormatLoc("LcdMod_EightBallPool_PocketedOpponentBall", ball);
                }

                if (owner == PLAYER_ONE || owner == PLAYER_TWO) RegisterCapturedBall(ball, owner);
            }

            ReconcileCapturedBalls();

            if (capturedEightBall)
            {
                if (!foul && PlayerHasClearedGroup(shooter))
                    SetGameOver(shooter, FormatLoc("LcdMod_EightBallPool_WinEightBall", shooter));
                else
                    SetGameOver(opponent, FormatLoc("LcdMod_EightBallPool_WinEarlyEightBall", opponent, shooter));

                _needsSaveWhenIdle = true;
                _shotPocketedBalls.Clear();
                return;
            }

            if (foul)
            {
                _currentPlayer = opponent;
                if (_playerOneGroup == GROUP_OPEN)
                {
                    _lastStatusMessage = FormatLoc("LcdMod_EightBallPool_FoulOpen",
                        string.IsNullOrEmpty(foulReason) ? Loc("LcdMod_EightBallPool_IllegalShot") : foulReason,
                        _currentPlayer);
                }
                else
                {
                    var awarded = AwardLowestRemainingBall(opponent);
                    ReconcileCapturedBalls();
                    if (awarded <= 0)
                    {
                        SetGameOver(opponent, FormatLoc("LcdMod_EightBallPool_WinNoFreeBall", opponent));
                    }
                    else
                    {
                        _lastStatusMessage = FormatLoc("LcdMod_EightBallPool_FoulAward",
                            string.IsNullOrEmpty(foulReason) ? Loc("LcdMod_EightBallPool_IllegalShot") : foulReason,
                            BuildAwardText(awarded),
                            _currentPlayer);
                    }
                }
            }
            else if (capturedOwnBall)
            {
                _currentPlayer = shooter;
                _lastStatusMessage = FormatLoc("LcdMod_EightBallPool_PlayerShootsAgain", shooter);
            }
            else
            {
                _currentPlayer = opponent;
                _lastStatusMessage = FormatLoc("LcdMod_EightBallPool_NoBallPocketed", _currentPlayer);
            }

            _shotPocketedBalls.Clear();
            _needsSaveWhenIdle = true;
        }

        void AssignGroupsFromFirstPocketedObjectBall(int shooter)
        {
            if (_playerOneGroup != GROUP_OPEN) return;

            foreach (var ball in _shotPocketedBalls)
            {
                var group = GetBallGroup(ball);
                if (!IsObjectGroup(group)) continue;
                AssignGroups(shooter, group);
                return;
            }
        }

        int GetPocketedObjectBallOwner(int shooter, int ball)
        {
            var opponent = OtherPlayer(shooter);
            if (BallBelongsToPlayer(ball, shooter)) return shooter;
            if (BallBelongsToPlayer(ball, opponent)) return opponent;
            return shooter;
        }

        bool IsIllegalFirstHit(int shooter, out string reason)
        {
            reason = null;
            if (_firstHitBall <= 0)
            {
                reason = Loc("LcdMod_EightBallPool_NoObjectBallHit");
                return true;
            }

            var playerGroup = GetPlayerGroup(shooter);
            if (playerGroup == GROUP_OPEN)
            {
                if (_firstHitBall == 8 && RemainingObjectBalls() > 1)
                {
                    reason = Loc("LcdMod_EightBallPool_HitEightBallBeforeGroups");
                    return true;
                }
                return false;
            }

            if (PlayerHasClearedGroup(shooter))
            {
                if (_firstHitBall == 8) return false;
                reason = Loc("LcdMod_EightBallPool_HitOpponentBeforeEight");
                return true;
            }

            if (GetBallGroup(_firstHitBall) != playerGroup)
            {
                reason = Loc(_firstHitBall == 8
                    ? "LcdMod_EightBallPool_HitEightBallBeforeClearing"
                    : "LcdMod_EightBallPool_HitOpponentFirst");
                return true;
            }

            return false;
        }

        int AwardLowestRemainingBall(int player)
        {
            var group = GetPlayerGroup(player);

            if (group == GROUP_OPEN)
            {
                return -1;
            }

            var ball = FindLowestRemainingBall(group);

            if (ball <= 0) return -1;
            PocketBallForPlayer(ball, player);
            return ball;
        }

        int FindLowestRemainingBall(int group)
        {
            for (var i = 1; i < BALL_COUNT; i++)
            {
                if (i == 8 || _balls[i].Pocketed) continue;
                if (GetBallGroup(i) == group) return i;
            }
            return -1;
        }

        void PocketBallForPlayer(int ball, int player)
        {
            if (ball <= 0 || ball >= BALL_COUNT || ball == 8) return;
            _balls[ball].Pocketed = true;
            _balls[ball].Velocity = Vector2.Zero;
            ClearPocketShrinkAnimation(ball);
            RegisterCapturedBall(ball, player);
            _lastPocketedBall = ball;
            _needsSaveWhenIdle = true;
        }

        string BuildAwardText(int awardedBall)
        {
            if (awardedBall > 0) return FormatLoc("LcdMod_EightBallPool_AwardFreeBall", _currentPlayer, BallName(awardedBall));
            return Loc("LcdMod_EightBallPool_NoFreeBallAvailable");
        }

        void SetGameOver(int winner, string message)
        {
            _gameOver = true;
            _winner = winner;
            _currentPlayer = winner;
            _lastStatusMessage = message;
            ShowNewGameMessageBox();
        }

        void AssignGroups(int player, int group)
        {
            if (!IsObjectGroup(group) || _playerOneGroup != GROUP_OPEN) return;
            _playerOneGroup = player == PLAYER_ONE ? group : OppositeGroup(group);
            _lastStatusMessage = FormatLoc("LcdMod_EightBallPool_PlayerGroupsAssigned",
                GroupLabel(GetPlayerGroup(PLAYER_ONE)),
                GroupLabel(GetPlayerGroup(PLAYER_TWO)));
        }

        bool PlayerHasClearedGroup(int player)
        {
            var group = GetPlayerGroup(player);
            if (!IsObjectGroup(group)) return false;
            for (var i = 1; i < BALL_COUNT; i++)
            {
                if (i == 8 || _balls[i].Pocketed) continue;
                if (GetBallGroup(i) == group) return false;
            }
            return true;
        }

        bool BallBelongsToPlayer(int ball, int player)
        {
            var group = GetBallGroup(ball);
            return IsObjectGroup(group) && group == GetPlayerGroup(player);
        }

        int GetPlayerGroup(int player)
        {
            if (_playerOneGroup == GROUP_OPEN) return GROUP_OPEN;
            return player == PLAYER_ONE ? _playerOneGroup : OppositeGroup(_playerOneGroup);
        }

        static int OtherPlayer(int player)
        {
            return player == PLAYER_ONE ? PLAYER_TWO : PLAYER_ONE;
        }

        static int OppositeGroup(int group)
        {
            if (group == GROUP_SOLIDS) return GROUP_STRIPES;
            if (group == GROUP_STRIPES) return GROUP_SOLIDS;
            return GROUP_OPEN;
        }

        static int GetBallGroup(int ball)
        {
            if (ball >= 1 && ball <= 7) return GROUP_SOLIDS;
            if (ball >= 9 && ball <= 15) return GROUP_STRIPES;
            return GROUP_OPEN;
        }

        static bool IsObjectGroup(int group)
        {
            return group == GROUP_SOLIDS || group == GROUP_STRIPES;
        }

        static bool IsValidGroup(int group)
        {
            return group == GROUP_OPEN || group == GROUP_SOLIDS || group == GROUP_STRIPES;
        }

        string BuildTurnStatus()
        {
            if (_gameOver) return FormatLoc("LcdMod_EightBallPool_GameOverWinner", _winner);
            return FormatLoc("LcdMod_EightBallPool_PlayerToShoot", _currentPlayer);
        }

        bool AreBallsMoving()
        {
            return ArePhysicalBallsMoving() || ArePocketShrinkAnimationsRunning();
        }

        bool ArePhysicalBallsMoving()
        {
            for (var i = 0; i < BALL_COUNT; i++)
            {
                if (!_balls[i].Pocketed && _balls[i].Velocity.LengthSquared() >= MIN_SPEED * MIN_SPEED) return true;
            }
            return false;
        }

        void QueueSelfUpdateIfNeeded()
        {
            if (_selfUpdateQueued || !AreBallsMoving() || !CanSelfUpdate())
                return;

            _selfUpdateQueued = true;
            LcdModClientComponent.RunNextFrame.Add(RunQueuedSelfUpdate);
        }

        void RunQueuedSelfUpdate()
        {
            _selfUpdateQueued = false;

            try
            {
                if (!CanSelfUpdate() || !AreBallsMoving())
                    return;

                _script.Run();
            }
            catch (Exception e)
            {
                LogHelper.LogInfo(e.ToString());
            }
        }

        bool CanSelfUpdate()
        {
            try
            {
                if (_script == null || MyAPIGateway.Session == null)
                    return false;

                var block = _script.Block;
                if (block == null || block.MarkedForClose || block.Closed)
                    return false;

                var grid = block.CubeGrid;
                if (grid == null || grid.MarkedForClose || grid.Closed)
                    return false;

                var gameScript = _script as GameSurfaceScript;
                return gameScript == null || gameScript.IsCurrentGame(this);
            }
            catch
            {
                return false;
            }
        }

        void DrawTable(List<MySprite> sprites)
        {
            var border = Math.Max(1f, _railThickness * 0.22f);
            DrawBevelBorder(sprites, _tableOuterRect, border, _bevelLight, _bevelDark);
            sprites.Add(RectSprite(Inset(_tableOuterRect, border), _railColor));
            var playBorder = GetInnerBevelScreenThickness();
            sprites.Add(RectSprite(_playRect, _feltColor));
            DrawBevelBorder(sprites, _playRect, playBorder, GetInnerBevelTopLeftColor(), GetInnerBevelBottomRightColor());
            DrawPocketFunnels(sprites);
#if DEBUG
            if(LocalConfigManager.DebugInteractive)
                DrawPhysicsWallsDebug(sprites);
#endif
            DrawHeadString(sprites);
            DrawCueRespawnArea(sprites);
            DrawFootSpot(sprites);
            DrawRackTriangle(sprites);
            DrawPockets(sprites);
        }

        Color GetInnerBevelTopLeftColor()
        {
            // The inner bevel belongs to the felt/playfield, so derive it from
            // the felt color instead of using the fixed outer-board bevel colors.
            return _feltColor.MulValue(0.9f);
        }

        Color GetInnerBevelBottomRightColor()
        {
            // Match the existing inner-bevel orientation: top/left is darker,
            // bottom/right is lighter.
            return _feltColor.MulValue(1.1f);
        }

        float GetInnerBevelScreenThickness()
        {
            // Keep the funnel side strips the exact same thickness as the
            // inner playfield bevel rectangles drawn around _playRect.
            var outerBevelBorder = Math.Max(1f, _railThickness * 0.22f);
            return Math.Max(1f, outerBevelBorder * 0.82f);
        }

        Color GetInnerBevelColorForPocketMouthEndpoint(int pocketIndex, bool leftEndpoint)
        {
            Vector2 mouthLeft;
            Vector2 mouthRight;
            GetPocketMouthEndpoints(pocketIndex, out mouthLeft, out mouthRight);
            var endpoint = leftEndpoint ? mouthLeft : mouthRight;
            var outwardNormal = GetPocketTransitionOutwardNormal(pocketIndex, endpoint);

            // Top and left edges use the top-left inner bevel color; bottom and
            // right edges use the bottom-right inner bevel color. Corner-pocket
            // side fills can therefore inherit different colors on different sides.
            if (outwardNormal.X < -0.1f || outwardNormal.Y < -0.1f)
                return GetInnerBevelTopLeftColor();

            return GetInnerBevelBottomRightColor();
        }

        void DrawHeadString(List<MySprite> sprites)
        {
            var x = _playRect.X + _playRect.Width * CUE_SPAWN_X;
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, _playRect.Center.Y),
                Size = new Vector2(Math.Max(1f, _playRect.Width * 0.003f), _playRect.Height), Color = new Color(255, 255, 255, 42), Alignment = TextAlignment.CENTER });
        }

        void DrawCueRespawnArea(List<MySprite> sprites)
        {
            var center = ToScreen(new Vector2(CUE_SPAWN_X, CUE_SPAWN_Y));
            var radius = CUE_RESPAWN_RADIUS * _playRect.Width;
            var warning = GetWarningColor(58);
            var outline = new Color(255, 255, 255, 150);
            var thickness = Math.Max(1.5f, _ballScreenRadius * 0.12f);

            if (!_gameOver && IsCueBallRespawning())
            {
                var clip = ToClipRect(new RectangleF(center.X - radius, center.Y - radius, radius, radius * 2f));
                if (clip.Width > 0 && clip.Height > 0)
                {
                    sprites.Add(MySprite.CreateClipRect(clip));
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(radius * 2f),
                        Color = warning, Alignment = TextAlignment.CENTER });
                    sprites.Add(MySprite.CreateClearClipRect());
                }
            }

            DrawArc(sprites, center, radius, (float)(Math.PI * 0.5), (float)(Math.PI * 1.5), 24, thickness, outline);
            DrawLine(sprites, center + new Vector2(0f, -radius), center + new Vector2(0f, radius), thickness, outline);
        }

        void DrawFootSpot(List<MySprite> sprites)
        {
            var center = ToScreen(new Vector2(0.75f, TABLE_HEIGHT * 0.5f));
            var size = Math.Max(3f, _ballScreenRadius * 0.35f);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(size),
                Color = new Color(255, 255, 255, 92), Alignment = TextAlignment.CENTER });
        }

        void DrawRackTriangle(List<MySprite> sprites)
        {
            var gap = BALL_RADIUS * 2.08f;
            var centerY = TABLE_HEIGHT * 0.5f;
            var rackOffsetX = -BALL_RADIUS * 0.5f;
            var apexX = 0.70f - BALL_RADIUS * 1.55f + rackOffsetX;
            var baseX = 0.70f + 4f * gap * 0.88f + BALL_RADIUS * 1.55f + rackOffsetX;
            var halfHeight = 2f * gap + BALL_RADIUS * 1.35f;
            var apex = ToScreen(new Vector2(apexX, centerY));
            var top = ToScreen(new Vector2(baseX, centerY - halfHeight));
            var bottom = ToScreen(new Vector2(baseX, centerY + halfHeight));
            var thickness = Math.Max(1.5f, _ballScreenRadius * 0.15f);
            var color = new Color(255, 255, 255, 115);

            DrawLine(sprites, apex, top, thickness, color);
            DrawLine(sprites, top, bottom, thickness, color);
            DrawLine(sprites, bottom, apex, thickness, color);
        }

        void DrawPockets(List<MySprite> sprites)
        {
            var pocketDiameter = GetPocketScreenRadius() * 2f;
            for (var i = 0; i < _pockets.Length; i++)
            {
                var center = GetVisualPocketCenter(i);
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(pocketDiameter),
                    Color = _pocketColor, Alignment = TextAlignment.CENTER });
            }
        }

        void DrawPocketFunnels(List<MySprite> sprites)
        {
            for (var i = 0; i < _pockets.Length; i++)
                DrawPocketFunnel(sprites, i);
        }


#if DEBUG
        void DrawPhysicsWallsDebug(List<MySprite> sprites)
        {
            var debugColor = new Color(255, 0, 255, 210);
            var debugZoneColor = new Color(255, 0, 255, 95);
            var wallThickness = Math.Max(2f, _ballScreenRadius * 0.16f);
            var transitionRadius = Math.Max(1.5f, POCKET_TRANSITION_ZONE_RADIUS * _playRect.Width);

            DrawStraightRailWallsDebug(sprites, wallThickness, debugColor);

            for (var i = 0; i < _pockets.Length; i++)
            {
                Vector2 narrowLeft;
                Vector2 narrowRight;
                Vector2 mouthLeft;
                Vector2 mouthRight;
                Vector2 centerAtMiddle;
                GetPocketFunnelWallGeometry(i, out narrowLeft, out narrowRight, out mouthLeft, out mouthRight, out centerAtMiddle);

                DrawLine(sprites, ToScreen(narrowLeft), ToScreen(mouthLeft), wallThickness, debugColor);
                DrawLine(sprites, ToScreen(narrowRight), ToScreen(mouthRight), wallThickness, debugColor);

                Vector2 transitionLeft;
                Vector2 transitionRight;
                GetPocketTransitionZoneCenters(i, out transitionLeft, out transitionRight);
                DrawTransitionZoneDebug(sprites, ToScreen(transitionLeft), transitionRadius, debugZoneColor, debugColor);
                DrawTransitionZoneDebug(sprites, ToScreen(transitionRight), transitionRadius, debugZoneColor, debugColor);
            }
        }

        void DrawStraightRailWallsDebug(List<MySprite> sprites, float wallThickness, Color debugColor)
        {
            Vector2 a;
            Vector2 b;

            GetPocketMouthEndpoints(0, out a, out b);
            var topLeftStart = Math.Max(a.X, b.X);
            GetPocketMouthEndpoints(1, out a, out b);
            var topMiddleLeft = Math.Min(a.X, b.X);
            var topMiddleRight = Math.Max(a.X, b.X);
            GetPocketMouthEndpoints(2, out a, out b);
            var topRightEnd = Math.Min(a.X, b.X);

            DrawLine(sprites, ToScreen(new Vector2(topLeftStart, 0f)), ToScreen(new Vector2(topMiddleLeft, 0f)), wallThickness, debugColor);
            DrawLine(sprites, ToScreen(new Vector2(topMiddleRight, 0f)), ToScreen(new Vector2(topRightEnd, 0f)), wallThickness, debugColor);

            GetPocketMouthEndpoints(3, out a, out b);
            var bottomLeftStart = Math.Max(a.X, b.X);
            GetPocketMouthEndpoints(4, out a, out b);
            var bottomMiddleLeft = Math.Min(a.X, b.X);
            var bottomMiddleRight = Math.Max(a.X, b.X);
            GetPocketMouthEndpoints(5, out a, out b);
            var bottomRightEnd = Math.Min(a.X, b.X);

            DrawLine(sprites, ToScreen(new Vector2(bottomLeftStart, TABLE_HEIGHT)), ToScreen(new Vector2(bottomMiddleLeft, TABLE_HEIGHT)), wallThickness, debugColor);
            DrawLine(sprites, ToScreen(new Vector2(bottomMiddleRight, TABLE_HEIGHT)), ToScreen(new Vector2(bottomRightEnd, TABLE_HEIGHT)), wallThickness, debugColor);

            GetPocketMouthEndpoints(0, out a, out b);
            var leftTopEnd = Math.Max(a.Y, b.Y);
            GetPocketMouthEndpoints(3, out a, out b);
            var leftBottomStart = Math.Min(a.Y, b.Y);
            DrawLine(sprites, ToScreen(new Vector2(0f, leftTopEnd)), ToScreen(new Vector2(0f, leftBottomStart)), wallThickness, debugColor);

            GetPocketMouthEndpoints(2, out a, out b);
            var rightTopEnd = Math.Max(a.Y, b.Y);
            GetPocketMouthEndpoints(5, out a, out b);
            var rightBottomStart = Math.Min(a.Y, b.Y);
            DrawLine(sprites, ToScreen(new Vector2(1f, rightTopEnd)), ToScreen(new Vector2(1f, rightBottomStart)), wallThickness, debugColor);
        }

        void DrawTransitionZoneDebug(List<MySprite> sprites, Vector2 center, float radius, Color fillColor, Color outlineColor)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(radius * 2f),
                Color = fillColor, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "CircleHollow", Position = center, Size = new Vector2(radius * 2f),
                Color = outlineColor, Alignment = TextAlignment.CENTER });
        }
#endif

        float GetPocketScreenRadius()
        {
            return POCKET_RADIUS * _playRect.Width;
        }

        float GetBallScreenRadius(int number)
        {
            return GetBallPhysicsRadius(number) * _playRect.Width;
        }

        Vector2 GetVisualPocketCenter(int pocketIndex)
        {
            return ToScreen(GetPhysicsPocketCenter(pocketIndex));
        }

        void DrawPocketFunnel(List<MySprite> sprites, int pocketIndex)
        {
            var narrowCenter = GetVisualPocketCenter(pocketIndex);
            var mouthCenter = ToScreen(GetPocketFunnelMouthCenter(pocketIndex));
            var direction = mouthCenter - narrowCenter;
            if (direction.LengthSquared() <= 0.0001f) return;
            direction.Normalize();

            var perpendicular = new Vector2(-direction.Y, direction.X);
            var narrowHalf = Math.Max(1f, GetPocketScreenRadius());
            var wideHalf = Math.Max(narrowHalf, GetPocketFunnelMouthHalfWidth(pocketIndex) * _playRect.Width);

            var narrowLeft = narrowCenter + perpendicular * narrowHalf;
            var narrowRight = narrowCenter - perpendicular * narrowHalf;
            var mouthLeft = mouthCenter + perpendicular * wideHalf;
            var mouthRight = mouthCenter - perpendicular * wideHalf;

            var clip = ToClipRect(GetFunnelDrawClipRect(narrowLeft, narrowRight, mouthLeft, mouthRight, Math.Max(narrowHalf, wideHalf) + 2f));
            if (clip.Width <= 0 || clip.Height <= 0) return;

            sprites.Add(MySprite.CreateClipRect(clip));

            var funnelFillColor = _feltColor;

            var centerFillWidth = Math.Max(2f, narrowHalf * 2f);
            DrawLine(sprites, narrowCenter, mouthCenter, centerFillWidth, funnelFillColor);

            var sideOpening = Math.Max(0f, wideHalf - narrowHalf);
            if (sideOpening > 0.5f)
            {
                var sideFillWidth = GetInnerBevelScreenThickness();
                var sideInset = sideFillWidth * 0.5f;

                var leftEdgeStart = narrowCenter + perpendicular * narrowHalf;
                var leftEdgeEnd = mouthCenter + perpendicular * wideHalf;
                var rightEdgeStart = narrowCenter - perpendicular * narrowHalf;
                var rightEdgeEnd = mouthCenter - perpendicular * wideHalf;

                var leftSideStart = leftEdgeStart - perpendicular * sideInset;
                var leftSideEnd = leftEdgeEnd - perpendicular * sideInset;
                var rightSideStart = rightEdgeStart + perpendicular * sideInset;
                var rightSideEnd = rightEdgeEnd + perpendicular * sideInset;

                var leftSideColor = GetInnerBevelColorForPocketMouthEndpoint(pocketIndex, true);
                var rightSideColor = GetInnerBevelColorForPocketMouthEndpoint(pocketIndex, false);

                DrawLine(sprites, leftSideStart, leftSideEnd, sideFillWidth, leftSideColor);
                DrawLine(sprites, rightSideStart, rightSideEnd, sideFillWidth, rightSideColor);
            }

#if DEBUG
            var debugFunnelColor = new Color(255, 0, 255, 220);
            var wallThickness = Math.Max(2f, _ballScreenRadius * 0.18f);
            DrawLine(sprites, narrowLeft, mouthLeft, wallThickness, debugFunnelColor);
            DrawLine(sprites, narrowRight, mouthRight, wallThickness, debugFunnelColor);
#endif

            sprites.Add(MySprite.CreateClearClipRect());
        }

        RectangleF GetFunnelDrawClipRect(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float padding)
        {
            var left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) - padding;
            var top = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) - padding;
            var right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + padding;
            var bottom = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + padding;

            left = Math.Max(left, _tableOuterRect.X);
            top = Math.Max(top, _tableOuterRect.Y);
            right = Math.Min(right, _tableOuterRect.Right);
            bottom = Math.Min(bottom, _tableOuterRect.Bottom);

            return new RectangleF(left, top, Math.Max(0f, right - left), Math.Max(0f, bottom - top));
        }

        void DrawAimGuide(List<MySprite> sprites)
        {
            if (_gameOver || AreBallsMoving() || _cueBallInHand || _balls[CUE_BALL].Pocketed) return;
            Vector2 target;
            if (!TryGetCursorTablePoint(out target)) return;
            var cue = _balls[CUE_BALL];
            var delta = target - cue.Position;
            var length = delta.Length();
            if (length < GetBallPhysicsRadius(CUE_BALL) * 1.5f) return;
            var direction = delta / length;
            var power = MathHelper.Clamp(length / 0.46f, 0f, 1f);
            var clip = ToClipRect(_viewBox);
            if (clip.Width > 0 && clip.Height > 0) sprites.Add(MySprite.CreateClipRect(clip));
            DrawCueStick(sprites, cue.Position, direction, power);
            var cueRadius = GetBallPhysicsRadius(CUE_BALL);
            var start = ToScreen(cue.Position + direction * cueRadius * 1.4f);
            var end = ToScreen(cue.Position + direction * Math.Min(length, 0.52f));
            var playerColor = GetCurrentPlayerCueTipColor();
            var lineColor = new Color(playerColor.R, playerColor.G, playerColor.B, 190);
            var ghostColor = new Color(playerColor.R, playerColor.G, playerColor.B, 82);
            DrawLine(sprites, start, end, Math.Max(1.5f, _ballScreenRadius * 0.08f), lineColor);
            var ghost = cue.Position + direction * Math.Min(length, 0.52f);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = ToScreen(ghost), Size = new Vector2(GetBallScreenRadius(CUE_BALL) * 2f),
                Color = ghostColor, Alignment = TextAlignment.CENTER });
            if (clip.Width > 0 && clip.Height > 0) sprites.Add(MySprite.CreateClearClipRect());
        }

        void DrawCueStick(List<MySprite> sprites, Vector2 cueBallPosition, Vector2 aimDirection, float power)
        {
            var reverseDirection = -aimDirection;
            var cueRadius = GetBallPhysicsRadius(CUE_BALL);
            var baseGap = cueRadius * 1.18f;
            var pullBack = MathHelper.Lerp(cueRadius * 0.25f, cueRadius * 5.8f, power);
            var tipLength = cueRadius * 0.32f;
            var ferruleLength = cueRadius * 0.48f;
            var shaftLength = cueRadius * 8.2f;
            var buttLength = cueRadius * 3.8f;

            var tipStart = cueBallPosition + reverseDirection * (baseGap + pullBack);
            var tipEnd = tipStart + reverseDirection * tipLength;
            var ferruleEnd = tipEnd + reverseDirection * ferruleLength;
            var shaftEnd = ferruleEnd + reverseDirection * shaftLength;
            var buttEnd = shaftEnd + reverseDirection * buttLength;

            var tipThickness = Math.Max(2f, _ballScreenRadius * 0.12f);
            var ferruleThickness = Math.Max(2.5f, _ballScreenRadius * 0.18f);
            var shaftThickness = Math.Max(3.5f, _ballScreenRadius * 0.26f);
            var buttThickness = Math.Max(4.5f, _ballScreenRadius * 0.34f);

            var tipColor = GetCurrentPlayerCueTipColor();

            DrawLine(sprites, ToScreen(tipStart), ToScreen(tipEnd), tipThickness, tipColor);
            DrawLine(sprites, ToScreen(tipEnd), ToScreen(ferruleEnd), ferruleThickness, _cueFerruleColor);
            DrawLine(sprites, ToScreen(ferruleEnd), ToScreen(shaftEnd), shaftThickness, _cueWoodColor);
            DrawLine(sprites, ToScreen(shaftEnd), ToScreen(buttEnd), buttThickness, _cueGripColor);

            var wrapStart = Vector2.Lerp(shaftEnd, buttEnd, 0.2f);
            var wrapEnd = Vector2.Lerp(shaftEnd, buttEnd, 0.85f);
            DrawLine(sprites, ToScreen(wrapStart), ToScreen(wrapEnd), Math.Max(2f, _ballScreenRadius * 0.12f), _cueWoodAccentColor);
        }

        void DrawBalls(List<MySprite> sprites)
        {
            for (var i = BALL_COUNT - 1; i >= 0; i--)
            {
                if (!_balls[i].Pocketed) DrawBall(sprites, _balls[i]);
                else if (_pocketShrinkScales[i] > 0f) DrawPocketShrinkBall(sprites, i);
            }
        }

        void DrawPocketShrinkBall(List<MySprite> sprites, int ballIndex)
        {
            var ball = _balls[ballIndex];
            ball.Position = _pocketShrinkPositions[ballIndex];
            DrawBall(sprites, ball, MathHelper.Clamp(_pocketShrinkScales[ballIndex], 0f, 1f));
        }

        void DrawBall(List<MySprite> sprites, Ball ball) => DrawBall(sprites, ball, 1f);

        void DrawBall(List<MySprite> sprites, Ball ball, float scale)
        {
            var center = ToScreen(ball.Position);
            var number = ball.Number;
            var radius = GetBallScreenRadius(number) * MathHelper.Clamp(scale, 0f, 1f);
            if (radius <= 0.25f) return;
            if (number == CUE_BALL)
            {
                DrawCircle(sprites, center, radius, new Color(245, 245, 235));
                DrawBallHighlight(sprites, center, radius);
                DrawBallOutline(sprites, center, radius);
                return;
            }
            var stripe = number >= 9;
            var color = _ballColors[Math.Min(number, _ballColors.Length - 1)];
            if (stripe)
            {
                DrawCircle(sprites, center, radius, new Color(245, 245, 235));
                DrawEquatorStripe(sprites, center, radius, color);
            }
            else DrawCircle(sprites, center, radius, color);
            DrawBallHighlight(sprites, center, radius);
            DrawNumberBadge(sprites, center, radius, number, number == 8 ? Color.White : Color.Black);
            DrawBallOutline(sprites, center, radius);
        }

        void DrawNumberBadge(List<MySprite> sprites, Vector2 center, float radius, int number, Color numberColor)
        {
            var badgeDiameter = radius * 0.92f;
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(badgeDiameter),
                Color = number == 8 ? new Color(15, 15, 15) : new Color(250, 250, 245), Alignment = TextAlignment.CENTER });
            var label = number.ToString();
            var scale = MathHelper.Clamp(radius * (number >= 10 ? 0.032f : 0.042f), 0.18f, 0.48f);
            var size = _panel.MeasureStringInPixels(new StringBuilder(label), "White", scale);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = label, Position = new Vector2(center.X, center.Y - size.Y * 0.5f),
                Color = numberColor, FontId = "White", Alignment = TextAlignment.CENTER, RotationOrScale = scale });
        }

        void DrawBallHighlight(List<MySprite> sprites, Vector2 center, float radius)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center + new Vector2(-radius * 0.32f, -radius * 0.34f),
                Size = new Vector2(radius * 0.52f), Color = new Color(255, 255, 255, 70), Alignment = TextAlignment.CENTER });
        }

        void DrawBallOutline(List<MySprite> sprites, Vector2 center, float radius)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "CircleHollow", Position = center, Size = new Vector2(radius * 2.08f),
                Color = new Color(0, 0, 0, 120), Alignment = TextAlignment.CENTER });
        }

        void DrawEquatorStripe(List<MySprite> sprites, Vector2 center, float radius, Color stripeColor)
        {
            var diameter = radius * 2f;
            var stripeHeight = diameter * STRIPE_BAND_RATIO;
            var halfStripe = stripeHeight * 0.5f;
            var boardArea = GetBoardClipArea();
            var bandTop = Math.Max((int)Math.Floor(boardArea.Y), (int)Math.Floor(center.Y - halfStripe));
            var bandBottom = Math.Min((int)Math.Ceiling(boardArea.Bottom), (int)Math.Ceiling(center.Y + halfStripe));
            var bandLeft = Math.Max((int)Math.Floor(boardArea.X), (int)Math.Floor(center.X - radius));
            var bandRight = Math.Min((int)Math.Ceiling(boardArea.Right), (int)Math.Ceiling(center.X + radius));
            if (bandBottom <= bandTop || bandRight <= bandLeft) return;
            var bandClip = new Rectangle(bandLeft, bandTop, bandRight - bandLeft, bandBottom - bandTop);
            sprites.Add(MySprite.CreateClipRect(bandClip));
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(diameter),
                Color = stripeColor, Alignment = TextAlignment.CENTER });
            sprites.Add(MySprite.CreateClearClipRect());
        }

        void DrawHud(List<MySprite> sprites)
        {
            var headerY = _headerRect.Y + 2f;
            DrawTextWithShadow(sprites, GetStatusText(), new Vector2(_headerRect.X, headerY), TextAlignment.LEFT, _fontScale * 0.86f, _textColor);
            DrawTextWithShadow(sprites, FormatLoc("LcdMod_EightBallPool_Shots", _shotCount), new Vector2(_headerRect.Right, headerY), TextAlignment.RIGHT, _fontScale, _textColor);
            DrawPowerMeter(sprites);
        }

        string GetStatusText()
        {
            if (_gameOver) return FormatLoc("LcdMod_EightBallPool_GameOverNewGameHint", _lastStatusMessage);
            if (AreBallsMoving()) return Loc("LcdMod_EightBallPool_BallsRolling");
            if (_cueBallInHand || _balls[CUE_BALL].Pocketed) return FormatLoc("LcdMod_EightBallPool_CueBallInHand", _currentPlayer);
            if (RemainingObjectBalls() == 0) return Loc("LcdMod_EightBallPool_TableClearedNewRack");
            if (!string.IsNullOrEmpty(_lastStatusMessage)) return _lastStatusMessage;
            return BuildTurnStatus();
        }

        int RemainingObjectBalls()
        {
            var remaining = 0;
            for (var i = 1; i < BALL_COUNT; i++)
            {
                if (!_balls[i].Pocketed) remaining++;
            }
            return remaining;
        }

        string BallName(int number)
        {
            if (number == 8) return Loc("LcdMod_EightBallPool_BallNameEight");
            if (number > 8) return FormatLoc("LcdMod_EightBallPool_BallNameStripe", number);
            if (number > 0) return FormatLoc("LcdMod_EightBallPool_BallNameSolid", number);
            return FormatLoc("LcdMod_EightBallPool_BallNameGeneric", number);
        }

        void DrawFooter(List<MySprite> sprites)
        {
            if (_footerRect.Width <= 0f || _footerRect.Height <= 0f) return;
            ReconcileCapturedBalls();

            if (_widePlayerLayout)
            {
                var halfHeight = _footerRect.Height * 0.5f;
                var playerOneRect = new RectangleF(_footerRect.X, _footerRect.Y, _footerRect.Width, halfHeight);
                var playerTwoRect = new RectangleF(_footerRect.X, _footerRect.Y + halfHeight, _footerRect.Width, halfHeight);
                DrawPlayerFooterPanel(sprites, PLAYER_ONE, playerOneRect, true, true);
                DrawPlayerFooterPanel(sprites, PLAYER_TWO, playerTwoRect, true, true);
                sprites.Add(RectSprite(new RectangleF(_footerRect.X + 4f, _footerRect.Center.Y - 0.5f, Math.Max(1f, _footerRect.Width - 8f), 1f), new Color(255, 255, 255, 70)));
            }
            else
            {
                var halfWidth = _footerRect.Width * 0.5f;
                var playerOneRect = new RectangleF(_footerRect.X, _footerRect.Y, halfWidth, _footerRect.Height);
                var playerTwoRect = new RectangleF(_footerRect.X + halfWidth, _footerRect.Y, halfWidth, _footerRect.Height);
                DrawPlayerFooterPanel(sprites, PLAYER_ONE, playerOneRect, true, false);
                DrawPlayerFooterPanel(sprites, PLAYER_TWO, playerTwoRect, false, false);
                sprites.Add(RectSprite(new RectangleF(_footerRect.Center.X - 0.5f, _footerRect.Y + 3f, 1f, Math.Max(1f, _footerRect.Height - 6f)), new Color(255, 255, 255, 70)));
            }
        }

        void DrawPlayerFooterPanel(List<MySprite> sprites, int player, RectangleF panelRect, bool leftSide, bool verticalLayout)
        {
            var playerColor = GetPlayerColor(player);
            var active = !_gameOver && _currentPlayer == player;
            var winner = _gameOver && _winner == player;
            if (active || winner)
                sprites.Add(RectSprite(Inset(panelRect, 1.5f), new Color(playerColor.R, playerColor.G, playerColor.B, winner ? (byte)92 : (byte)54)));

            var label = FormatLoc("LcdMod_EightBallPool_FooterPlayerLabel", player, GroupLabel(GetPlayerGroup(player)));
            if (active) label = "> " + label;
            if (winner) label += "  " + Loc("LcdMod_EightBallPool_Winner");

            var labelScale = MathHelper.Clamp(_fontScale * (verticalLayout ? 0.58f : 0.62f), 0.25f, 0.56f);
            var labelSize = _panel.MeasureStringInPixels(new StringBuilder(label), "White", labelScale);
            var labelY = panelRect.Y + Math.Max(2f, panelRect.Height * (verticalLayout ? 0.08f : 0.12f));
            var labelPos = leftSide
                ? new Vector2(panelRect.X + 6f, labelY)
                : new Vector2(panelRect.Right - 6f, labelY);
            DrawTextWithShadow(sprites, label, labelPos, leftSide ? TextAlignment.LEFT : TextAlignment.RIGHT, labelScale, playerColor);

            if (verticalLayout) DrawCapturedBallsGrid(sprites, player, panelRect, labelSize.Y, labelScale);
            else DrawCapturedBallsRow(sprites, player, panelRect, leftSide, labelScale);
        }

        void DrawCapturedBallsRow(List<MySprite> sprites, int player, RectangleF panelRect, bool leftSide, float labelScale)
        {
            var capturedCount = CountCapturedBallsForPlayer(player);
            var horizontalPadding = Math.Max(8f, panelRect.Width * 0.035f);
            var availableWidth = Math.Max(1f, panelRect.Width - horizontalPadding * 2f);
            var desiredRadius = MathHelper.Clamp(panelRect.Height * 0.36f, 7f, _ballScreenRadius * 1.04f);
            var iconRadius = desiredRadius;
            var spacingFactor = 2.55f;

            if (capturedCount > 1)
            {
                var maxRadiusByWidth = availableWidth / (2f + (capturedCount - 1) * spacingFactor);
                iconRadius = Math.Min(iconRadius, Math.Max(4f, maxRadiusByWidth));
            }
            else if (capturedCount == 1)
            {
                iconRadius = Math.Min(iconRadius, availableWidth * 0.5f);
            }

            var iconSpacing = iconRadius * spacingFactor;
            var totalWidth = capturedCount <= 0 ? 0f : iconRadius * 2f + (capturedCount - 1) * iconSpacing;
            var y = panelRect.Bottom - Math.Max(iconRadius + 4f, panelRect.Height * 0.28f);
            var x = leftSide
                ? panelRect.X + horizontalPadding + iconRadius
                : panelRect.Right - horizontalPadding - totalWidth + iconRadius;

            var any = false;
            foreach (var ball in _captureOrder)
            {
                if (ball <= 0 || ball >= BALL_COUNT || _capturedBy[ball] != player) continue;
                DrawBallIcon(sprites, ball, new Vector2(x, y), iconRadius);
                x += iconSpacing;
                any = true;
            }

            if (!any) DrawNoBallsText(sprites, panelRect, leftSide, y, labelScale, horizontalPadding);
        }

        void DrawCapturedBallsGrid(List<MySprite> sprites, int player, RectangleF panelRect, float labelHeight, float labelScale)
        {
            var capturedCount = CountCapturedBallsForPlayer(player);
            var padding = Math.Max(6f, panelRect.Width * 0.05f);
            var top = panelRect.Y + Math.Max(16f, labelHeight + 9f);
            var availableWidth = Math.Max(1f, panelRect.Width - padding * 2f);
            var availableHeight = Math.Max(1f, panelRect.Bottom - padding - top);
            var desiredRadius = MathHelper.Clamp(Math.Min(panelRect.Width * 0.105f, availableHeight * 0.22f), 5f, _ballScreenRadius * 1.02f);
            var iconRadius = desiredRadius;
            var spacingFactor = 2.55f;
            var columns = Math.Max(1, (int)Math.Floor((availableWidth + iconRadius * 0.55f) / (iconRadius * spacingFactor)));
            if (capturedCount > 0) columns = Math.Max(1, Math.Min(columns, capturedCount));
            var rows = capturedCount <= 0 ? 0 : (capturedCount + columns - 1) / columns;
            if (rows > 1)
            {
                var maxRadiusByHeight = availableHeight / (2f + (rows - 1) * spacingFactor);
                iconRadius = Math.Min(iconRadius, Math.Max(4f, maxRadiusByHeight));
                columns = Math.Max(1, (int)Math.Floor((availableWidth + iconRadius * 0.55f) / (iconRadius * spacingFactor)));
                if (capturedCount > 0) columns = Math.Max(1, Math.Min(columns, capturedCount));
            }

            if (capturedCount <= 0)
            {
                DrawNoBallsText(sprites, panelRect, true, top + availableHeight * 0.45f, labelScale, padding);
                return;
            }

            var iconSpacing = iconRadius * spacingFactor;
            var drawn = 0;
            foreach (var ball in _captureOrder)
            {
                if (ball <= 0 || ball >= BALL_COUNT || _capturedBy[ball] != player) continue;
                var col = drawn % columns;
                var row = drawn / columns;
                var x = panelRect.X + padding + iconRadius + col * iconSpacing;
                var y = top + iconRadius + iconRadius + row * iconSpacing;
                DrawBallIcon(sprites, ball, new Vector2(x, y), iconRadius);
                drawn++;
            }
        }

        void DrawNoBallsText(List<MySprite> sprites, RectangleF panelRect, bool leftSide, float y, float labelScale, float horizontalPadding)
        {
            var empty = Loc("LcdMod_EightBallPool_NoBalls");
            var emptySize = _panel.MeasureStringInPixels(new StringBuilder(empty), "White", labelScale * 0.86f);
            var emptyPos = leftSide
                ? new Vector2(panelRect.X + horizontalPadding, y - emptySize.Y * 0.5f)
                : new Vector2(panelRect.Right - horizontalPadding, y - emptySize.Y * 0.5f);
            DrawTextWithShadow(sprites, empty, emptyPos, leftSide ? TextAlignment.LEFT : TextAlignment.RIGHT, labelScale * 0.86f, new Color(236, 240, 241, 150));
        }

        int CountCapturedBallsForPlayer(int player)
        {
            var count = 0;
            foreach (var ball in _captureOrder)
            {
                if (ball > 0 && ball < BALL_COUNT && _capturedBy[ball] == player) count++;
            }
            return count;
        }

        void DrawBallIcon(List<MySprite> sprites, int number, Vector2 center, float radius)
        {
            if (number == 8)
            {
                DrawCircle(sprites, center, radius, new Color(15, 15, 15));
                DrawBallIconNumberBadge(sprites, number, center, radius, Color.White);
                return;
            }

            var stripe = number >= 9;
            var color = _ballColors[Math.Min(number, _ballColors.Length - 1)];
            if (stripe)
            {
                DrawCircle(sprites, center, radius, new Color(245, 245, 235));
                DrawIconStripe(sprites, center, radius, color);
            }
            else DrawCircle(sprites, center, radius, color);

            DrawBallIconNumberBadge(sprites, number, center, radius, Color.Black);
        }

        void DrawIconStripe(List<MySprite> sprites, Vector2 center, float radius, Color stripeColor)
        {
            var diameter = radius * 2f;
            var stripeHeight = diameter * STRIPE_BAND_RATIO;
            var bandTop = (int)Math.Floor(center.Y - stripeHeight * 0.5f);
            var bandLeft = (int)Math.Floor(center.X - radius);
            var bandClip = new Rectangle(bandLeft, bandTop, Math.Max(1, (int)Math.Ceiling(diameter)), Math.Max(1, (int)Math.Ceiling(stripeHeight)));
            sprites.Add(MySprite.CreateClipRect(bandClip));
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(diameter),
                Color = stripeColor, Alignment = TextAlignment.CENTER });
            sprites.Add(MySprite.CreateClearClipRect());
        }

        void DrawBallIconNumberBadge(List<MySprite> sprites, int number, Vector2 center, float radius, Color color)
        {
            var badgeDiameter = radius * 0.9f;
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(badgeDiameter),
                Color = number == 8 ? new Color(15, 15, 15) : new Color(250, 250, 245), Alignment = TextAlignment.CENTER });
            var label = number.ToString();
            var scale = MathHelper.Clamp(radius * (number >= 10 ? 0.042f : 0.052f), 0.13f, 0.28f);
            var size = _panel.MeasureStringInPixels(new StringBuilder(label), "White", scale);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = label, Position = new Vector2(center.X, center.Y - size.Y * 0.5f),
                Color = color, FontId = "White", Alignment = TextAlignment.CENTER, RotationOrScale = scale });
        }

        string GroupLabel(int group)
        {
            if (group == GROUP_SOLIDS) return Loc("LcdMod_EightBallPool_GroupSolids");
            if (group == GROUP_STRIPES) return Loc("LcdMod_EightBallPool_GroupStripes");
            return Loc("LcdMod_EightBallPool_GroupOpen");
        }

        Color GetPlayerColor(int player)
        {
            var colorConfig = _script?.ColorableConfig;
            if (player == PLAYER_ONE)
                return colorConfig?.HeaderColor ?? new Color(86, 151, 255);

            return colorConfig?.ErrorColor ?? new Color(160, 48, 48);
        }

        Color GetWarningColor(byte alpha)
        {
            var colorConfig = _script?.ColorableConfig;
            var warning = colorConfig?.WarningColor ?? new Color(224, 160, 16);
            return new Color(warning.R, warning.G, warning.B, alpha);
        }

        void DrawPowerMeter(List<MySprite> sprites)
        {
            if (_powerMeterRect.Width <= 0f || _gameOver || AreBallsMoving() || _cueBallInHand || _balls[CUE_BALL].Pocketed) return;
            Vector2 target;
            if (!TryGetCursorTablePoint(out target)) return;
            var power = MathHelper.Clamp((target - _balls[CUE_BALL].Position).Length() / 0.46f, 0f, 1f);
            sprites.Add(RectSprite(_powerMeterRect, new Color(0, 0, 0, 120)));
            sprites.Add(RectSprite(new RectangleF(_powerMeterRect.X, _powerMeterRect.Y, _powerMeterRect.Width * power, _powerMeterRect.Height), new Color(236, 240, 241, 190)));
        }

        Color GetCurrentPlayerCueTipColor()
        {
            return GetPlayerColor(_currentPlayer);
        }

        void DrawTextWithShadow(List<MySprite> sprites, string text, Vector2 position, TextAlignment alignment, float scale, Color color)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text, Position = position + new Vector2(1.5f, 1.5f), Color = _shadowColor,
                FontId = "White", Alignment = alignment, RotationOrScale = scale });
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text, Position = position, Color = color,
                FontId = "White", Alignment = alignment, RotationOrScale = scale });
        }

        Vector2 ToScreen(Vector2 tablePoint)
        {
            return new Vector2(_playRect.X + tablePoint.X * _playRect.Width, _playRect.Y + tablePoint.Y / TABLE_HEIGHT * _playRect.Height);
        }

        void DrawCircle(List<MySprite> sprites, Vector2 center, float radius, Color color)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = center, Size = new Vector2(radius * 2f), Color = color, Alignment = TextAlignment.CENTER });
        }

        void DrawLine(List<MySprite> sprites, Vector2 start, Vector2 end, float thickness, Color color)
        {
            var delta = end - start;
            var length = delta.Length();
            if (length <= 0f) return;
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = (start + end) * 0.5f,
                Size = new Vector2(length, thickness), Color = color, Alignment = TextAlignment.CENTER, RotationOrScale = (float)Math.Atan2(delta.Y, delta.X) });
        }

        void DrawArc(List<MySprite> sprites, Vector2 center, float radius, float startAngle, float endAngle, int segments, float thickness, Color color)
        {
            if (radius <= 0f || segments <= 0) return;
            var previous = center + new Vector2((float)Math.Cos(startAngle), (float)Math.Sin(startAngle)) * radius;
            for (var i = 1; i <= segments; i++)
            {
                var t = i / (float)segments;
                var angle = MathHelper.Lerp(startAngle, endAngle, t);
                var current = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                DrawLine(sprites, previous, current, thickness, color);
                previous = current;
            }
        }

        void DrawBevelBorder(List<MySprite> sprites, RectangleF rect, float border, Color topLeftColor, Color bottomRightColor)
        {
            if (border <= 0f || rect.Width <= 0f || rect.Height <= 0f) return;
            border = Math.Min(border, Math.Min(rect.Width, rect.Height) * 0.5f);
            if (border <= 0f) return;

            AddRectangleIfVisible(sprites, new RectangleF(rect.X + border, rect.Y, Math.Max(0f, rect.Width - border * 2f), border), topLeftColor);
            AddRectangleIfVisible(sprites, new RectangleF(rect.X, rect.Y + border, border, Math.Max(0f, rect.Height - border * 2f)), topLeftColor);
            AddRectangleIfVisible(sprites, new RectangleF(rect.Right - border, rect.Y + border, border, Math.Max(0f, rect.Height - border * 2f)), bottomRightColor);
            AddRectangleIfVisible(sprites, new RectangleF(rect.X + border, rect.Bottom - border, Math.Max(0f, rect.Width - border * 2f), border), bottomRightColor);

            var topLeftCorner = new RectangleF(rect.X, rect.Y, border, border);
            var topRightCorner = new RectangleF(rect.Right - border, rect.Y, border, border);
            var bottomLeftCorner = new RectangleF(rect.X, rect.Bottom - border, border, border);
            var bottomRightCorner = new RectangleF(rect.Right - border, rect.Bottom - border, border, border);

            AddRectangleIfVisible(sprites, topLeftCorner, topLeftColor);
            AddRectangleIfVisible(sprites, bottomRightCorner, bottomRightColor);
            DrawSplitBevelCorner(sprites, topRightCorner, topLeftColor, bottomRightColor);
            DrawSplitBevelCorner(sprites, bottomLeftCorner, topLeftColor, bottomRightColor);
        }

        static void DrawSplitBevelCorner(List<MySprite> sprites, RectangleF rect, Color topLeftColor, Color bottomRightColor)
        {
            if (rect.Width <= 0f || rect.Height <= 0f) return;
            sprites.Add(DrawRightTriangle(rect, topLeftColor, Math.PI / 2));
            sprites.Add(DrawRightTriangle(rect, bottomRightColor, Math.PI / 2 * 3));
        }

        static void AddRectangleIfVisible(List<MySprite> sprites, RectangleF rect, Color color)
        {
            if (rect.Width <= 0f || rect.Height <= 0f) return;
            sprites.Add(RectSprite(rect, color));
        }

        static MySprite DrawRightTriangle(RectangleF rect, Color color, double rotation)
        {
            var sprite = MySprite.CreateSprite("RightTriangle", rect.Center, new Vector2(rect.Height, rect.Width));
            sprite.Color = color;
            sprite.RotationOrScale = (float)rotation;
            return sprite;
        }

        RectangleF GetBoardClipArea()
        {
            // Striped ball bands must remain visible while balls are in the funnel/pocket openings,
            // which live in the rail area outside _playRect.  Use the whole table board area
            // rather than only the visible felt rectangle.
            var board = _tableOuterRect;
            var left = Math.Max(board.X, _viewBox.X);
            var top = Math.Max(board.Y, _viewBox.Y);
            var right = Math.Min(board.Right, _viewBox.Right);
            var bottom = Math.Min(board.Bottom, _viewBox.Bottom);
            return new RectangleF(left, top, Math.Max(0f, right - left), Math.Max(0f, bottom - top));
        }

        static RectangleF Inset(RectangleF rect, float amount)
        {
            return new RectangleF(rect.X + amount, rect.Y + amount, Math.Max(0f, rect.Width - amount * 2f), Math.Max(0f, rect.Height - amount * 2f));
        }

        static Rectangle ToClipRect(RectangleF rect)
        {
            var left = (int)Math.Floor(rect.X);
            var top = (int)Math.Floor(rect.Y);
            var right = (int)Math.Ceiling(rect.Right);
            var bottom = (int)Math.Ceiling(rect.Bottom);
            return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        static MySprite RectSprite(RectangleF rect, Color color)
        {
            var sprite = MySprite.CreateSprite("SquareSimple", rect.Center, rect.Size);
            sprite.Color = color;
            return sprite;
        }

        long GetCurrentGameplayFrame()
        {
            return MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
        }

        static string Loc(string key)
        {
            return LocHelper.GetLoc(key);
        }

        static string FormatLoc(string key, params object[] args)
        {
            return string.Format(Loc(key), args);
        }
        
        public IReadOnlyList<Control> Children { get; set; }

        public bool HasVisibleItems()
        {
            return true;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }
    }
}
