using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ChessChallenge.API;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Games.Chess.Enum;
using LcdMod.Client.Games.Chess.TinyChessChallenge.Bots;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using LcdMod.Client.Utility;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Games.Chess
{
    public partial class ChessGame : IGame
    {
        const string CUSTOM_DATA_KEY = "Chess";

        bool _playingAsBlack;
        bool _showDangers = true;

        long _playingAsWhitePlayerId;
        long _playingAsBlackPlayerId;
        int _sessionId;

        public Sandbox.ModAPI.Ingame.IMyTextSurface Surface => _script.Surface;

        RectangleF _viewBox;
        public RectangleF BoardViewBox;

        readonly Color[] _boardColors =
        {
            new Color(115, 149, 82),
            new Color(235, 236, 208),
            new Color(255, 255, 51, 127),
            new Color(0, 0, 0, 64),
            new Color(200, 32, 32, 64),
        };

        readonly Dictionary<byte, string> _textureCache = new Dictionary<byte, string>();

        public List<ControlBase> Interactive { get; } = new List<ControlBase>();
        public GameSurfaceScript.GameEnum Id => GameSurfaceScript.GameEnum.Chess;

        static string UnpackTexture(string packed)
        {
            if (string.IsNullOrEmpty(packed))
                return string.Empty;

            char headerChar = packed[0];
            int headerBits = headerChar;
            int width = (headerBits >> 8) & 0x7F; // width (7 bits)
            int height = (headerBits >> 1) & 0x7F; // height (7 bits)

            StringBuilder sb = new StringBuilder(width * height + height);

            int index = 1; // skip header
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x += 7)
                {
                    if (index >= packed.Length)
                        throw new ArgumentException("Packed string is too short for expected pixels.");

                    int bits = packed[index++];
                    bits >>= 1; // skip first bit (not used)

                    for (int i = 6; i >= 0; i--) // 7 pixels
                    {
                        int pos = x + (6 - i);
                        if (pos < width)
                        {
                            int colorIndex = (bits >> (i * 2)) & 0x3; // 2 bits per pixel
                            sb.Append((char)colorIndex);
                        }
                    }

                    // last bit is ignored (used by the encoder only to avoid reserved char space)
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }


        string ApplyTextureTint(string texture, int tint)
        {
            var sb = new StringBuilder();
            sb.Append(texture);
            sb.Replace($"{'\0'}", ""); // empty spaces

            switch (tint)
            {
                case 0x0:
                {
                    sb.Replace('\u0001', TEXTURE_ATLAS[0]);
                    sb.Replace('\u0002', TEXTURE_ATLAS[1]);
                    sb.Replace('\u0003', TEXTURE_ATLAS[2]);
                    return sb.ToString();
                }
                case 0x10:
                    sb.Replace('\u0001', TEXTURE_ATLAS[3]);
                    sb.Replace('\u0002', TEXTURE_ATLAS[4]);
                    sb.Replace('\u0003', TEXTURE_ATLAS[5]);
                    return sb.ToString();
                default: throw new Exception("Argument was Out of Range");
            }
        }

        public string GetTextureFromId(byte textureId)
        {
            var textureIndex = (byte)(textureId & 0xEF);
            if (textureIndex == 0)
                return string.Empty;

            string data;
            if (_textureCache.TryGetValue(textureId, out data))
                return data;

            string texture;
            if (!_textureCache.TryGetValue((byte)(textureIndex + 128), out texture))
            {
                try
                {
                    var seeker = 0; // 4 colors (-1 transparent) 2 teams, meaning 6 chars for colors)
                    var textureSize = 6;
                    for (int i = 0; i < textureIndex; i++)
                    {
                        seeker += textureSize;
                        //search for the texture of "textureIndex"
                        textureSize = (int)(((TEXTURE_ATLAS[seeker] >> 8) & 0x7F) / 7f + .5f) *
                            ((TEXTURE_ATLAS[seeker] >> 1) & 0x7F) + 1;
                    }

                    texture = UnpackTexture(TEXTURE_ATLAS.Substring(seeker, textureSize));
                    _textureCache[(byte)(textureIndex + 128)] = texture;
                }
                catch
                {
                    return string.Empty;
                }
            }

            data = ApplyTextureTint(texture, textureId & 0x10);
            _textureCache[textureId] = data;

            return data;
        }

        void BakeBoardVisual()
        {
            _viewBox = _script.ViewBox;

            var gridSize = Math.Min(_viewBox.Width, _viewBox.Height);

            BoardViewBox = new RectangleF((_viewBox.Center.X - gridSize / 2), (_viewBox.Center.Y - gridSize / 2),
                gridSize,
                gridSize);

            _boardSide = (int)Math.Sqrt(Board.Length);

            _gridCells = GetGridCells(BoardViewBox, _boardSide);
            if (_boardCellEntries == null || _boardCellEntries.Length != _gridCells.Length)
                _boardCellEntries = null;

            float cellSize = _gridCells[0].Width;
            var sb = new StringBuilder(GetTextureFromId(0x01));
            Vector2 measuredSize = _panel.MeasureStringInPixels(sb, "LcdMod_Monospace", 1);
            Scale = cellSize * .8f / measuredSize.X;
            Padding = cellSize * .1f;
        }


        static RectangleF[] GetGridCells(RectangleF frame, int gridSize)
        {
            RectangleF[] rectangles = new RectangleF[gridSize * gridSize];

            if (Math.Abs(frame.Width - frame.Height) > 0.01)
                throw new Exception("Grid size must be equal to grid size.");

            float cellWidth = frame.Width / gridSize;
            float cellHeight = frame.Height / gridSize;

            int index = 0;
            for (int row = 0; row < gridSize; row++)
            {
                for (int col = 0; col < gridSize; col++)
                {
                    float x = frame.X + col * cellWidth;
                    float y = frame.Y + row * cellHeight;

                    rectangles[index++] = new RectangleF(x, y, cellWidth, cellHeight);
                }
            }

            return rectangles;
        }

        void RenderBoardCell(List<MySprite> frame, int index)
        {
            var gridCell = GetGridCell(index);
            int row = index / _boardSide + 1;
            frame.Add(gridCell.ToSprite(_boardColors[(index + row) % 2]));

            RenderBoardCellOverlays(frame, index, gridCell);
            RenderPiece(frame, index, gridCell);
            RenderBoardCellCoordinates(frame, index, gridCell);
        }

        void RenderBoardCellOverlays(List<MySprite> frame, int index, RectangleF gridCell)
        {
            var color = _boardColors[2];

            if (_history.Any())
            {
                var last = _history.Last();
                if (PointToIndex(last.Origin) == index || PointToIndex(last.Target) == index)
                    frame.Add(gridCell.ToSprite(color));
            }

            if (SelectedTile != null && PointToIndex(SelectedTile.Value) == index)
                frame.Add(gridCell.ToSprite(color));

            if (_selectedTile != null && _availableMoves[_selectedTile.Value] != null)
            {
                // RenderBoardCell receives a board index. GetGridCell(index) already
                // applies the play-as-black visual flip, so do not feed this index
                // through BoardPointFromGridIndex here or the hint overlay gets
                // flipped a second time.
                var point = BoardPointFromBoardIndex(index);
                if (_availableMoves[_selectedTile.Value].Any(move => move.X == point.X && move.Y == point.Y))
                {
                    color = _showDangers && IsPositionInDanger(point, _selectedColor, Board)
                        ? _boardColors[4]
                        : _boardColors[3];
                    frame.Add(GetCell(point) != 0
                        ? gridCell.ToCircleHollow(_boardColors[3])
                        : gridCell.ToCircle(color));
                }
            }

            if (_checkPosition != null && _checkPosition.Value.X + _checkPosition.Value.Y * _boardSide == index)
                frame.Add(gridCell.ToSprite(_boardColors[4]));
        }

        void RenderPiece(List<MySprite> frame, int index, RectangleF grid)
        {
            var cell = Board[index];
            if (cell == 0)
                return;

            var data = GetTextureFromId(cell);

            frame.Add(new MySprite(SpriteType.TEXT, data,
                new Vector2(grid.Center.X, grid.Position.Y + Padding),
                fontId: "LcdMod_Monospace", rotation: Scale));
        }

        void RenderBoardCellCoordinates(List<MySprite> frame, int index, RectangleF gridCell)
        {
            // index is a board index. Labels must be placed according to the visual
            // index after the play-as-black flip, but the text itself should still
            // describe the chess coordinate of this board square.
            var visualIndex = BoardIndexToVisualIndex(index);
            var boardPoint = BoardPointFromBoardIndex(index);
            var colorOffset = _playingAsBlack ? 1 : 0;

            if (visualIndex % _boardSide == 0)
            {
                int displayRow = _boardSide - boardPoint.Y;
                frame.Add(new MySprite(SpriteType.TEXT, displayRow.ToString(),
                    gridCell.Position + new Vector2(Padding, 0), null,
                    _boardColors[(displayRow + colorOffset) % 2], rotation: Scale * 8));
            }

            if (visualIndex >= _boardSide * (_boardSide - 1))
            {
                var column = (char)('a' + boardPoint.X);

                frame.Add(new MySprite(SpriteType.TEXT, column.ToString(),
                    new Vector2(gridCell.Right - Padding, gridCell.Bottom - 3 * Padding) -
                    Padding / 2, null,
                    _boardColors[(column + colorOffset) % 2], rotation: Scale * 8));
            }
        }

        public readonly byte[] Board = new byte[64];

        readonly List<ChessMoveRecord> _history = new List<ChessMoveRecord>();

        RectangleF[] _gridCells;
        RectangleControl[] _boardCellEntries;
        readonly List<RectangleControl> _overlayControlEntries = new List<RectangleControl>();

        IMyTextSurface _panel;
        InteractiveSurfaceScript _script;

        int _currentMove;

        string _historyText = string.Empty;

        public float Scale;
        public float Padding;

        int _boardSide;

        Point? _selectedTile;

        Point? SelectedTile
        {
            get { return _selectedTile; }
            set
            {
                _selectedTile = value;

                if (value == null)
                {
                    _selectedColor = PieceColor.None;
                }
                else
                {
                    var cell = Board[PointToIndex(value.Value)];
                    _selectedColor = GetColor(cell);
                }
            }
        }

        PieceColor _selectedColor = PieceColor.None;

        public ChessGame(IMyTextSurface panel, InteractiveSurfaceScript script)
        {
            _panel = panel;
            _script = script;
            ReloadProgram();
            SetBot(_selectedBot);
        }

        void BuildGlobalMenu()
        {
            var selected = LocHelper.GetLoc("LcdMod_Selected");
            _script.SetGlobalMenu(
                new GlobalMenuEntry("LcdMod_Chess", new List<GlobalMenuEntry>
                {
                    new GlobalMenuEntry("LcdMod_NewGame", (ctx, sender) => NewGame()),
                    new GlobalMenuEntry("LcdMod_Export", (ctx, sender) => ExportPgnData()),
                    new GlobalMenuEntry(_playingAsBlack ? "LcdMod_PlayAsWhite" : "LcdMod_PlayAsBlack",
                        (ctx, sender) => SwitchSide()),
                    new GlobalMenuEntry(_showDangers ? "LcdMod_HideDangerousTiles" : "LcdMod_ShowDangerousTiles",
                        (ctx, sender) => SwitchDanger()),
                    new GlobalMenuEntry("Difficulty", new List<GlobalMenuEntry>
                    {
                        new GlobalMenuEntry(
                            LocHelper.GetLoc("BlockComboBoxValue_TextPanelShowTextNone") + (_selectedBot == ChessBotSelection.None ? selected : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.None); }),
                        new GlobalMenuEntry(
                            LocHelper.GetLoc("DifficultyEasy") + " - WhateverBot " +
                            (_selectedBot == ChessBotSelection.WhateverBot ? selected : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.WhateverBot); }),
                        new GlobalMenuEntry(
                            LocHelper.GetLoc("DifficultyNormal") + " - Squeedo " +
                            (_selectedBot == ChessBotSelection.Squeedo ? selected : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.Squeedo); }),
                        new GlobalMenuEntry(
                            LocHelper.GetLoc("DifficultyHard") + " - Boychesser " +
                            (_selectedBot == ChessBotSelection.Boychesser ? selected : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.Boychesser); }),
                    })
                })
            );
        }

        void ExportPgnData()
        {
            try
            {
                var data = Export();
                var name = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss") + "-Chess-pgn-export.txt";
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(name, typeof(ChessGame)))
                {
                    writer.Write(data);
                    writer.Flush();
                    writer.Close();
                }

                string path = Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath, "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName, name);

                if (path.Contains(
                        "steamuser")) // running on Proton *most likely* so format the path as if the game was running on Linux
                {
                    var basePath =
                        MyAPIGateway.Utilities.GamePaths.ContentPath.Replace("\\common\\SpaceEngineers\\Content", "");
                    path = Path.Combine(basePath, "compatdata", "244850", "pfx", "drive_c", path.Substring(3))
                        .Replace("\\", "/").Substring(2);
                }

                MyAPIGateway.Utilities.ShowMessage($"Pgn exported to", path);
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification("Something when wrong while exporting PGN",
                    font: MyFontEnum.Red);
                ErrorHandlerHelper.LogError(e, typeof(ChessGame));
            }
        }

        void SwitchSide()
        {
            _playingAsBlack = !_playingAsBlack;
            NewGame();
            BuildGlobalMenu();
        }

        void SwitchDanger()
        {
            _showDangers = !_showDangers;
            BuildGlobalMenu();
        }

        void SetBot(ChessBotSelection difficulty)
        {
            if(_selectedBot == difficulty && _api != null)
                return;
            
            IChessBot bot;
            switch (difficulty)
            {
                case ChessBotSelection.WhateverBot:
                    bot = new Bot153();
                    break;
                case ChessBotSelection.Squeedo:
                    bot = new Bot253();
                    break;
                case ChessBotSelection.Boychesser:
                    bot = new Bot614();
                    break;
                default:
                    bot = null;
                    break;
            }


            _api = bot == null ? null : new ChessBotApi(this, bot);
            BuildGlobalMenu();

            if(_selectedBot == difficulty)
                return;

            _selectedBot = difficulty;
            Save();
        }

        void ReloadProgram()
        {
            Load();
            BakeBoardVisual();
            PopulateHistory();

            for (int x = 0; x < _boardSide; x++)
            for (int y = 0; y < _boardSide; y++)
                _availableMoves[new Point(x, y)] = new List<Point>();
        }

        ChessGameConfig BuildConfig()
        {
            return new ChessGameConfig
            {
                Board = Board,
                Move = _currentMove,
                Castling = (int)_availableCastling,
                History = ChessMoveRecord.SerializeList(_history),
                PlayingAsWhitePlayerId = _playingAsWhitePlayerId,
                PlayingAsBlackPlayerId = _playingAsBlackPlayerId,
                SessionId = _sessionId,
                SelectedBot = (int)_selectedBot,
                ShowDangers = _showDangers,
                PlayingAsBlack = _playingAsBlack
            };
        }

        public void Save()
        {
            _script.Config.SetCustomData(CUSTOM_DATA_KEY, MyAPIGateway.Utilities.SerializeToBinary(BuildConfig()));
            ConfigManager.Sync((IMyTerminalBlock)_script.Block, _script.ProviderConfig);
        }

        ChessGameConfig LoadConfig()
        {
            var data = _script.Config.GetCustomData(CUSTOM_DATA_KEY);
            if (data == null || data.Length == 0)
                throw new Exception("Missing chess config.");

            return MyAPIGateway.Utilities.SerializeFromBinary<ChessGameConfig>(data);
        }

        public void Load()
        {
            try
            {
                var config = LoadConfig();
                if (config == null)
                    throw new Exception("Missing chess config.");

                for (int i = 0; i < config.Board.Length; i++)
                    Board[i] = config.Board[i];

                _currentMove = config.Move;
                _availableCastling = (Castling)config.Castling;
                _playingAsWhitePlayerId = config.PlayingAsWhitePlayerId;
                _playingAsBlackPlayerId = config.PlayingAsBlackPlayerId;
                _sessionId = config.SessionId;
                _selectedBot = (ChessBotSelection)config.SelectedBot;
                _showDangers = config.ShowDangers;
                _playingAsBlack = config.PlayingAsBlack;

                var history = config.History ?? string.Empty;

                if (history.Length > 262144)
                    throw new Exception("Corrupted History.");

                _history.Clear();
                _history.AddRange(ChessMoveRecord.DeserializeList(history));

                _shoudRecalculateMoves = true;
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
            RebuildLayout();
        }

        void PopulateHistory()
        {
            _historyText = string.Empty;
            for (var index = 0; index < _history.Count; index++)
                _historyText = FormatHistoryLine(_history[index]) + "\n" + _historyText;
        }

        ChessBotApi _api;
        IEnumerator<bool> _botThinkCoroutine;
        bool _botThinkRunning;
        bool _botThinkFinished;
        Move _botThinkMove;
        Exception _botThinkException;

        public void Update()
        {
            HandleCoroutine();
            if (_api != null)
                HandleBotThinkCoroutine();
        }

        void HandleBotThinkCoroutine()
        {
            try
            {
                if (_botThinkCoroutine != null)
                {
                    if (_botThinkCoroutine?.MoveNext() ?? true)
                        return;

                    _botThinkCoroutine?.Dispose();
                    _botThinkCoroutine = null;
                    return;
                }

                if (ShouldStartBotThink())
                    _botThinkCoroutine = RunBotThinkCoroutine();
            }
            catch (Exception e)
            {
                _botThinkCoroutine?.Dispose();
                _botThinkCoroutine = null;
                ErrorHandlerHelper.LogError(e, nameof(ChessGame));
                throw;
            }
        }

        bool ShouldStartBotThink()
        {
            if (_api == null)
                return false;

            if (_botThinkRunning || _botThinkCoroutine != null)
                return false;

            if (_coroutine != null)
                return false;

            if (_overlayOverlay != null)
                return false;

            if (SelectedTile != null)
                return false;

            return IsBotTurn();
        }

        bool IsBotTurn()
        {
            var currentColor = (PieceColor)(_currentMove % 2);

            // _playingAsBlack means the local/human side is black, so the bot plays white.
            if (_playingAsBlack)
                return currentColor == PieceColor.White;

            // Default/local side is white, so the bot plays black.
            return currentColor == PieceColor.Black;
        }

        IEnumerator<bool> RunBotThinkCoroutine()
        {
            _botThinkRunning = true;
            _botThinkFinished = false;
            _botThinkException = null;
            _botThinkMove = ChessChallenge.API.Move.NullMove;

            // Build the API board snapshot on the main/game thread.
            // The parallel worker must not read live ChessGame fields directly.
            _api.PrepareBoard();

            MyAPIGateway.Parallel.Start(
                () =>
                {
                    try
                    {
                        // Give the original challenge bots a realistic amount of
                        // virtual clock. Bot614 in particular budgets a fraction of
                        // MillisecondsRemaining, so the old 1000ms default made it
                        // search for only a few dozen milliseconds.
                        _botThinkMove = _api.ThinkPrepared(10000);
                    }
                    catch (Exception e)
                    {
                        _botThinkException = e;
                    }
                },
                () => { _botThinkFinished = true; });

            while (!_botThinkFinished)
                yield return true;

            _botThinkRunning = false;

            // Reset the mutable API board from live game state on the main thread.
            // This mirrors the original API's use of a cloned search board without
            // reading game state from the parallel worker.
            if (_api != null && _api.Board != null)
                _api.Board.LoadFromGame();

            if (_botThinkException != null)
            {
                ErrorHandlerHelper.LogError(_botThinkException, nameof(ChessGame));
                yield break;
            }

            if (!_botThinkMove.IsNull)
            {
                if (IsBotMoveForCurrentSide(_botThinkMove))
                {
                    MakeChallengeMove(_botThinkMove);
                    if (_api != null && _api.Board != null)
                        _api.Board.LoadFromGame();
                    SelectedTile = null;
                }
                else
                {
                    LogHelper.LogInfo(
                        $"Rejected bot move for wrong side: {_botThinkMove}, " +
                        $"movePiece={_botThinkMove.MovePieceType}, " +
                        $"currentMove={_currentMove}, whiteToMove={(_currentMove % 2) == 0}");
                }
            }
        }

        bool IsBotMoveForCurrentSide(Move move)
        {
            if (move.IsNull)
                return false;

            var origin = ToGamePoint(move.StartSquare);
            var cell = GetCell(origin) ?? 0;

            if (cell == 0)
                return false;

            var currentColor = (PieceColor)(_currentMove % 2);
            return GetColor(cell) == currentColor;
        }

        List<MySprite> _sprites = new List<MySprite>();
        ChessBotSelection _selectedBot = 0;
        bool _shoudRecalculateMoves;

        public List<MySprite> GetSprites()
        {
            _sprites.Clear();

            if (_viewBox != _script.ViewBox)
                RebuildLayout();

            if (_overlayOverlay?.Disposed ?? false)
                _overlayOverlay = null;

            _overlayOverlay?.Render(_sprites);

            RebuildInteractiveEntries();
            return _sprites;
        }

        void RebuildLayout()
        {
            BakeBoardVisual();
            _overlayOverlay?.LayoutChanged();
        }

        void HandleCoroutine()
        {
            if (_shoudRecalculateMoves)
            {
                _coroutine?.Dispose();
                _coroutine = GeneratePathFind();
                _shoudRecalculateMoves = false;
            }

            if (_coroutine == null)
                return;

            if (_coroutine.MoveNext())
                return;

            _coroutine.Dispose();
            _coroutine = null;
        }

        void RebuildInteractiveEntries()
        {
            Interactive.Clear();

            if (_gridCells == null || _gridCells.Length == 0)
                return;

            EnsureBoardCellEntries();

            for (int index = 0; index < _gridCells.Length; index++)
            {
                bool canClick = IsBoardCellInteractive(index);
                var entry = _boardCellEntries[index];
                entry.SetRect(_gridCells[index]);
                entry.SetCursor(GetBoardCellCursor(index));
                entry.SetDataContext(index);
                entry.SetOnClick(canClick ? (Action<object, object>)ClickBoardCellFromEntry : null);
                entry.ClickSound = GetBoardCellClickSound(index);
                entry.SetVisible(true);
                Interactive.Add(entry);
            }

            if (_botThinkRunning || _botThinkCoroutine != null)
            {
                HideOverlayControlEntries();
                return;
            }

            if (_overlayOverlay == null)
            {
                HideOverlayControlEntries();
                return;
            }

            for (int index = 0; index < _overlayOverlay.Boxes.Count; index++)
            {
                var entry = GetOverlayControlEntry(index);
                entry.SetRect(_overlayOverlay.Boxes[index]);
                entry.SetCursor(CursorType.Hand);
                entry.SetDataContext(index);
                entry.SetOnClick(ClickControlBoxFromEntry);
                entry.SetVisible(true);
                Interactive.Add(entry);
            }

            for (int index = _overlayOverlay.Boxes.Count; index < _overlayControlEntries.Count; index++)
                _overlayControlEntries[index].SetVisible(false);

            if(_overlayOverlay.RectangleControl != null)
                Interactive.Add(_overlayOverlay.RectangleControl);
        }

        void EnsureBoardCellEntries()
        {
            if (_gridCells == null)
                return;

            if (_boardCellEntries != null && _boardCellEntries.Length == _gridCells.Length)
                return;

            _boardCellEntries = new RectangleControl[_gridCells.Length];
            for (int index = 0; index < _boardCellEntries.Length; index++)
            {
                _boardCellEntries[index] = new RectangleControl(_gridCells[index], CursorType.Default, index)
                {
                    CustomRender = delegate(ControlBase entry, ControlRenderContext context, List<MySprite> sprites)
                    {
                        RenderBoardCell(sprites, (int)entry.DataContext);
                    }
                };
            }
        }

        RectangleControl GetOverlayControlEntry(int index)
        {
            while (_overlayControlEntries.Count <= index)
            {
                _overlayControlEntries.Add(new RectangleControl(
                    default(RectangleF),
                    CursorType.Hand,
                    _overlayControlEntries.Count)
                {
                    CustomRender = delegate(ControlBase entry, ControlRenderContext context, List<MySprite> sprites)
                    {
                        _overlayOverlay?.RenderBox(sprites, (int)entry.DataContext);
                    }
                });
            }

            return _overlayControlEntries[index];
        }

        void HideOverlayControlEntries()
        {
            for (int index = 0; index < _overlayControlEntries.Count; index++)
                _overlayControlEntries[index].SetVisible(false);
        }

        void ClickBoardCellFromEntry(object value, object sender) => ClickBoardCell((int)value);

        void ClickControlBoxFromEntry(object value, object sender) => ClickControlBox((int)value);

        bool IsBoardCellInteractive(int index)
        {
            if (_coroutine != null || _botThinkRunning || _botThinkCoroutine != null)
                return false;

            if (_gridCells == null || index < 0 || index >= _gridCells.Length || IsGameOver)
                return false;

            var point = BoardPointFromGridIndex(index);
            var cell = GetCell(point) ?? 0;

            if (SelectedTile != null)
            {
                if (SelectedTile.Value.Equals(point))
                    return true;

                List<Point> selectedMoves;
                if (_availableMoves.TryGetValue(SelectedTile.Value, out selectedMoves) &&
                    selectedMoves != null &&
                    selectedMoves.Any(move => move.X == point.X && move.Y == point.Y))
                {
                    return true;
                }
            }

            return cell != 0;
        }

        CursorType GetBoardCellCursor(int index)
        {
            if (_coroutine != null || _botThinkRunning || _botThinkCoroutine != null)
                return CursorType.WaitCursor;

            if (_gridCells == null || index < 0 || index >= _gridCells.Length)
                return CursorType.Default; // Default is the arrow cursor in the current UI set.

            if (IsGameOver)
                return CursorType.Cross;

            var point = BoardPointFromGridIndex(index);
            var cell = GetCell(point) ?? 0;
            var currentColor = (PieceColor)(_currentMove % 2);

            if (SelectedTile != null)
            {
                if (SelectedTile.Value.Equals(point))
                    return CursorType.Hand;

                List<Point> selectedMoves;
                if (_availableMoves.TryGetValue(SelectedTile.Value, out selectedMoves) &&
                    selectedMoves != null &&
                    selectedMoves.Any(move => move.X == point.X && move.Y == point.Y))
                {
                    return CursorType.Hand;
                }

                if (cell != 0)
                {
                    var cellColor = GetColor(cell);
                    return cellColor == currentColor ? CursorType.Hand : CursorType.No;
                }

                return CursorType.Default;
            }

            if (cell == 0)
                return CursorType.Default;

            return GetColor(cell) == currentColor ? CursorType.Hand : CursorType.No;
        }

        MySoundPair GetBoardCellClickSound(int index)
        {
            if (_coroutine != null || _botThinkRunning || _botThinkCoroutine != null || IsGameOver)
                return AudioHelper.HudUnable;

            if (_gridCells == null || index < 0 || index >= _gridCells.Length)
                return AudioHelper.HudUnable;

            var point = BoardPointFromGridIndex(index);
            var cell = GetCell(point) ?? 0;
            var currentColor = (PieceColor)(_currentMove % 2);

            if (SelectedTile != null)
            {
                if (SelectedTile.Value.Equals(point))
                    return AudioHelper.HudClick;

                List<Point> selectedMoves;
                if (_availableMoves.TryGetValue(SelectedTile.Value, out selectedMoves) &&
                    selectedMoves != null &&
                    selectedMoves.Any(move => move.X == point.X && move.Y == point.Y))
                {
                    return AudioHelper.HudClick;
                }

                if (cell != 0)
                {
                    var cellColor = GetColor(cell);
                    return cellColor == currentColor ? AudioHelper.HudClick : AudioHelper.HudUnable;
                }

                return AudioHelper.HudUnable;
            }

            if (cell == 0)
                return AudioHelper.HudUnable;

            return GetColor(cell) == currentColor ? AudioHelper.HudClick : AudioHelper.HudUnable;
        }

        Point BoardPointFromGridIndex(int index)
        {
            return BoardPointFromBoardIndex(VisualIndexToBoardIndex(index));
        }

        void ClickControlBox(int index)
        {
            if (_overlayOverlay == null)
                return;

            if (index >= 0 && index < _overlayOverlay.Boxes.Count)
                _overlayOverlay.ClickBox(index);

            if (_overlayOverlay != null && !_overlayOverlay.Disposed)
                _overlayOverlay.Dispose();

            if (_overlayOverlay != null && _overlayOverlay.Disposed)
                _overlayOverlay = null;
        }

        void ClickBoardCell(int index)
        {
            if (IsGameOver)
            {
                GameOverMessage();
                return;
            }

            if (_overlayOverlay != null || _gridCells == null || index < 0 || index >= _gridCells.Length)
                return;

            if (_coroutine != null || _botThinkRunning || _botThinkCoroutine != null)
                return;

            var point = BoardPointFromGridIndex(index);

            if (TryExecuteActionAt(point) == ActionResult.Success)
                Save();

            SelectedTile = TrySelect(point);
        }

        public bool IsGameOver => !_availableMoves.Any(a => a.Value.Any());

        public void GameOverMessage()
        {
            _script.ShowMessageBox("Game Over!", "Play again?", "New Game", "Dismiss", (o, o1) => NewGame());
        }
    }
}
