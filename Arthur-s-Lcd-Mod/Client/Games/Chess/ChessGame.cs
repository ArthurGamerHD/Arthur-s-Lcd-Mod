using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChessChallenge.API;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Games.Chess.Enum;
using LcdMod.Client.Games.Chess.TinyChessChallenge.Bots;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Adk.Gui.Core.Input;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using GameSurfaceScript = LcdMod.Client.SurfaceScripts.GameSurfaceScript;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

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

        public IMyTextSurface Surface => _script.Surface;

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

        public GameSurfaceScript.GameEnum Id => GameSurfaceScript.GameEnum.Chess;

        readonly GameThemeContext _themeContext;

        public IVisualStyleScope StyleParent => _themeContext.StyleParent;

        public ResourceTree Resources => _themeContext.Resources;

        public bool IsDirty => _themeContext.IsDirty;

        public void MarkDirty()
        {
            _themeContext.MarkDirty();
        }


        void BakeBoardVisual()
        {
            _viewBox = _script.GameViewBox;

            var gridSize = Math.Min(_viewBox.Width, _viewBox.Height);

            BoardViewBox = new RectangleF((_viewBox.Center.X - gridSize / 2), (_viewBox.Center.Y - gridSize / 2),
                gridSize,
                gridSize);

            _boardSide = (int)Math.Sqrt(Board.Length);

            _gridCells = GetGridCells(BoardViewBox, _boardSide);

            float cellSize = _gridCells[0].Width;
            UpdatePieceScale(cellSize);
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

            MySprite sprite;
            if (TryCreatePieceSprite(cell, new Vector2(grid.Center.X, grid.Position.Y + Padding), out sprite))
                frame.Add(sprite);
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
        readonly List<GameRegion> _regions = new List<GameRegion>();

        IMyTextSurface _panel;
        GameSurfaceScript _script;

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

        public ChessGame(IMyTextSurface panel, GameSurfaceScript script)
        {
            _panel = panel;
            _script = script;
            _themeContext = new GameThemeContext(script);
            ReloadProgram();
            SetBot(_selectedBot);
        }

        void BuildGlobalMenu()
        {
            var selected = LocHelper.GetLoc(MOD_PREFIX + "Selected");
            _script.SetGlobalMenu(
                new GlobalMenuEntry(MOD_PREFIX + "Chess", new List<GlobalMenuEntry>
                {
                    new GlobalMenuEntry(MOD_PREFIX + "NewGame", (ctx, sender) => _script.ConfirmNewGame(NewGame)),
                    new GlobalMenuEntry(MOD_PREFIX + "Export", (ctx, sender) => ExportPgnData()),
                    new GlobalMenuEntry(_playingAsBlack ? MOD_PREFIX + "PlayAsWhite" : MOD_PREFIX + "PlayAsBlack",
                        (ctx, sender) => SwitchSide()),
                    new GlobalMenuEntry(_showDangers ? MOD_PREFIX + "HideDangerousTiles" : MOD_PREFIX + "ShowDangerousTiles",
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
            _script.GeneralComponent.SetCustomData(CUSTOM_DATA_KEY, MyAPIGateway.Utilities.SerializeToBinary(BuildConfig()));
            ConfigManager.Sync((IMyTerminalBlock)_script.Block, _script.ProviderConfig);
        }

        ChessGameConfig LoadConfig()
        {
            var data = _script.GeneralComponent.GetCustomData(CUSTOM_DATA_KEY);
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

            if (_viewBox != _script.GameViewBox)
                RebuildLayout();

            if (_overlayOverlay?.Disposed ?? false)
                _overlayOverlay = null;

            _overlayOverlay?.Render(_sprites);

            if (_gridCells != null)
                for (int index = 0; index < _gridCells.Length; index++)
                    RenderBoardCell(_sprites, index);
            if (_overlayOverlay != null && !IsBotThinking)
            {
                for (int index = 0; index < _overlayOverlay.Boxes.Count; index++)
                    _overlayOverlay.RenderBox(_sprites, index);
                if (_overlayOverlay.CoversBoard)
                    _overlayOverlay.Render(_sprites);
            }

            BuildRegions();
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

        bool IsBotThinking => _botThinkRunning || _botThinkCoroutine != null;

        void BuildRegions()
        {
            int count = 0;
            if (_gridCells != null)
            {
                for (int index = 0; index < _gridCells.Length; index++)
                {
                    var cell = Region(count++);
                    int captured = index;
                    cell.Bounds = _gridCells[index];
                    cell.Cursor = GetBoardCellCursor(index);
                    cell.Clicked = () => ClickBoardCellFromRegion(captured);
                    cell.SecondaryClicked = null;
                }
            }

            if (_overlayOverlay != null && !IsBotThinking)
            {
                for (int index = 0; index < _overlayOverlay.Boxes.Count; index++)
                {
                    var box = Region(count++);
                    int captured = index;
                    box.Bounds = _overlayOverlay.Boxes[index];
                    box.Cursor = PointerCursor.Hand;
                    box.Clicked = () => ClickControlBoxFromRegion(captured);
                    box.SecondaryClicked = null;
                }

                if (_overlayOverlay.CoversBoard)
                {
                    var cover = Region(count++);
                    cover.Bounds = BoardViewBox;
                    cover.Cursor = PointerCursor.No;
                    cover.Clicked = _overlayOverlay.ClickCover;
                    cover.SecondaryClicked = null;
                }
            }

            if (_regions.Count > count)
                _regions.RemoveRange(count, _regions.Count - count);
        }

        GameRegion Region(int index)
        {
            while (_regions.Count <= index)
                _regions.Add(new GameRegion());
            return _regions[index];
        }

        void ClickBoardCellFromRegion(int index)
        {
            _script.PlaySounds(GetBoardCellClickSound(index));
            if (IsBoardCellInteractive(index))
                ClickBoardCell(index);
        }

        void ClickControlBoxFromRegion(int index)
        {
            _script.PlaySounds(AudioHelper.HudClick);
            ClickControlBox(index);
        }

        public IReadOnlyList<GameRegion> Regions => _regions;

        public void PointerMoved(Vector2? position)
        {
        }

        public void PointerClicked(GamePointerButton button, Vector2 position)
        {
        }

        public PointerCursor CursorAt(Vector2 position) => PointerCursor.Default;

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

        PointerCursor GetBoardCellCursor(int index)
        {
            if (_coroutine != null || IsBotThinking)
                return PointerCursor.Wait;

            if (_gridCells == null || index < 0 || index >= _gridCells.Length)
                return PointerCursor.Default;

            if (IsGameOver)
                return PointerCursor.Cross;

            var point = BoardPointFromGridIndex(index);
            var cell = GetCell(point) ?? 0;
            var currentColor = (PieceColor)(_currentMove % 2);

            if (SelectedTile != null)
            {
                if (SelectedTile.Value.Equals(point))
                    return PointerCursor.DragMove;

                List<Point> selectedMoves;
                if (_availableMoves.TryGetValue(SelectedTile.Value, out selectedMoves) &&
                    selectedMoves != null &&
                    selectedMoves.Any(move => move.X == point.X && move.Y == point.Y))
                {
                    return PointerCursor.Hand;
                }
            }

            if (cell == 0)
                return PointerCursor.Default;

            return GetColor(cell) == currentColor ? PointerCursor.DragMove : PointerCursor.No;
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
            _script.OfferNewGame(NewGame);
        }

        public bool HasVisibleItems()
        {
            return true;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }
    }
}
