using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ChessChallenge.API;
using Graph.Apps.Abstract;
using Graph.Apps.Games.Chess.Enum;
using Graph.Apps.Games.Chess.TinyChessChallenge.Bots;
using Graph.Apps.Utility;
using Graph.Extensions;
using Graph.Helpers;
using Graph.System.Config;
using Graph.System.Controls;
using ProtoBuf;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public partial class ChessGame : IGame
    {
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
        readonly List<MySprite> _boardVisualCache = new List<MySprite>();

        public List<InteractiveEntry> Interactive { get; } = new List<InteractiveEntry>();
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

        void RenderPieces(List<MySprite> frame)
        {
            for (var index = 0; index < _gridCells.Length; index++)
            {
                var grid = GetGridCell(index);

                var cell = Board[index];
                if (cell == 0)
                    continue;

                var data = GetTextureFromId(cell);

                frame.Add(new MySprite(SpriteType.TEXT, data,
                    new Vector2(grid.Center.X, grid.Position.Y + Padding),
                    fontId: "Monospace", rotation: Scale));
            }
        }

        void RenderBoardOverlays(List<MySprite> frame)
        {
            var color = _boardColors[2];

            if (_history.Any())
            {
                var last = _history.Last();
                frame.Add(GetGridCell(PointToIndex(last.Item1)).ToSprite(color));
                frame.Add(GetGridCell(PointToIndex(last.Item2)).ToSprite(color));
            }

            if (SelectedTile != null)
            {
                frame.Add(GetGridCell(PointToIndex(SelectedTile.Value)).ToSprite(color));
            }

            if (_selectedTile != null && _availableMoves[_selectedTile.Value] != null)
            {
                foreach (var move in _availableMoves[_selectedTile.Value])
                {
                    color = _showDangers && IsPositionInDanger(move, _selectedColor, Board)
                        ? _boardColors[4]
                        : _boardColors[3];
                    var gridCell = GetGridCell(move.X + move.Y * _boardSide);
                    frame.Add(GetCell(move) != 0
                        ? gridCell.ToCircleHollow(_boardColors[3])
                        : gridCell.ToCircle(color));
                }
            }

            if (_checkPosition != null)
            {
                color = _boardColors[4];
                frame.Add(GetGridCell(_checkPosition.Value.X + _checkPosition.Value.Y * _boardSide).ToSprite(color));
            }
        }

        void BakeBoardVisual()
        {
            _viewBox = _script.ViewBox;
            
            var gridSize = Math.Min(_viewBox.Width, _viewBox.Height) * .95f;

            BoardViewBox = new RectangleF((_viewBox.Center.X - gridSize / 2) , (_viewBox.Center.Y - gridSize / 2) , gridSize,
                gridSize);

            _boardSide = (int)Math.Sqrt(Board.Length);

            _gridCells = GetGridCells(BoardViewBox, _boardSide);

            float cellSize = _gridCells[0].Width;
            var sb = new StringBuilder(GetTextureFromId(0x01));
            Vector2 measuredSize = _panel.MeasureStringInPixels(sb, "Monospace", 1);
            Scale = cellSize * .8f / measuredSize.X;
            Padding = cellSize * .1f;

            _boardVisualCache.Clear();

            var row = 0;

            for (var index = 0; index < _gridCells.Length; index++)
            {
                var gridCell = _gridCells[index];
                if (index % 8 == 0)
                    row++;

                _boardVisualCache.Add(gridCell.ToSprite(_boardColors[(index + row) % 2]));
            }

            var step = _playingAsBlack ? 1 : -1;
            var colorOffset = _playingAsBlack ? 1 : 0;

            row = _playingAsBlack ? 1 : _boardSide;
            for (var index = 0; index < _gridCells.Length; index += _boardSide)
            {
                _boardVisualCache.Add(new MySprite(SpriteType.TEXT, row.ToString(),
                    _gridCells[index].Position + new Vector2(Padding, 0), null,
                    _boardColors[(row + colorOffset) % 2], rotation: Scale * 8));
                row += step;
            }

            var column = _playingAsBlack ? (char)('a' + _boardSide - 1) : 'a';

            for (var index = _boardSide * (_boardSide - 1); index < _gridCells.Length; index++)
            {
                _boardVisualCache.Add(new MySprite(SpriteType.TEXT, column.ToString(),
                    new Vector2(_gridCells[index].Right - Padding, _gridCells[index].Bottom - 3 * Padding) -
                    Padding / 2, null,
                    _boardColors[(column + colorOffset) % 2], rotation: Scale * 8));
                column -= (char)step;
            }
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

        void RenderBoard(List<MySprite> frame) => frame.AddRange(_boardVisualCache);

        public readonly byte[] Board = new byte[64];

        readonly List<MyTuple<Point, Point>> _history = new List<MyTuple<Point, Point>>();

        RectangleF[] _gridCells;

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
                    _selectedPieceType = PieceType.None;
                }
                else
                {
                    var cell = Board[PointToIndex(value.Value)];
                    _selectedColor = GetColor(cell);
                    _selectedPieceType = (PieceType)(cell & 0xEF);
                }
            }
        }

        PieceType _selectedPieceType = PieceType.None;

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
            _script.SetGlobalMenu(
                new GlobalMenuEntry("Game", new List<GlobalMenuEntry>
                {
                    new GlobalMenuEntry("New Game", (ctx, sender) => NewGame()),
                    new GlobalMenuEntry("Export", (ctx, sender) => ExportPgnData()),
                    new GlobalMenuEntry(_playingAsBlack ? "Play as White" : "Play as Black", (ctx, sender) => SwitchSide()),
                    new GlobalMenuEntry(_showDangers ? "Hide Dangerous Tiles" : "Show Dangerous Tiles", (ctx, sender) => SwitchDanger()),
                    new GlobalMenuEntry("Difficulty", new List<GlobalMenuEntry>
                    {
                        new GlobalMenuEntry("None" + (_selectedBot == ChessBotSelection.None ? " (selected)" : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.None); }),
                        new GlobalMenuEntry("Easy - WhateverBot" + (_selectedBot == ChessBotSelection.WhateverBot ? " (selected)" : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.WhateverBot); }),
                        new GlobalMenuEntry("Medium - Squeedo" + (_selectedBot == ChessBotSelection.Squeedo ? " (selected)" : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.Squeedo);}),
                        new GlobalMenuEntry("Hard - Boychesser" + (_selectedBot == ChessBotSelection.Boychesser ? " (selected)" : ""),
                            (ctx, sender) => { SetBot(ChessBotSelection.Boychesser);}),
                    })
                }),
                new GlobalMenuEntry("Help", new List<GlobalMenuEntry>
                {
                    new GlobalMenuEntry("About", (ctx, sender) => { }),
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

                string path = Path.Combine(MyAPIGateway.Utilities.GamePaths.UserDataPath,"Storage", MyAPIGateway.Utilities.GamePaths.ModScopeName, name);

                if (path.Contains("steamuser")) // running on Proton *most likely* so format the path as if the game was running on Linux
                {
                    var basePath = MyAPIGateway.Utilities.GamePaths.ContentPath.Replace("\\common\\SpaceEngineers\\Content", "");
                    path = Path.Combine(basePath,"compatdata","244850","pfx","drive_c", path.Substring(3)).Replace("\\", "/").Substring(2);
                }

                MyAPIGateway.Utilities.ShowMessage($"Pgn exported to", path);
            }
            catch (Exception e)
            {
                MyAPIGateway.Utilities.ShowNotification("Something when wrong while exporting PGN", font:MyFontEnum.Red);
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
            _selectedBot = difficulty;
            _api = bot == null ? null : new ChessBotApi(this, bot);
            BuildGlobalMenu();
        }

        void ReloadProgram()
        {
            Load();
            BakeBoardVisual();BakeBoardVisual();
            PopulateHistory();

            for (int x = 0; x < _boardSide; x++)
            for (int y = 0; y < _boardSide; y++)
                _availableMoves[new Point(x, y)] = new List<Point>();

            _coroutine = GeneratePathFind();
        }

        ChessGameConfig BuildConfig()
        {
            var history = new StringBuilder();
            foreach (var pointPair in _history)
                history.Append($"{pointPair.Item1.ToChar()}{pointPair.Item2.ToChar()}");

            return new ChessGameConfig
            {
                Board = Board,
                Move = _currentMove,
                Castling = (int)_availableCastling,
                History = history.ToString(),
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
            _script.Config.CustomData = MyAPIGateway.Utilities.SerializeToBinary(BuildConfig());
            ConfigManager.Sync((IMyTerminalBlock)_script.Block);
        }

        ChessGameConfig LoadConfig()
        {
            var data = _script.Config.CustomData;
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

                if (history.Length > 4096)
                    throw new Exception("Corrupted History.");

                _history.Clear();

                if (history.Length != 0 && history.Length % 2 == 0)
                {
                    for (var index = 0; index < history.Length; index += 2)
                        _history.Add(new MyTuple<Point, Point>(history[index].ToPoint(), history[index + 1].ToPoint()));
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, nameof(ChessGame));
                NewGame();
                Save();
            }
        }

        void PopulateHistory()
        {
            _historyText = string.Empty;
            for (var index = 0; index < _history.Count; index++)
                _historyText =
                    $"{index + 1}. {ToChessMove(_history[index].Item1)} > {ToChessMove(_history[index].Item2)}" + "\n" +
                    _historyText;
        }

        const int BOT_THINK_TIME_MS = 1000;

        ChessBotApi _api;
        IEnumerator<bool> _botThinkCoroutine;
        bool _botThinkRunning;
        bool _botThinkFinished;
        Move _botThinkMove;
        Exception _botThinkException;

        public void Tick()
        {
            HandleCoroutine();
            if(_api != null)
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
                        _botThinkMove = _api.ThinkPrepared(BOT_THINK_TIME_MS);
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
                    LogHelper.Log(
                        $"Rejected bot move for wrong side: {_botThinkMove}, " +
                        $"movePiece={_botThinkMove.MovePieceType}, " +
                        $"currentMove={_currentMove}, whiteToMove={(_currentMove % 2) == 0}");
                }
            }
        }

        bool IsBotMoveForCurrentSide(ChessChallenge.API.Move move)
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

        public List<MySprite> Render()
        {
            _sprites.Clear();
            
            if(_viewBox != _script.ViewBox)
                BakeBoardVisual();
            
            RenderBoard(_sprites);
            RenderBoardOverlays(_sprites);
            RenderPieces(_sprites);

            if (_overlayOverlay?.Disposed ?? false)
                _overlayOverlay = null;

            _overlayOverlay?.Render(_sprites);

            RebuildInteractiveEntries();
            return _sprites;
        }

        void HandleCoroutine()
        {
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

            for (int index = 0; index < _gridCells.Length; index++)
            {
                int capturedIndex = index;
                Interactive.Add(new InteractiveRectangleEntry(
                    _gridCells[index],
                    GetBoardCellCursor(index),
                    capturedIndex,
                    (value, sender) => ClickBoardCell((int)value))
                {
                    ClickSound = GetBoardCellClickSound(index),
                });
            }

            if (_botThinkRunning || _botThinkCoroutine != null)
                return;

            if (_overlayOverlay == null)
                return;

            for (int index = 0; index < _overlayOverlay.Boxes.Count; index++)
            {
                int capturedIndex = index;
                Interactive.Add(new InteractiveRectangleEntry(
                    _overlayOverlay.Boxes[index],
                    CursorType.Hand,
                    capturedIndex,
                    (value, sender) => ClickControlBox((int)value)));
            }
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
            var point = new Point(index % _boardSide, index / _boardSide);

            if (_playingAsBlack)
                point = new Point(_boardSide - point.X - 1, _boardSide - point.Y - 1);

            return point;
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

        void GameOverMessage()
        {
            _script.ShowMessageBox("Game Over!", "Play again?", "New Game", "Dismiss", (o, o1) => NewGame());
        }
    }
}