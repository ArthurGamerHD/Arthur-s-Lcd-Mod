using System;
using System.Collections.Generic;
using System.Text;
using Graph.Apps.Abstract;
using Graph.Apps.Utility;
using Graph.Helpers;
using Graph.System.Config;
using Graph.System.Controls;
using Graph.System.Modules;
using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Apps.Games.Minesweeper
{
    public sealed class MinesweeperGame : IGame
    {
        const byte MINE = 1;
        const byte REVEALED = 2;
        const byte FLAGGED = 4;
        const byte EXPLODED = 8;
        const int ADJACENT_SHIFT = 4;
        const float BOARD_FRAME_RATIO = 0.01f;
        const float OUTER_FRAME_CONTENT_MARGIN_RATIO = 0.016f;
        const float TILE_BEVEL_RATIO = 0.16f;
        const float EMOTE_BEVEL_RATIO = 0.1f;
        const float TILE_OUTER_INSET_RATIO = 0.016f;
        const float HEADER_PANEL_RATIO = 0.13f;
        const float STATUS_BUTTON_RATIO = 0.78f;
        const int MAX_TIMER_SECONDS = 999;
        const long GAMEPLAY_FRAMES_PER_SECOND = 60L;
        const long IDLE_NEUTRAL_SECONDS = 2L;
        const string BOMB_TEXTURE = "Textures\\FactionLogo\\Others\\OtherIcon_19.dds";
        const string EXPLODED_BOMB_TEXTURE = "Textures\\FactionLogo\\Others\\OtherIcon_23.dds";
        const string UNKNOWN_TEXTURE = "CursorHelp";
        const string FLAG_TEXTURE = "Flag";

        readonly IMyTextSurface _panel;
        readonly InteractiveSurfaceScript _script;
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<MySprite> _boardVisualCache = new List<MySprite>();
        readonly List<MinesweeperHistoryEntry> _history = new List<MinesweeperHistoryEntry>();
        readonly HashSet<int> _unknownCells = new HashSet<int>();
        readonly object _statusButtonContext = new object();
        readonly Random _emoteRandom = new Random();

        readonly Color[] _tileColors =
        {
            new Color(000, 000, 000), // Bomb
            new Color(123, 198, 255), // 1
            new Color(102, 193, 102), // 2
            new Color(255, 118, 135), // 3
            new Color(237, 135, 254), // 4
            new Color(220, 168, 039), // 5
            new Color(080, 134, 138), // 6
            new Color(152, 152, 152), // 7
            new Color(207, 215, 223), // 8
            new Color(255, 255, 255), // Explosion
            new Color(236, 240, 241), // Unknown
            new Color(231, 076, 060), // Flag
        };

        readonly Color _bevelLight = new Color(120, 127, 135);
        readonly Color _bevelDark = new Color(39, 46, 53);
        readonly Color _tileFaceHidden = new Color(78, 85, 92);
        readonly Color _tileFaceRevealed = new Color(59, 66, 74);
        readonly Color _tileFaceExploded = new Color(237, 101, 101);
        readonly Color _displayBackground = new Color(8, 8, 8);
        readonly Color _displayRed = new Color(255, 32, 32);
        readonly Color _emoteYellow = new Color(255, 210, 32);

        RectangleF _viewBox;
        RectangleF _headerOuterRect;
        RectangleF _bombDisplayRect;
        RectangleF _timeDisplayRect;
        RectangleF _statusButtonRect;
        RectangleF _boardOuterRect;
        RectangleF _gridOuterRect;
        RectangleF _boardViewBox;
        float _boardFrameThickness;
        RectangleF[] _gridCells;
        byte[] _cells;

        int _width;
        int _height;
        int _mineCount;
        int _revealedCount;
        int _flagsUsed;
        int _seed;
        bool _minesPlaced;
        bool _flagMode;
        MinesweeperState _state;
        MinesweeperDifficulty _difficulty;
        long _timerStartedFrame;
        long _lastTileOpenFrame;
        int _elapsedSeconds;
        bool _timerRunning;
        bool _suspiciousLooksLeft = true;
        int _suspiciousTicksUntilSwitch;
        float _cellTextScale;
        float _displayTextScale;

        public List<InteractiveEntry> Interactive { get; }

        public GameSurfaceScript.GameEnum Id => GameSurfaceScript.GameEnum.Minesweeper;

        public MinesweeperGame(IMyTextSurface panel, InteractiveSurfaceScript script)
        {
            _panel = panel;
            _script = script;
            Interactive = new List<InteractiveEntry>();
            _difficulty = MinesweeperDifficulty.Easy;

            ReloadProgram();

            _script.SetGlobalMenu(
                new GlobalMenuEntry("Game", new List<GlobalMenuEntry>
                {
                    new GlobalMenuEntry("New Game", delegate
                    {
                        NewGame();
                        Save();
                    }),
                    new GlobalMenuEntry("Flag Mode", delegate { ToggleFlagMode(); }),
                    new GlobalMenuEntry("Difficulty", new List<GlobalMenuEntry>
                    {
                        new GlobalMenuEntry("Easy 9x9", delegate { SetDifficulty(MinesweeperDifficulty.Easy); }),
                        new GlobalMenuEntry("Medium 16x16", delegate { SetDifficulty(MinesweeperDifficulty.Medium); }),
                        new GlobalMenuEntry("Hard 30x16", delegate { SetDifficulty(MinesweeperDifficulty.Hard); })
                    })
                })
            );
        }

        public void Tick()
        {
            UpdateSuspiciousEmote();
        }

        public List<MySprite> Render()
        {
            _sprites.Clear();

            if (_viewBox != _script.ViewBox || _gridCells == null)
                BakeBoardVisual();

            RenderBoard(_sprites);
            RenderHeader(_sprites);
            RenderCells(_sprites);
            RebuildInteractiveEntries();

            return _sprites;
        }

        void ReloadProgram()
        {
            Load();
            BakeBoardVisual();
        }

        void SetDifficulty(MinesweeperDifficulty difficulty)
        {
            _difficulty = difficulty;
            NewGame();
            Save();
        }

        void ApplyDifficulty(MinesweeperDifficulty difficulty)
        {
            switch (difficulty)
            {
                case MinesweeperDifficulty.Hard:
                    _width = 30;
                    _height = 16;
                    _mineCount = 99;
                    break;
                case MinesweeperDifficulty.Medium:
                    _width = 16;
                    _height = 16;
                    _mineCount = 40;
                    break;
                case MinesweeperDifficulty.Easy:
                default:
                    _width = 9;
                    _height = 9;
                    _mineCount = 10;
                    break;
            }
        }

        void NewGame()
        {
            ApplyDifficulty(_difficulty);
            AllocateBoard();
            _state = MinesweeperState.Playing;
            _revealedCount = 0;
            _flagsUsed = 0;
            _minesPlaced = false;
            _flagMode = false;
            _seed = unchecked((int)DateTime.UtcNow.Ticks);
            _elapsedSeconds = 0;
            _timerStartedFrame = GetCurrentGameplayFrame();
            _lastTileOpenFrame = _timerStartedFrame;
            _timerRunning = true;
            _suspiciousLooksLeft = true;
            _suspiciousTicksUntilSwitch = 0;
            _history.Clear();
            _unknownCells.Clear();
            BakeBoardVisual();
        }

        void AllocateBoard()
        {
            int count = Math.Max(1, _width * _height);
            _cells = new byte[count];
            _gridCells = null;
        }

        MinesweeperGameConfig BuildConfig()
        {
            return new MinesweeperGameConfig
            {
                Width = _width,
                Height = _height,
                MineCount = _mineCount,
                State = (int)_state,
                RevealedCount = _revealedCount,
                FlagsUsed = _flagsUsed,
                Seed = _seed,
                Cells = CopyCells(),
                History = SerializeHistory(),
                FlagMode = _flagMode,
                MinesPlaced = _minesPlaced,
                UnknownCells = SerializeUnknownCells()
            };
        }

        byte[] CopyCells()
        {
            if (_cells == null)
                return null;

            var copy = new byte[_cells.Length];
            _cells.CopyTo(copy, 0);
            return copy;
        }

        public void Save()
        {
            _script.Config.CustomData = MyAPIGateway.Utilities.SerializeToBinary(BuildConfig());
            ConfigManager.Sync((IMyTerminalBlock)_script.Block, _script.ProviderConfig);
        }

        MinesweeperGameConfig LoadConfig()
        {
            var data = _script.Config.CustomData;
            if (data == null || data.Length == 0)
                throw new Exception("Missing minesweeper config.");

            return MyAPIGateway.Utilities.SerializeFromBinary<MinesweeperGameConfig>(data);
        }

        public void Load()
        {
            try
            {
                var config = LoadConfig();
                if (config == null)
                    throw new Exception("Missing minesweeper config.");

                if (config.Width <= 0 || config.Height <= 0 || config.Cells == null ||
                    config.Cells.Length != config.Width * config.Height)
                    throw new Exception("Corrupted minesweeper board.");

                _width = config.Width;
                _height = config.Height;
                _mineCount = Math.Max(1, Math.Min(config.MineCount, _width * _height - 1));
                _state = (MinesweeperState)config.State;
                _revealedCount = config.RevealedCount;
                _flagsUsed = config.FlagsUsed;
                _seed = config.Seed;
                _flagMode = config.FlagMode;
                _minesPlaced = config.MinesPlaced;
                _cells = new byte[config.Cells.Length];
                config.Cells.CopyTo(_cells, 0);
                DeserializeHistory(config.History);
                DeserializeUnknownCells(config.UnknownCells);
                _difficulty = InferDifficulty(_width, _height, _mineCount);
                _lastTileOpenFrame = _timerStartedFrame;
                _timerRunning = _state == MinesweeperState.Playing;
                _suspiciousLooksLeft = true;
                _suspiciousTicksUntilSwitch = 0;
            }
            catch (Exception e)
            {
                LogHelper.Log(e.ToString());
                _difficulty = MinesweeperDifficulty.Easy;
                NewGame();
                Save();
            }
        }

        MinesweeperDifficulty InferDifficulty(int width, int height, int mineCount)
        {
            if (width == 30 && height == 16 && mineCount == 99)
                return MinesweeperDifficulty.Hard;
            if (width == 16 && height == 16 && mineCount == 40)
                return MinesweeperDifficulty.Medium;
            return MinesweeperDifficulty.Easy;
        }

        void BakeBoardVisual()
        {
            _viewBox = _script.ViewBox;

            if (_cells == null || _cells.Length == 0)
                AllocateBoard();

            float viewBoxAreaBasis = (float)Math.Sqrt(Math.Max(1f, _viewBox.Width * _viewBox.Height));
            float padding = Math.Max(3f, viewBoxAreaBasis * 0.015f);
            _boardFrameThickness = GetScaledBorder(viewBoxAreaBasis, BOARD_FRAME_RATIO);
            float outerFrameContentMargin = GetScaledBorder(viewBoxAreaBasis, OUTER_FRAME_CONTENT_MARGIN_RATIO);
            float totalFrameInset = _boardFrameThickness + outerFrameContentMargin;

            float availableOuterWidth = Math.Max(1f, _viewBox.Width - padding * 2f);
            float availableOuterHeight = Math.Max(1f, _viewBox.Height - padding * 2f);
            float availableContentWidth = Math.Max(1f, availableOuterWidth - totalFrameInset * 2f);
            float availableContentHeight = Math.Max(1f, availableOuterHeight - totalFrameInset * 2f);

            float headerHeight = Math.Max(_boardFrameThickness * 9f, viewBoxAreaBasis * HEADER_PANEL_RATIO);
            headerHeight = Math.Min(headerHeight, Math.Max(1f, availableContentHeight * 0.26f));
            float contentGap = _boardFrameThickness;
            float innerPanelBorder = _boardFrameThickness;

            float availableGridWidth = Math.Max(1f, availableContentWidth - innerPanelBorder * 2f);
            float availableGridHeight =
                Math.Max(1f, availableContentHeight - headerHeight - contentGap - innerPanelBorder * 2f);
            float cellSize = Math.Max(1f, Math.Min(availableGridWidth / _width, availableGridHeight / _height));
            float boardWidth = cellSize * _width;
            float boardHeight = cellSize * _height;
            float gridOuterWidth = boardWidth + innerPanelBorder * 2f;
            float gridOuterHeight = boardHeight + innerPanelBorder * 2f;

            float assemblyContentWidth = Math.Max(gridOuterWidth, Math.Min(availableContentWidth, gridOuterWidth));
            float assemblyContentHeight = headerHeight + contentGap + gridOuterHeight;
            float assemblyOuterWidth = assemblyContentWidth + totalFrameInset * 2f;
            float assemblyOuterHeight = assemblyContentHeight + totalFrameInset * 2f;

            _boardOuterRect = new RectangleF(
                _viewBox.Center.X - assemblyOuterWidth * 0.5f,
                _viewBox.Y + padding + (availableOuterHeight - assemblyOuterHeight) * 0.5f,
                assemblyOuterWidth,
                assemblyOuterHeight);

            var assemblyContentRect = Inset(_boardOuterRect, totalFrameInset);

            _headerOuterRect = new RectangleF(
                assemblyContentRect.X,
                assemblyContentRect.Y,
                assemblyContentRect.Width,
                headerHeight);

            _gridOuterRect = new RectangleF(
                assemblyContentRect.Center.X - gridOuterWidth * 0.5f,
                _headerOuterRect.Bottom + contentGap,
                gridOuterWidth,
                gridOuterHeight);

            _boardViewBox = Inset(_gridOuterRect, innerPanelBorder);
            _gridCells = GetGridCells(_boardViewBox, _width, _height);

            LayoutHeaderControls();

            var cellMeasure = _panel.MeasureStringInPixels(new StringBuilder("8"), "Monospace", 1f);
            _cellTextScale = cellSize * 0.7f / cellMeasure.Y;

            var displayMeasure = _panel.MeasureStringInPixels(new StringBuilder("888"), "Monospace", 1f);
            float displayScaleX = displayMeasure.X <= 0 ? 0.6f : _bombDisplayRect.Width * 0.82f / displayMeasure.X;
            float displayScaleY = displayMeasure.Y <= 0 ? 0.6f : _bombDisplayRect.Height * 0.62f / displayMeasure.Y;
            _displayTextScale = Math.Max(0.35f, Math.Min(displayScaleX, displayScaleY));

            _boardVisualCache.Clear();
            DrawBoardFrame(_boardVisualCache);
        }

        static RectangleF[] GetGridCells(RectangleF frame, int width, int height)
        {
            RectangleF[] rectangles = new RectangleF[width * height];
            float cellWidth = frame.Width / width;
            float cellHeight = frame.Height / height;
            int index = 0;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    float x = frame.X + col * cellWidth;
                    float y = frame.Y + row * cellHeight;
                    rectangles[index++] = new RectangleF(x, y, cellWidth, cellHeight);
                }
            }

            return rectangles;
        }

        void RenderHeader(List<MySprite> frame)
        {
            DrawDigitalDisplay(frame, _bombDisplayRect, _mineCount - _flagsUsed);
            DrawStatusButton(frame);
            DrawDigitalDisplay(frame, _timeDisplayRect, GetTimerSeconds());
        }

        void LayoutHeaderControls()
        {
            float border = _boardFrameThickness > 0f ? _boardFrameThickness : 4f;
            var content = Inset(_headerOuterRect, border);
            float gap = Math.Max(4f, border * 2f);
            float buttonSize = Math.Max(12f, Math.Min(content.Height * STATUS_BUTTON_RATIO, content.Width * 0.18f));
            float displayHeight = Math.Max(12f, buttonSize * 0.82f);
            float displayWidth = Math.Max(displayHeight * 1.65f, Math.Min(content.Width * 0.31f, displayHeight * 2.6f));
            float displayY = content.Center.Y - displayHeight * 0.5f;

            _bombDisplayRect = new RectangleF(
                content.X + gap,
                displayY,
                displayWidth,
                displayHeight);

            _statusButtonRect = new RectangleF(
                content.Center.X - buttonSize * 0.5f,
                content.Center.Y - buttonSize * 0.5f,
                buttonSize,
                buttonSize);

            _timeDisplayRect = new RectangleF(
                content.Right - gap - displayWidth,
                displayY,
                displayWidth,
                displayHeight);
        }

        void DrawDigitalDisplay(List<MySprite> frame, RectangleF rect, int value)
        {
            float border = Math.Max(1f, GetScaledBorder(Math.Min(rect.Width, rect.Height), 0.10f));
            DrawTileFrame(frame, rect, border, true, _displayBackground);

            string text = FormatThreeDigits(value);
            Vector2 size = _panel.MeasureStringInPixels(new StringBuilder(text), "Monospace", _displayTextScale);
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - size.Y * 0.5f),
                Color = _displayRed,
                FontId = "Monospace",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = _displayTextScale
            });
        }

        void DrawStatusButton(List<MySprite> frame)
        {
            float border = GetTileBorder(_statusButtonRect, EMOTE_BEVEL_RATIO);
            DrawTileFrame(frame, _statusButtonRect, border, false, _tileFaceHidden);

            var iconRect = Inset(_statusButtonRect, border * 1.35f);

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = iconRect.Center,
                Size = iconRect.Size,
                Color = Color.Black,
                Alignment = TextAlignment.CENTER
            });

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = GetStatusEmoteTexture(),
                Position = iconRect.Center,
                Size = iconRect.Size * .95f,
                Color = _emoteYellow,
                Alignment = TextAlignment.CENTER
            });
            if (_state == MinesweeperState.Won)
            {
                frame.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = "\n" +
                           "\n" +
                           "\n" +
                           "\n" +
                           "\n" +
                           "",
                    Position = new Vector2(iconRect.Center.X, iconRect.Center.Y - iconRect.Size.Y * 0.18f),
                    Color = Color.Black,
                    Alignment = TextAlignment.CENTER,
                    FontId = "Monospace",
                    RotationOrScale = .055f * _displayTextScale
                });
            }
        }

        string GetStatusEmoteTexture()
        {
            if (_state == MinesweeperState.Won)
                return "LCD_Emote_Wink";
            if (_state == MinesweeperState.Lost)
                return "LCD_Emote_Dead";
            if (EyeTrackingModule.HoldingClick)
                return "LCD_Emote_Shocked";
            if (IsSuspiciousEmoteActive())
                return _suspiciousLooksLeft ? "LCD_Emote_Suspicious_Left" : "LCD_Emote_Suspicious_Right";
            if (IsIdleNeutral())
                return "LCD_Emote_Neutral";
            return "LCD_Emote_Happy";
        }

        static string FormatThreeDigits(int value)
        {
            value = Math.Max(0, Math.Min(999, value));
            return value.ToString("000");
        }

        void RenderBoard(List<MySprite> frame)
        {
            frame.AddRange(_boardVisualCache);
        }

        void RenderCells(List<MySprite> frame)
        {
            if (_cells == null || _gridCells == null)
                return;

            for (int i = 0; i < _cells.Length && i < _gridCells.Length; i++)
                RenderCell(frame, i);
        }

        void RenderCell(List<MySprite> frame, int index)
        {
            var cell = _cells[index];
            var rect = _gridCells[index];
            bool revealed = IsSet(cell, REVEALED);
            bool flagged = IsSet(cell, FLAGGED);
            bool mine = IsSet(cell, MINE);
            bool exploded = IsSet(cell, EXPLODED);
            float tileBorder = GetTileBorder(rect, TILE_BEVEL_RATIO);

            if (!revealed)
            {
                DrawTileFrame(frame, rect, tileBorder, false, _tileFaceHidden);
                if (flagged)
                    DrawCenteredTexture(frame, FLAG_TEXTURE, rect, tileBorder, _tileColors[11]);
                else if (_unknownCells.Contains(index))
                    DrawCenteredTexture(frame, UNKNOWN_TEXTURE, rect, tileBorder, _tileColors[10]);
                return;
            }

            DrawFlatTile(frame, rect, exploded ? _tileFaceExploded : _tileFaceRevealed);

            if (mine)
            {
                DrawCenteredTexture(frame, exploded ? EXPLODED_BOMB_TEXTURE : BOMB_TEXTURE, rect, tileBorder,
                    exploded ? _tileColors[9] : _tileColors[0]);
                return;
            }

            int adjacent = GetAdjacent(cell);
            if (adjacent > 0)
                DrawCenteredText(frame, adjacent.ToString(), rect,
                    _tileColors[Math.Min(adjacent, _tileColors.Length - 1)]);
        }

        void DrawCenteredText(List<MySprite> frame, string text, RectangleF rect, Color color)
        {
            Vector2 size = _panel.MeasureStringInPixels(new StringBuilder(text), "Monospace", _cellTextScale);
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - size.Y * 0.5f),
                Color = color,
                FontId = "Monospace",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = _cellTextScale
            });
        }

        void DrawCenteredTexture(List<MySprite> frame, string texture, RectangleF rect, float border, Color color)
        {
            var iconRect = Inset(rect, Math.Max(border * 1.25f, Math.Min(rect.Width, rect.Height) * 0.18f));
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture,
                Position = iconRect.Center,
                Size = iconRect.Size,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        void DrawBoardFrame(List<MySprite> frame)
        {
            float border = _boardFrameThickness > 0f ? _boardFrameThickness : 4f;
            DrawBevelBorder(frame, _boardOuterRect, border, _bevelLight, _bevelDark);
            frame.Add(DrawRectangle(Inset(_boardOuterRect, border), _tileFaceHidden));
            DrawInsetPanel(frame, _headerOuterRect, border, _tileFaceHidden);
            DrawInsetPanel(frame, _gridOuterRect, border, _tileFaceHidden);
        }

        void DrawInsetPanel(List<MySprite> frame, RectangleF rect, float border, Color fillColor)
        {
            DrawBevelBorder(frame, rect, border, _bevelDark, _bevelLight);
            frame.Add(DrawRectangle(Inset(rect, border), fillColor));
        }

        void DrawFlatTile(List<MySprite> frame, RectangleF rect, Color fillColor)
        {
            var outer = Inset(rect, Math.Max(0f, Math.Min(rect.Width, rect.Height) * TILE_OUTER_INSET_RATIO));
            frame.Add(DrawRectangle(outer, fillColor));
        }

        void DrawTileFrame(List<MySprite> frame, RectangleF rect, float border, bool pressed, Color fillColor)
        {
            var outer = Inset(rect, Math.Max(0f, Math.Min(rect.Width, rect.Height) * TILE_OUTER_INSET_RATIO));
            DrawBevelBorder(frame, outer, border, pressed ? _bevelDark : _bevelLight,
                pressed ? _bevelLight : _bevelDark);
            var inner = Inset(outer, border);
            frame.Add(DrawRectangle(inner, fillColor));
        }

        void DrawBevelBorder(List<MySprite> frame, RectangleF rect, float border, Color topLeftColor,
            Color bottomRightColor)
        {
            if (border <= 0f || rect.Width <= 0f || rect.Height <= 0f)
                return;

            border = Math.Min(border, Math.Min(rect.Width, rect.Height) * 0.5f);
            if (border <= 0f)
                return;

            var topRect = new RectangleF(
                rect.X + border,
                rect.Y,
                Math.Max(0f, rect.Width - border * 2f),
                border);

            var leftRect = new RectangleF(
                rect.X,
                rect.Y + border,
                border,
                Math.Max(0f, rect.Height - border * 2f));

            var rightRect = new RectangleF(
                rect.Right - border,
                rect.Y + border,
                border,
                Math.Max(0f, rect.Height - border * 2f));

            var bottomRect = new RectangleF(
                rect.X + border,
                rect.Bottom - border,
                Math.Max(0f, rect.Width - border * 2f),
                border);

            AddRectangleIfVisible(frame, topRect, topLeftColor);
            AddRectangleIfVisible(frame, leftRect, topLeftColor);
            AddRectangleIfVisible(frame, rightRect, bottomRightColor);
            AddRectangleIfVisible(frame, bottomRect, bottomRightColor);

            var topLeftCorner = new RectangleF(rect.X, rect.Y, border, border);
            var topRightCorner = new RectangleF(rect.Right - border, rect.Y, border, border);
            var bottomLeftCorner = new RectangleF(rect.X, rect.Bottom - border, border, border);
            var bottomRightCorner = new RectangleF(rect.Right - border, rect.Bottom - border, border, border);

            AddRectangleIfVisible(frame, topLeftCorner, topLeftColor);
            AddRectangleIfVisible(frame, bottomRightCorner, bottomRightColor);

            DrawSplitBevelCorner(frame, topRightCorner, topLeftColor, bottomRightColor);
            DrawSplitBevelCorner(frame, bottomLeftCorner, topLeftColor, bottomRightColor);
        }

        static void DrawSplitBevelCorner(List<MySprite> frame, RectangleF rect, Color topLeftColor,
            Color bottomRightColor)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            frame.Add(DrawRightTriangle(rect, topLeftColor, Math.PI / 2));
            frame.Add(DrawRightTriangle(rect, bottomRightColor, Math.PI / 2 * 3));
        }

        static void AddRectangleIfVisible(List<MySprite> frame, RectangleF rect, Color color)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            frame.Add(DrawRectangle(rect, color));
        }

        static RectangleF Inset(RectangleF rect, float amount)
        {
            return new RectangleF(
                rect.X + amount,
                rect.Y + amount,
                Math.Max(0f, rect.Width - amount * 2f),
                Math.Max(0f, rect.Height - amount * 2f));
        }

        static float GetTileBorder(RectangleF rect, float bevel)
        {
            return GetScaledBorder(Math.Min(rect.Width, rect.Height), bevel);
        }

        static float GetScaledBorder(float size, float ratio)
        {
            return Math.Max(1f, (float)Math.Floor(size * ratio));
        }

        static MySprite DrawRectangle(RectangleF grid, Color color)
        {
            var sprite = MySprite.CreateSprite("SquareSimple", grid.Center, grid.Size);
            sprite.Color = color;
            return sprite;
        }

        static MySprite DrawRightTriangle(RectangleF grid, Color color, double rotation)
        {
            var sprite = MySprite.CreateSprite("RightTriangle", grid.Center, new Vector2(grid.Height, grid.Width));
            sprite.Color = color;
            sprite.RotationOrScale = (float)rotation;
            return sprite;
        }

        void RebuildInteractiveEntries()
        {
            Interactive.Clear();

            if (_statusButtonRect.Width > 0f && _statusButtonRect.Height > 0f)
            {
                Interactive.Add(new InteractiveRectangleEntry(
                    _statusButtonRect,
                    CursorType.Hand,
                    _statusButtonContext,
                    delegate(object value, object sender) { RestartGame(); })
                {
                    ClickSound = AudioHelper.HudClick
                });
            }

            if (_gridCells == null)
                return;

            for (int i = 0; i < _gridCells.Length; i++)
            {
                int capturedIndex = i;
                Interactive.Add(new InteractiveRectangleEntry(
                    _gridCells[i],
                    GetCellCursor(i),
                    capturedIndex,
                    delegate(object value, object sender) { ClickCell((int)value, _flagMode); })
                {
                    ClickSound = GetCellClickSound(i),
                    OnSecondaryClick = delegate(object value, object sender) { FlagCell((int)value); }
                });
            }
        }

        CursorType GetCellCursor(int index)
        {
            if (_state != MinesweeperState.Playing)
                return CursorType.Cross;

            if (index < 0 || _cells == null || index >= _cells.Length)
                return CursorType.Default;

            var cell = _cells[index];
            if (IsSet(cell, REVEALED))
                return CursorType.Default;

            return CursorType.Hand;
        }

        MySoundPair GetCellClickSound(int index)
        {
            if (_state != MinesweeperState.Playing)
                return AudioHelper.HudUnable;

            if (index < 0 || _cells == null || index >= _cells.Length)
                return AudioHelper.HudUnable;

            return AudioHelper.HudClick;
        }

        void ClickCell(int index, bool flag)
        {
            if (index < 0 || _cells == null || index >= _cells.Length)
                return;

            if (_state != MinesweeperState.Playing)
                return;

            bool changed = flag ? ToggleFlag(index) : Reveal(index);
            if (!changed)
                return;

            StartTimerIfNeeded();
            Save();
        }

        void FlagCell(int value) => ClickCell(value, true);

        bool ToggleFlag(int index)
        {
            if (IsSet(_cells[index], REVEALED))
                return false;

            if (IsSet(_cells[index], FLAGGED))
            {
                _cells[index] = Clear(_cells[index], FLAGGED);
                _flagsUsed = Math.Max(0, _flagsUsed - 1);
                _unknownCells.Add(index);
                AddHistory('?', index);
                return true;
            }

            if (_unknownCells.Contains(index))
            {
                _unknownCells.Remove(index);
                AddHistory('U', index);
                return true;
            }

            if (_flagsUsed >= _mineCount)
                return false;

            _unknownCells.Remove(index);
            _cells[index] = Set(_cells[index], FLAGGED);
            _flagsUsed++;
            AddHistory('F', index);
            return true;
        }

        bool Reveal(int index)
        {
            if (IsSet(_cells[index], FLAGGED))
                return false;

            _unknownCells.Remove(index);

            if (IsSet(_cells[index], REVEALED))
                return RevealAroundIfSatisfied(index);

            if (!_minesPlaced)
                PlaceMines(index);

            if (IsSet(_cells[index], MINE))
            {
                _cells[index] = Set(Set(_cells[index], REVEALED), EXPLODED);
                _state = MinesweeperState.Lost;
                AddHistory('X', index);
                RevealAllMines();
                StopTimer();
                return true;
            }

            FloodReveal(index);
            AddHistory('R', index);
            CheckWin();
            return true;
        }

        bool RevealAroundIfSatisfied(int index)
        {
            int adjacent = GetAdjacent(_cells[index]);
            if (adjacent <= 0)
                return false;

            int flags = CountNeighborFlags(index);
            if (flags != adjacent)
                return false;

            bool changed = false;
            var neighbors = GetNeighbors(index);
            for (int i = 0; i < neighbors.Count; i++)
            {
                int neighbor = neighbors[i];
                if (!IsSet(_cells[neighbor], FLAGGED) && !IsSet(_cells[neighbor], REVEALED))
                {
                    if (Reveal(neighbor))
                        changed = true;
                    if (_state != MinesweeperState.Playing)
                        return changed;
                }
            }

            return changed;
        }

        void PlaceMines(int safeIndex)
        {
            _minesPlaced = true;

            var protectedCells = new HashSet<int>();
            protectedCells.Add(safeIndex);
            var safeNeighbors = GetNeighbors(safeIndex);
            for (int i = 0; i < safeNeighbors.Count; i++)
                protectedCells.Add(safeNeighbors[i]);

            int maxMines = Math.Max(1, _cells.Length - protectedCells.Count);
            int minesToPlace = Math.Min(_mineCount, maxMines);
            _mineCount = minesToPlace;

            var random = new Random(_seed == 0 ? _seed = unchecked((int)DateTime.UtcNow.Ticks) : _seed);
            int placed = 0;
            int guard = 0;
            while (placed < minesToPlace && guard < _cells.Length * 100)
            {
                guard++;
                int index = random.Next(_cells.Length);
                if (protectedCells.Contains(index) || IsSet(_cells[index], MINE))
                    continue;

                _cells[index] = Set(_cells[index], MINE);
                placed++;
            }

            RecalculateAdjacentCounts();
        }

        void RecalculateAdjacentCounts()
        {
            for (int i = 0; i < _cells.Length; i++)
                _cells[i] = (byte)(_cells[i] & 15);

            for (int i = 0; i < _cells.Length; i++)
            {
                if (!IsSet(_cells[i], MINE))
                    continue;

                var neighbors = GetNeighbors(i);
                for (int n = 0; n < neighbors.Count; n++)
                {
                    int neighbor = neighbors[n];
                    if (IsSet(_cells[neighbor], MINE))
                        continue;

                    int adjacent = GetAdjacent(_cells[neighbor]) + 1;
                    _cells[neighbor] = SetAdjacent(_cells[neighbor], adjacent);
                }
            }
        }

        void FloodReveal(int index)
        {
            var queue = new Queue<int>();
            RevealSafe(index);
            queue.Enqueue(index);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (GetAdjacent(_cells[current]) != 0)
                    continue;

                var neighbors = GetNeighbors(current);
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    if (IsSet(_cells[neighbor], REVEALED) || IsSet(_cells[neighbor], FLAGGED) ||
                        IsSet(_cells[neighbor], MINE))
                        continue;

                    RevealSafe(neighbor);
                    if (GetAdjacent(_cells[neighbor]) == 0)
                        queue.Enqueue(neighbor);
                }
            }
        }

        void RevealSafe(int index)
        {
            if (IsSet(_cells[index], REVEALED))
                return;

            _unknownCells.Remove(index);
            _cells[index] = Set(_cells[index], REVEALED);
            _revealedCount++;
            MarkTileOpened();
        }

        void RevealAllMines()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (IsSet(_cells[i], MINE))
                    _cells[i] = Set(_cells[i], REVEALED);
            }
        }

        void CheckWin()
        {
            int safeCount = _cells.Length - _mineCount;
            if (_revealedCount < safeCount)
                return;

            _state = MinesweeperState.Won;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (IsSet(_cells[i], MINE) && !IsSet(_cells[i], FLAGGED))
                {
                    _unknownCells.Remove(i);
                    _cells[i] = Set(_cells[i], FLAGGED);
                    _flagsUsed++;
                }
            }

            StopTimer();
        }

        int CountNeighborFlags(int index)
        {
            int count = 0;
            var neighbors = GetNeighbors(index);
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (IsSet(_cells[neighbors[i]], FLAGGED))
                    count++;
            }

            return count;
        }

        List<int> GetNeighbors(int index)
        {
            var result = new List<int>(8);
            int x = index % _width;
            int y = index / _width;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;

                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || nx >= _width || ny < 0 || ny >= _height)
                        continue;

                    result.Add(nx + ny * _width);
                }
            }

            return result;
        }

        void UpdateSuspiciousEmote()
        {
            if (!IsSuspiciousEmoteActive())
            {
                _suspiciousTicksUntilSwitch = 0;
                return;
            }

            _suspiciousTicksUntilSwitch--;
            if (_suspiciousTicksUntilSwitch > 0)
                return;

            _suspiciousLooksLeft = !_suspiciousLooksLeft;
            _suspiciousTicksUntilSwitch = _emoteRandom.Next(6, 61);
        }

        bool IsSuspiciousEmoteActive()
        {
            return _state == MinesweeperState.Playing && (_flagMode || CursorIsAboveUnknownTile());
        }

        bool CursorIsAboveUnknownTile()
        {
            int index = GetTileIndexAtCursor();
            return index >= 0 && !IsSet(_cells[index], REVEALED);
        }

        int GetTileIndexAtCursor()
        {
            if (_gridCells == null || _cells == null)
                return -1;

            var position = _script.CursorPosition;
            if (float.IsNaN(position.X) || float.IsNaN(position.Y))
                return -1;

            for (int i = 0; i < _gridCells.Length && i < _cells.Length; i++)
            {
                if (_gridCells[i].Contains(position))
                    return i;
            }

            return -1;
        }

        bool IsIdleNeutral()
        {
            if (_state != MinesweeperState.Playing)
                return false;

            long elapsedFrames = GetCurrentGameplayFrame() - _lastTileOpenFrame;
            return elapsedFrames >= IDLE_NEUTRAL_SECONDS * GAMEPLAY_FRAMES_PER_SECOND;
        }

        void MarkTileOpened()
        {
            _lastTileOpenFrame = GetCurrentGameplayFrame();
        }

        void ToggleFlagMode()
        {
            _flagMode = !_flagMode;
            _suspiciousTicksUntilSwitch = 0;
            Save();
        }

        void RestartGame()
        {
            NewGame();
            Save();
        }

        void StartTimerIfNeeded()
        {
            if (_timerRunning || _state != MinesweeperState.Playing)
                return;

            _timerStartedFrame = GetCurrentGameplayFrame();
            _lastTileOpenFrame = _timerStartedFrame;
            _timerRunning = true;
        }

        void StopTimer()
        {
            if (!_timerRunning)
                return;

            _elapsedSeconds = GetTimerSeconds();
            _timerRunning = false;
        }

        int GetTimerSeconds()
        {
            if (!_timerRunning)
                return Math.Max(0, Math.Min(MAX_TIMER_SECONDS, _elapsedSeconds));

            long frameDelta = GetCurrentGameplayFrame() - _timerStartedFrame;
            if (frameDelta < 0L)
                frameDelta = 0L;

            int seconds = _elapsedSeconds + (int)(frameDelta / GAMEPLAY_FRAMES_PER_SECOND);
            return Math.Max(0, Math.Min(MAX_TIMER_SECONDS, seconds));
        }

        static long GetCurrentGameplayFrame()
        {
            return MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
        }

        string SerializeHistory()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _history.Count; i++)
            {
                if (i > 0)
                    sb.Append(';');

                sb.Append(_history[i].Action);
                sb.Append(':');
                sb.Append(_history[i].Index);
            }

            return sb.ToString();
        }

        void DeserializeHistory(string data)
        {
            _history.Clear();
            if (string.IsNullOrEmpty(data))
                return;

            string[] parts = data.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part) || part.Length < 3)
                    continue;

                int sep = part.IndexOf(':');
                if (sep <= 0 || sep >= part.Length - 1)
                    continue;

                int index;
                if (!int.TryParse(part.Substring(sep + 1), out index))
                    continue;

                if (index < 0 || _cells == null || index >= _cells.Length)
                    continue;

                _history.Add(new MinesweeperHistoryEntry(part[0], index));
            }
        }

        string SerializeUnknownCells()
        {
            if (_unknownCells.Count == 0)
                return string.Empty;

            var indexes = new List<int>(_unknownCells);
            indexes.Sort();

            var sb = new StringBuilder();
            for (int i = 0; i < indexes.Count; i++)
            {
                if (i > 0)
                    sb.Append(';');

                sb.Append(indexes[i]);
            }

            return sb.ToString();
        }

        void DeserializeUnknownCells(string data)
        {
            _unknownCells.Clear();
            if (string.IsNullOrEmpty(data))
                return;

            string[] parts = data.Split(';');
            for (int i = 0; i < parts.Length; i++)
            {
                int index;
                if (!int.TryParse(parts[i], out index))
                    continue;

                if (index < 0 || _cells == null || index >= _cells.Length)
                    continue;

                if (IsSet(_cells[index], REVEALED) || IsSet(_cells[index], FLAGGED))
                    continue;

                _unknownCells.Add(index);
            }
        }

        void AddHistory(char action, int index)
        {
            _history.Add(new MinesweeperHistoryEntry(action, index));
        }

        static bool IsSet(byte value, byte flag)
        {
            return (value & flag) == flag;
        }

        static byte Set(byte value, byte flag)
        {
            return (byte)(value | flag);
        }

        static byte Clear(byte value, byte flag)
        {
            return (byte)(value & ~flag);
        }

        static int GetAdjacent(byte value)
        {
            return value >> ADJACENT_SHIFT;
        }

        static byte SetAdjacent(byte value, int adjacent)
        {
            return (byte)((value & 15) | ((Math.Max(0, Math.Min(8, adjacent)) & 15) << ADJACENT_SHIFT));
        }

        struct MinesweeperHistoryEntry
        {
            public readonly char Action;
            public readonly int Index;

            public MinesweeperHistoryEntry(char action, int index)
            {
                Action = action;
                Index = index;
            }
        }
    }

    internal enum MinesweeperState
    {
        Playing = 0,
        Won = 1,
        Lost = 2
    }

    internal enum MinesweeperDifficulty
    {
        Easy,
        Medium,
        Hard
    }
}