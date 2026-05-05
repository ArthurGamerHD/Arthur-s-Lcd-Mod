// This is an implementation of SebLague Tiny-Chess API
// Tiny-Chess-Godot is under MIT License

/*
MIT License

Copyright (c) 2023 Sebastian Lague

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

// the original API can be found at: https://github.com/SebLague/Tiny-Chess-Godot/tree/main/scripts/chess-challenge/API
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LcdMod.Client.Games.Chess.TinyChessChallenge;
using ChessGame = LcdMod.Client.Games.Chess.ChessGame;

// ReSharper disable once CheckNamespace
namespace ChessChallenge.API
{
    public interface IChessBot
    {
        Move Think(Board board, Timer timer);
    }

    public sealed class ChessBotApi
    {
        readonly ChessGame _game;
        readonly IChessBot _bot;
        readonly Board _board;

        public ChessBotApi(ChessGame game, IChessBot bot)
        {
            if (game == null)
                throw new ArgumentNullException(nameof(game));
            if (bot == null)
                throw new ArgumentNullException(nameof(bot));

            _game = game;
            _bot = bot;
            _board = new Board(_game);
        }

        public Move Think(int millisecondsRemaining = 1000)
        {
            return Think(new Timer(millisecondsRemaining));
        }

        public Move Think(Timer timer)
        {
            PrepareBoard();
            return ThinkPrepared(timer);
        }

        public void PrepareBoard()
        {
            // Must be called on the game/main thread. The shim board is a mutable
            // search board, and loading it reads live ChessGame state.
            _board.LoadFromGame();
        }

        public Move ThinkPrepared(int millisecondsRemaining = 1000)
        {
            return ThinkPrepared(new Timer(millisecondsRemaining));
        }

        public Move ThinkPrepared(Timer timer)
        {
            // Does not read live ChessGame state before Think. Safe to call from the
            // parallel worker after PrepareBoard() has built a snapshot on the main thread.
            //
            // Important: bots are allowed to mutate the Board during search using
            // MakeMove/UndoMove/ForceSkipTurn/UndoSkipTurn. If a bot exits early or
            // fails to perfectly unwind its search, the shared shim Board can remain
            // in a non-root state. Reset it after Think so the next turn starts from
            // the live ChessGame again.
            try
            {
                return _bot.Think(_board, timer ?? new Timer(1000));
            }
            finally
            {
                _board.LoadFromGame();
            }
        }

        public bool PlayBotMove(int millisecondsRemaining = 1000)
        {
            return PlayBotMove(new Timer(millisecondsRemaining));
        }

        public bool PlayBotMove(Timer timer)
        {
            var move = Think(timer);
            if (move.IsNull)
                return false;

            _game.MakeChallengeMove(move);
            _board.LoadFromGame();
            return true;
        }

        public Board Board => _board;

        public IChessBot Bot => _bot;
    }

    public sealed class Board
    {
        const int BOARD_SIZE = 64;

        readonly ChessGame _game;
        readonly PieceType[] _pieces = new PieceType[BOARD_SIZE];
        readonly bool[] _whitePieces = new bool[BOARD_SIZE];
        readonly List<Move> _gameMoveHistory = new List<Move>();
        readonly List<ulong> _repetitionHistory = new List<ulong>();
        readonly Stack<BoardState> _stateStack = new Stack<BoardState>();

        bool _whiteToMove;
        bool _whiteCastleKingSide;
        bool _whiteCastleQueenSide;
        bool _blackCastleKingSide;
        bool _blackCastleQueenSide;
        int _enPassantSquareIndex = -1;
        int _fiftyMoveCounter;
        int _plyCount;
        readonly string _gameStartFenString;

        sealed class BoardState
        {
            public PieceType[] Pieces;
            public bool[] WhitePieces;
            public bool WhiteToMove;
            public bool WhiteCastleKingSide;
            public bool WhiteCastleQueenSide;
            public bool BlackCastleKingSide;
            public bool BlackCastleQueenSide;
            public int EnPassantSquareIndex;
            public int FiftyMoveCounter;
            public int PlyCount;
            public int MoveHistoryCount;
            public int RepetitionHistoryCount;
        }

        public Board(ChessGame game)
        {
            if (game == null)
                throw new ArgumentNullException(nameof(game));

            _game = game;
            LoadFromGame();
            _gameStartFenString = GetFenString();
        }

        public void LoadFromGame()
        {
            for (int i = 0; i < BOARD_SIZE; i++)
            {
                var square = new Square(i);
                var piece = _game.GetChallengePiece(square);
                _pieces[i] = piece.PieceType;
                _whitePieces[i] = piece.IsWhite;
            }

            _whiteToMove = _game.IsChallengeWhiteToMove;
            _plyCount = _game.ChallengePlyCount;
            _fiftyMoveCounter = 0;
            _enPassantSquareIndex = -1;

            _whiteCastleKingSide = _game.HasChallengeKingsideCastleRight(true);
            _whiteCastleQueenSide = _game.HasChallengeQueensideCastleRight(true);
            _blackCastleKingSide = _game.HasChallengeKingsideCastleRight(false);
            _blackCastleQueenSide = _game.HasChallengeQueensideCastleRight(false);

            _gameMoveHistory.Clear();
            var existingHistory = _game.GetChallengeMoveHistory();
            if (existingHistory != null)
                _gameMoveHistory.AddRange(existingHistory);

            _repetitionHistory.Clear();
            _repetitionHistory.Add(ZobristKey);

            _stateStack.Clear();
        }

        public Move[] GetLegalMoves(bool capturesOnly = false)
        {
            var moves = new List<Move>();
            GeneratePseudoLegalMoves(moves, capturesOnly);

            for (int i = moves.Count - 1; i >= 0; i--)
            {
                var move = moves[i];
                MakeMoveInternal(move, recordState: true, recordHistory: false);

                bool illegal = IsKingInCheck(!IsWhiteToMove);

                RestoreState(_stateStack.Pop());

                if (illegal)
                    moves.RemoveAt(i);
            }

            return moves.ToArray();
        }

        void GeneratePseudoLegalMoves(List<Move> moves, bool capturesOnly)
        {
            for (int i = 0; i < BOARD_SIZE; i++)
            {
                if (_pieces[i] == PieceType.None || _whitePieces[i] != _whiteToMove)
                    continue;

                switch (_pieces[i])
                {
                    case PieceType.Pawn:
                        GeneratePawnMoves(i, moves, capturesOnly);
                        break;
                    case PieceType.Knight:
                        GenerateKnightMoves(i, moves, capturesOnly);
                        break;
                    case PieceType.Bishop:
                        GenerateSlidingMoves(i, moves, capturesOnly, BishopDirections);
                        break;
                    case PieceType.Rook:
                        GenerateSlidingMoves(i, moves, capturesOnly, RookDirections);
                        break;
                    case PieceType.Queen:
                        GenerateSlidingMoves(i, moves, capturesOnly, QueenDirections);
                        break;
                    case PieceType.King:
                        GenerateKingMoves(i, moves, capturesOnly);
                        break;
                }
            }
        }

        static readonly int[] RookDirections = { 1, -1, 8, -8 };
        static readonly int[] BishopDirections = { 9, -9, 7, -7 };
        static readonly int[] QueenDirections = { 1, -1, 8, -8, 9, -9, 7, -7 };
        static readonly int[] KnightOffsets = { 17, 15, 10, 6, -17, -15, -10, -6 };
        static readonly int[] KingOffsets = { 1, -1, 8, -8, 9, -9, 7, -7 };

        void GeneratePawnMoves(int from, List<Move> moves, bool capturesOnly)
        {
            bool white = _whitePieces[from];
            int direction = white ? 8 : -8;
            int startRank = white ? 1 : 6;
            int promotionRank = white ? 7 : 0;
            int fromRank = RankOf(from);
            int fromFile = FileOf(from);

            int oneForward = from + direction;
            if (!capturesOnly && IsValidSquare(oneForward) && _pieces[oneForward] == PieceType.None)
            {
                AddPawnMove(moves, from, oneForward, promotionRank);

                int twoForward = from + direction * 2;
                if (fromRank == startRank && IsValidSquare(twoForward) && _pieces[twoForward] == PieceType.None)
                    moves.Add(CreateMove(from, twoForward, PieceType.None, false, false));
            }

            int[] captureOffsets = white ? new[] { 7, 9 } : new[] { -9, -7 };
            for (int i = 0; i < captureOffsets.Length; i++)
            {
                int to = from + captureOffsets[i];
                if (!IsValidSquare(to))
                    continue;

                int fileDiff = Math.Abs(FileOf(to) - fromFile);
                if (fileDiff != 1)
                    continue;

                bool isEnPassant = to == _enPassantSquareIndex;
                bool isCapture = _pieces[to] != PieceType.None && _whitePieces[to] != white;

                if (isCapture || isEnPassant)
                    AddPawnMove(moves, from, to, promotionRank, isEnPassant);
            }
        }

        void AddPawnMove(List<Move> moves, int from, int to, int promotionRank, bool isEnPassant = false)
        {
            if (RankOf(to) == promotionRank)
            {
                moves.Add(CreateMove(from, to, PieceType.Queen, isEnPassant, false));
                moves.Add(CreateMove(from, to, PieceType.Rook, isEnPassant, false));
                moves.Add(CreateMove(from, to, PieceType.Bishop, isEnPassant, false));
                moves.Add(CreateMove(from, to, PieceType.Knight, isEnPassant, false));
                return;
            }

            moves.Add(CreateMove(from, to, PieceType.None, isEnPassant, false));
        }

        void GenerateKnightMoves(int from, List<Move> moves, bool capturesOnly)
        {
            int fromFile = FileOf(from);
            int fromRank = RankOf(from);
            bool white = _whitePieces[from];

            for (int i = 0; i < KnightOffsets.Length; i++)
            {
                int to = from + KnightOffsets[i];
                if (!IsValidSquare(to))
                    continue;

                int fileDiff = Math.Abs(FileOf(to) - fromFile);
                int rankDiff = Math.Abs(RankOf(to) - fromRank);
                if (!((fileDiff == 1 && rankDiff == 2) || (fileDiff == 2 && rankDiff == 1)))
                    continue;

                TryAddMove(moves, from, to, white, capturesOnly);
            }
        }

        void GenerateSlidingMoves(int from, List<Move> moves, bool capturesOnly, int[] directions)
        {
            bool white = _whitePieces[from];

            for (int d = 0; d < directions.Length; d++)
            {
                int direction = directions[d];
                int current = from;

                while (true)
                {
                    int next = current + direction;
                    if (!IsValidSquare(next) || Wraps(current, next, direction))
                        break;

                    if (_pieces[next] == PieceType.None)
                    {
                        if (!capturesOnly)
                            moves.Add(CreateMove(from, next, PieceType.None, false, false));

                        current = next;
                        continue;
                    }

                    if (_whitePieces[next] != white)
                        moves.Add(CreateMove(from, next, PieceType.None, false, false));

                    break;
                }
            }
        }

        void GenerateKingMoves(int from, List<Move> moves, bool capturesOnly)
        {
            int fromFile = FileOf(from);
            int fromRank = RankOf(from);
            bool white = _whitePieces[from];

            for (int i = 0; i < KingOffsets.Length; i++)
            {
                int to = from + KingOffsets[i];
                if (!IsValidSquare(to))
                    continue;

                if (Math.Abs(FileOf(to) - fromFile) > 1 || Math.Abs(RankOf(to) - fromRank) > 1)
                    continue;

                TryAddMove(moves, from, to, white, capturesOnly);
            }

            if (capturesOnly || IsKingInCheck(white))
                return;

            if (white && from == 4)
            {
                if (_whiteCastleKingSide &&
                    _pieces[5] == PieceType.None &&
                    _pieces[6] == PieceType.None &&
                    !IsSquareAttacked(5, false) &&
                    !IsSquareAttacked(6, false))
                {
                    moves.Add(CreateMove(4, 6, PieceType.None, false, true));
                }

                if (_whiteCastleQueenSide &&
                    _pieces[3] == PieceType.None &&
                    _pieces[2] == PieceType.None &&
                    _pieces[1] == PieceType.None &&
                    !IsSquareAttacked(3, false) &&
                    !IsSquareAttacked(2, false))
                {
                    moves.Add(CreateMove(4, 2, PieceType.None, false, true));
                }
            }
            else if (!white && from == 60)
            {
                if (_blackCastleKingSide &&
                    _pieces[61] == PieceType.None &&
                    _pieces[62] == PieceType.None &&
                    !IsSquareAttacked(61, true) &&
                    !IsSquareAttacked(62, true))
                {
                    moves.Add(CreateMove(60, 62, PieceType.None, false, true));
                }

                if (_blackCastleQueenSide &&
                    _pieces[59] == PieceType.None &&
                    _pieces[58] == PieceType.None &&
                    _pieces[57] == PieceType.None &&
                    !IsSquareAttacked(59, true) &&
                    !IsSquareAttacked(58, true))
                {
                    moves.Add(CreateMove(60, 58, PieceType.None, false, true));
                }
            }
        }

        void TryAddMove(List<Move> moves, int from, int to, bool white, bool capturesOnly)
        {
            if (_pieces[to] == PieceType.None)
            {
                if (!capturesOnly)
                    moves.Add(CreateMove(from, to, PieceType.None, false, false));

                return;
            }

            if (_whitePieces[to] != white)
                moves.Add(CreateMove(from, to, PieceType.None, false, false));
        }

        Move CreateMove(int from, int to, PieceType promotionPieceType, bool isEnPassant, bool isCastles)
        {
            var capture = PieceType.None;
            if (isEnPassant)
            {
                int capturedPawnSquare = _whitePieces[from] ? to - 8 : to + 8;
                if (IsValidSquare(capturedPawnSquare))
                    capture = _pieces[capturedPawnSquare];
            }
            else
            {
                capture = _pieces[to];
            }

            return new Move(
                new Square(from),
                new Square(to),
                _pieces[from],
                capture,
                promotionPieceType,
                isEnPassant,
                isCastles);
        }

        public void MakeMove(Move move)
        {
            if (!move.IsNull)
                MakeMoveInternal(move, recordState: true, recordHistory: true);
        }

        void MakeMoveInternal(Move move, bool recordState, bool recordHistory)
        {
            if (recordState)
                _stateStack.Push(CaptureState());

            int from = move.StartSquare.Index;
            int to = move.TargetSquare.Index;
            PieceType movingPiece = _pieces[from];
            bool movingWhite = _whitePieces[from];
            bool isCapture = _pieces[to] != PieceType.None || move.IsEnPassant;

            _enPassantSquareIndex = -1;

            _pieces[from] = PieceType.None;
            _whitePieces[from] = false;

            if (move.IsEnPassant)
            {
                int capturedPawnSquare = movingWhite ? to - 8 : to + 8;
                if (IsValidSquare(capturedPawnSquare))
                {
                    _pieces[capturedPawnSquare] = PieceType.None;
                    _whitePieces[capturedPawnSquare] = false;
                }
            }

            if (move.IsCastles && movingPiece == PieceType.King)
            {
                if (to == 6)
                    MoveRookForCastles(7, 5);
                else if (to == 2)
                    MoveRookForCastles(0, 3);
                else if (to == 62)
                    MoveRookForCastles(63, 61);
                else if (to == 58)
                    MoveRookForCastles(56, 59);
            }

            _pieces[to] = move.PromotionPieceType != PieceType.None ? move.PromotionPieceType : movingPiece;
            _whitePieces[to] = movingWhite;

            UpdateCastleRightsAfterMove(from, to, movingPiece);

            if (movingPiece == PieceType.Pawn && Math.Abs(to - from) == 16)
                _enPassantSquareIndex = (from + to) / 2;

            _fiftyMoveCounter = movingPiece == PieceType.Pawn || isCapture ? 0 : _fiftyMoveCounter + 1;
            _whiteToMove = !_whiteToMove;
            _plyCount++;

            if (recordHistory)
            {
                _gameMoveHistory.Add(move);
                _repetitionHistory.Add(ZobristKey);
            }
        }

        void MoveRookForCastles(int rookFrom, int rookTo)
        {
            _pieces[rookTo] = _pieces[rookFrom];
            _whitePieces[rookTo] = _whitePieces[rookFrom];
            _pieces[rookFrom] = PieceType.None;
            _whitePieces[rookFrom] = false;
        }

        void UpdateCastleRightsAfterMove(int from, int to, PieceType movingPiece)
        {
            if (movingPiece == PieceType.King)
            {
                if (_whitePieces[to])
                {
                    _whiteCastleKingSide = false;
                    _whiteCastleQueenSide = false;
                }
                else
                {
                    _blackCastleKingSide = false;
                    _blackCastleQueenSide = false;
                }
            }

            if (from == 0 || to == 0)
                _whiteCastleQueenSide = false;
            if (from == 7 || to == 7)
                _whiteCastleKingSide = false;
            if (from == 56 || to == 56)
                _blackCastleQueenSide = false;
            if (from == 63 || to == 63)
                _blackCastleKingSide = false;
        }

        public void UndoMove(Move move)
        {
            if (_stateStack.Count == 0)
                return;

            RestoreState(_stateStack.Pop());
        }

        public bool TrySkipTurn()
        {
            if (IsInCheck())
                return false;

            ForceSkipTurn();
            return true;
        }

        public void ForceSkipTurn()
        {
            _stateStack.Push(CaptureState());
            _enPassantSquareIndex = -1;
            _whiteToMove = !_whiteToMove;
            _plyCount++;
            _gameMoveHistory.Add(Move.NullMove);
            _repetitionHistory.Add(ZobristKey);
        }

        public void UndoSkipTurn()
        {
            if (_stateStack.Count == 0)
                return;

            RestoreState(_stateStack.Pop());
        }

        BoardState CaptureState()
        {
            return new BoardState
            {
                Pieces = (PieceType[])_pieces.Clone(),
                WhitePieces = (bool[])_whitePieces.Clone(),
                WhiteToMove = _whiteToMove,
                WhiteCastleKingSide = _whiteCastleKingSide,
                WhiteCastleQueenSide = _whiteCastleQueenSide,
                BlackCastleKingSide = _blackCastleKingSide,
                BlackCastleQueenSide = _blackCastleQueenSide,
                EnPassantSquareIndex = _enPassantSquareIndex,
                FiftyMoveCounter = _fiftyMoveCounter,
                PlyCount = _plyCount,
                MoveHistoryCount = _gameMoveHistory.Count,
                RepetitionHistoryCount = _repetitionHistory.Count
            };
        }

        void RestoreState(BoardState state)
        {
            Array.Copy(state.Pieces, _pieces, BOARD_SIZE);
            Array.Copy(state.WhitePieces, _whitePieces, BOARD_SIZE);
            _whiteToMove = state.WhiteToMove;
            _whiteCastleKingSide = state.WhiteCastleKingSide;
            _whiteCastleQueenSide = state.WhiteCastleQueenSide;
            _blackCastleKingSide = state.BlackCastleKingSide;
            _blackCastleQueenSide = state.BlackCastleQueenSide;
            _enPassantSquareIndex = state.EnPassantSquareIndex;
            _fiftyMoveCounter = state.FiftyMoveCounter;
            _plyCount = state.PlyCount;

            while (_gameMoveHistory.Count > state.MoveHistoryCount)
                _gameMoveHistory.RemoveAt(_gameMoveHistory.Count - 1);

            while (_repetitionHistory.Count > state.RepetitionHistoryCount)
                _repetitionHistory.RemoveAt(_repetitionHistory.Count - 1);
        }

        public bool IsInCheck()
        {
            return IsKingInCheck(_whiteToMove);
        }

        bool IsKingInCheck(bool white)
        {
            int kingSquare = FindKingSquare(white);
            return kingSquare >= 0 && IsSquareAttacked(kingSquare, !white);
        }

        int FindKingSquare(bool white)
        {
            for (int i = 0; i < BOARD_SIZE; i++)
            {
                if (_pieces[i] == PieceType.King && _whitePieces[i] == white)
                    return i;
            }

            return -1;
        }

        bool IsSquareAttacked(int square, bool byWhite)
        {
            int file = FileOf(square);

            int pawnA = square + (byWhite ? -7 : 7);
            int pawnB = square + (byWhite ? -9 : 9);
            if (IsValidSquare(pawnA) && Math.Abs(FileOf(pawnA) - file) == 1 &&
                _pieces[pawnA] == PieceType.Pawn && _whitePieces[pawnA] == byWhite)
                return true;
            if (IsValidSquare(pawnB) && Math.Abs(FileOf(pawnB) - file) == 1 &&
                _pieces[pawnB] == PieceType.Pawn && _whitePieces[pawnB] == byWhite)
                return true;

            for (int i = 0; i < KnightOffsets.Length; i++)
            {
                int from = square + KnightOffsets[i];
                if (IsValidSquare(from) &&
                    Math.Abs(FileOf(from) - file) <= 2 &&
                    _pieces[from] == PieceType.Knight &&
                    _whitePieces[from] == byWhite)
                    return true;
            }

            if (IsAttackedBySlidingPiece(square, byWhite, RookDirections, PieceType.Rook, PieceType.Queen))
                return true;

            if (IsAttackedBySlidingPiece(square, byWhite, BishopDirections, PieceType.Bishop, PieceType.Queen))
                return true;

            for (int i = 0; i < KingOffsets.Length; i++)
            {
                int from = square + KingOffsets[i];
                if (IsValidSquare(from) &&
                    Math.Abs(FileOf(from) - file) <= 1 &&
                    _pieces[from] == PieceType.King &&
                    _whitePieces[from] == byWhite)
                    return true;
            }

            return false;
        }

        bool IsAttackedBySlidingPiece(int square, bool byWhite, int[] directions, PieceType a, PieceType b)
        {
            for (int i = 0; i < directions.Length; i++)
            {
                int direction = directions[i];
                int current = square;

                while (true)
                {
                    int next = current + direction;
                    if (!IsValidSquare(next) || Wraps(current, next, direction))
                        break;

                    if (_pieces[next] != PieceType.None)
                    {
                        if (_whitePieces[next] == byWhite &&
                            (_pieces[next] == a || _pieces[next] == b))
                            return true;

                        break;
                    }

                    current = next;
                }
            }

            return false;
        }

        public bool IsInCheckmate()
        {
            return IsInCheck() && GetLegalMoves().Length == 0;
        }

        public bool IsDraw()
        {
            return IsInStalemate() || IsFiftyMoveDraw() || IsRepeatedPosition() || IsInsufficientMaterial();
        }

        public bool IsInStalemate()
        {
            return !IsInCheck() && GetLegalMoves().Length == 0;
        }

        public bool IsFiftyMoveDraw()
        {
            return _fiftyMoveCounter >= 100;
        }

        public bool IsRepeatedPosition()
        {
            ulong key = ZobristKey;
            int count = 0;

            for (int i = 0; i < _repetitionHistory.Count; i++)
            {
                if (_repetitionHistory[i] == key)
                    count++;
            }

            return count >= 3;
        }

        public bool IsInsufficientMaterial()
        {
            int whiteMinor = 0;
            int blackMinor = 0;

            for (int i = 0; i < BOARD_SIZE; i++)
            {
                switch (_pieces[i])
                {
                    case PieceType.Pawn:
                    case PieceType.Rook:
                    case PieceType.Queen:
                        return false;
                    case PieceType.Bishop:
                    case PieceType.Knight:
                        if (_whitePieces[i])
                            whiteMinor++;
                        else
                            blackMinor++;
                        break;
                }
            }

            return whiteMinor <= 1 && blackMinor <= 1;
        }

        public bool HasKingsideCastleRight(bool white)
        {
            return white ? _whiteCastleKingSide : _blackCastleKingSide;
        }

        public bool HasQueensideCastleRight(bool white)
        {
            return white ? _whiteCastleQueenSide : _blackCastleQueenSide;
        }

        public Square GetKingSquare(bool white)
        {
            int square = FindKingSquare(white);
            return new Square(square >= 0 ? square : 0);
        }

        public Piece GetPiece(Square square)
        {
            if (!IsValidSquare(square.Index) || _pieces[square.Index] == PieceType.None)
                return new Piece(PieceType.None, false, square);

            return new Piece(_pieces[square.Index], _whitePieces[square.Index], square);
        }

        public PieceList GetPieceList(PieceType pieceType, bool white)
        {
            return new PieceList(this, pieceType, white);
        }

        public PieceList[] GetAllPieceLists()
        {
            var lists = new List<PieceList>(12);

            for (int color = 0; color < 2; color++)
            {
                bool white = color == 0;
                lists.Add(new PieceList(this, PieceType.Pawn, white));
                lists.Add(new PieceList(this, PieceType.Knight, white));
                lists.Add(new PieceList(this, PieceType.Bishop, white));
                lists.Add(new PieceList(this, PieceType.Rook, white));
                lists.Add(new PieceList(this, PieceType.Queen, white));
                lists.Add(new PieceList(this, PieceType.King, white));
            }

            return lists.ToArray();
        }

        public bool SquareIsAttackedByOpponent(Square square)
        {
            return IsSquareAttacked(square.Index, !_whiteToMove);
        }

        public string GetFenString()
        {
            var sb = new StringBuilder();

            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;

                for (int file = 0; file < 8; file++)
                {
                    int index = file + rank * 8;

                    if (_pieces[index] == PieceType.None)
                    {
                        empty++;
                        continue;
                    }

                    if (empty > 0)
                    {
                        sb.Append(empty);
                        empty = 0;
                    }

                    sb.Append(ToFenPiece(index));
                }

                if (empty > 0)
                    sb.Append(empty);

                if (rank != 0)
                    sb.Append('/');
            }

            sb.Append(_whiteToMove ? " w " : " b ");
            sb.Append(BuildFenCastlingRights());
            sb.Append(' ');
            sb.Append(_enPassantSquareIndex >= 0 ? new Square(_enPassantSquareIndex).Name : "-");
            sb.Append(' ');
            sb.Append(_fiftyMoveCounter);
            sb.Append(' ');
            sb.Append((_plyCount / 2) + 1);
            return sb.ToString();
        }

        char ToFenPiece(int index)
        {
            char c;
            switch (_pieces[index])
            {
                case PieceType.Pawn:
                    c = 'p';
                    break;
                case PieceType.Rook:
                    c = 'r';
                    break;
                case PieceType.Knight:
                    c = 'n';
                    break;
                case PieceType.Bishop:
                    c = 'b';
                    break;
                case PieceType.Queen:
                    c = 'q';
                    break;
                case PieceType.King:
                    c = 'k';
                    break;
                default:
                    c = ' ';
                    break;
            }

            return _whitePieces[index] ? char.ToUpper(c) : c;
        }

        string BuildFenCastlingRights()
        {
            var sb = new StringBuilder();

            if (_whiteCastleKingSide)
                sb.Append('K');
            if (_whiteCastleQueenSide)
                sb.Append('Q');
            if (_blackCastleKingSide)
                sb.Append('k');
            if (_blackCastleQueenSide)
                sb.Append('q');

            return sb.Length == 0 ? "-" : sb.ToString();
        }

        public ulong GetPieceBitboard(PieceType pieceType, bool white)
        {
            ulong bitboard = 0UL;

            for (int i = 0; i < BOARD_SIZE; i++)
            {
                if (_pieces[i] == pieceType && _whitePieces[i] == white)
                    bitboard |= 1UL << i;
            }

            return bitboard;
        }

        public ulong WhitePiecesBitboard => GetColorBitboard(true);

        public ulong BlackPiecesBitboard => GetColorBitboard(false);

        ulong GetColorBitboard(bool white)
        {
            ulong bitboard = 0UL;

            for (int i = 0; i < BOARD_SIZE; i++)
            {
                if (_pieces[i] != PieceType.None && _whitePieces[i] == white)
                    bitboard |= 1UL << i;
            }

            return bitboard;
        }

        public ulong AllPiecesBitboard => WhitePiecesBitboard | BlackPiecesBitboard;

        public bool IsWhiteToMove => _whiteToMove;

        public int PlyCount => _plyCount;

        public int FiftyMoveCounter => _fiftyMoveCounter;

        public ulong ZobristKey
        {
            get
            {
                unchecked
                {
                    ulong hash = 1469598103934665603UL;

                    for (int i = 0; i < BOARD_SIZE; i++)
                    {
                        hash ^= (ulong)((int)_pieces[i] + 1);
                        hash *= 1099511628211UL;
                        hash ^= _whitePieces[i] ? 1UL : 0UL;
                        hash *= 1099511628211UL;
                    }

                    hash ^= _whiteToMove ? 1UL : 0UL;
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(_whiteCastleKingSide ? 1 : 0);
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(_whiteCastleQueenSide ? 1 : 0);
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(_blackCastleKingSide ? 1 : 0);
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(_blackCastleQueenSide ? 1 : 0);
                    hash *= 1099511628211UL;
                    hash ^= (ulong)(_enPassantSquareIndex + 1);
                    hash *= 1099511628211UL;

                    return hash;
                }
            }
        }

        public ulong[] GameRepetitionHistory => _repetitionHistory.ToArray();

        public string GameStartFenString => _gameStartFenString;

        public Move[] GameMoveHistory => _gameMoveHistory.ToArray();

        public string CreateDiagram(bool blackAtTop = true, bool includeFen = true, bool includeZobristKey = true, Square? highlightedSquare = null)
        {
            var result = new StringBuilder();

            for (int y = 0; y < 8; y++)
            {
                int rankIndex = blackAtTop ? 7 - y : y;
                result.AppendLine("+---+---+---+---+---+---+---+---+");

                for (int x = 0; x < 8; x++)
                {
                    int fileIndex = blackAtTop ? x : 7 - x;
                    var square = new Square(fileIndex, rankIndex);
                    var piece = GetPiece(square);
                    char symbol = GetPieceSymbol(piece);

                    if (highlightedSquare.HasValue && highlightedSquare.Value == square)
                        result.Append("|(").Append(symbol).Append(")");
                    else
                        result.Append("| ").Append(symbol).Append(" ");

                    if (x == 7)
                        result.AppendLine("| " + (rankIndex + 1));
                }

                if (y == 7)
                {
                    result.AppendLine("+---+---+---+---+---+---+---+---+");
                    result.AppendLine(blackAtTop ? " a   b   c   d   e   f   g   h " : " h   g   f   e   d   c   b   a ");
                }
            }

            if (includeFen)
                result.AppendLine("Fen : " + GetFenString());

            if (includeZobristKey)
                result.AppendLine("Zobrist Key : " + ZobristKey);

            return result.ToString();
        }

        static char GetPieceSymbol(Piece piece)
        {
            if (piece.IsNull)
                return ' ';

            char symbol = piece.IsKnight ? 'N' : piece.PieceType.ToString()[0];
            return piece.IsWhite ? char.ToUpper(symbol) : char.ToLower(symbol);
        }

        public override string ToString()
        {
            return CreateDiagram();
        }

        public static Board CreateBoardFromFen(string fen)
        {
            throw new NotSupportedException("CreateBoardFromFEN is not supported by this ChessGame shim.");
        }

        static bool IsValidSquare(int square)
        {
            return square >= 0 && square < BOARD_SIZE;
        }

        static int FileOf(int square)
        {
            return square & 7;
        }

        static int RankOf(int square)
        {
            return square >> 3;
        }

        static bool Wraps(int from, int to, int direction)
        {
            int fromFile = FileOf(from);
            int toFile = FileOf(to);

            if (direction == 1 || direction == -1)
                return Math.Abs(toFile - fromFile) != 1;

            if (direction == 9 || direction == -7)
                return toFile - fromFile != 1;

            if (direction == 7 || direction == -9)
                return fromFile - toFile != 1;

            return false;
        }
    }


    public static class BitboardHelper
    {
        public static void SetSquare(ref ulong bitboard, Square square)
        {
            bitboard |= 1UL << square.Index;
        }

        public static void ClearSquare(ref ulong bitboard, Square square)
        {
            bitboard &= ~(1UL << square.Index);
        }

        public static void ToggleSquare(ref ulong bitboard, Square square)
        {
            bitboard ^= 1UL << square.Index;
        }

        public static bool SquareIsSet(ulong bitboard, Square square)
        {
            return ((bitboard >> square.Index) & 1UL) != 0UL;
        }

        public static int ClearAndGetIndexOfLsb(ref ulong bitboard)
        {
            if (bitboard == 0UL)
                return -1;

            for (int i = 0; i < 64; i++)
            {
                ulong mask = 1UL << i;
                if ((bitboard & mask) != 0UL)
                {
                    bitboard &= ~mask;
                    return i;
                }
            }

            return -1;
        }

        public static int GetNumberOfSetBits(ulong bitboard)
        {
            int count = 0;
            while (bitboard != 0UL)
            {
                bitboard &= bitboard - 1UL;
                count++;
            }
            return count;
        }

        public static ulong GetPieceAttacks(PieceType pieceType, Square square, Board board, bool isWhite)
        {
            return GetPieceAttacks(pieceType, square, board != null ? board.AllPiecesBitboard : 0UL, isWhite);
        }

        public static ulong GetPieceAttacks(PieceType pieceType, Square square, ulong blockers, bool isWhite)
        {
            switch (pieceType)
            {
                case PieceType.Pawn:
                    return GetPawnAttacks(square, isWhite);
                case PieceType.Knight:
                    return GetKnightAttacks(square);
                case PieceType.Bishop:
                    return GetBishopAttacks(square, blockers);
                case PieceType.Rook:
                    return GetRookAttacks(square, blockers);
                case PieceType.Queen:
                    return GetQueenAttacks(square, blockers);
                case PieceType.King:
                    return GetKingAttacks(square);
                default:
                    return 0UL;
            }
        }

        public static ulong GetSliderAttacks(PieceType pieceType, Square square, Board board)
        {
            return GetSliderAttacks(pieceType, square, board != null ? board.AllPiecesBitboard : 0UL);
        }

        public static ulong GetSliderAttacks(PieceType pieceType, Square square, ulong blockers)
        {
            switch (pieceType)
            {
                case PieceType.Bishop:
                    return GetBishopAttacks(square, blockers);
                case PieceType.Rook:
                    return GetRookAttacks(square, blockers);
                case PieceType.Queen:
                    return GetQueenAttacks(square, blockers);
                default:
                    return 0UL;
            }
        }

        public static ulong GetKnightAttacks(Square square)
        {
            int file = square.File;
            int rank = square.Rank;
            ulong result = 0UL;
            AddAttack(ref result, file + 1, rank + 2);
            AddAttack(ref result, file + 2, rank + 1);
            AddAttack(ref result, file + 2, rank - 1);
            AddAttack(ref result, file + 1, rank - 2);
            AddAttack(ref result, file - 1, rank - 2);
            AddAttack(ref result, file - 2, rank - 1);
            AddAttack(ref result, file - 2, rank + 1);
            AddAttack(ref result, file - 1, rank + 2);
            return result;
        }

        public static ulong GetKingAttacks(Square square)
        {
            int file = square.File;
            int rank = square.Rank;
            ulong result = 0UL;
            for (int df = -1; df <= 1; df++)
            {
                for (int dr = -1; dr <= 1; dr++)
                {
                    if (df != 0 || dr != 0)
                        AddAttack(ref result, file + df, rank + dr);
                }
            }
            return result;
        }

        public static ulong GetPawnAttacks(Square square, bool isWhite)
        {
            int file = square.File;
            int rank = square.Rank;
            ulong result = 0UL;
            int direction = isWhite ? 1 : -1;
            AddAttack(ref result, file - 1, rank + direction);
            AddAttack(ref result, file + 1, rank + direction);
            return result;
        }

        public static void VisualizeBitboard(ulong bitboard)
        {
            // The original challenge API uses this for editor debugging. The Space Engineers shim has no debug overlay.
        }

        public static void StopVisualizingBitboard()
        {
            // The original challenge API uses this for editor debugging. The Space Engineers shim has no debug overlay.
        }

        static ulong GetRookAttacks(Square square, ulong blockers)
        {
            ulong result = 0UL;
            AddRayAttacks(ref result, square.File, square.Rank, 1, 0, blockers);
            AddRayAttacks(ref result, square.File, square.Rank, -1, 0, blockers);
            AddRayAttacks(ref result, square.File, square.Rank, 0, 1, blockers);
            AddRayAttacks(ref result, square.File, square.Rank, 0, -1, blockers);
            return result;
        }

        static ulong GetBishopAttacks(Square square, ulong blockers)
        {
            ulong result = 0UL;
            AddRayAttacks(ref result, square.File, square.Rank, 1, 1, blockers);
            AddRayAttacks(ref result, square.File, square.Rank, -1, 1, blockers);
            AddRayAttacks(ref result, square.File, square.Rank, 1, -1, blockers);
            AddRayAttacks(ref result, square.File, square.Rank, -1, -1, blockers);
            return result;
        }

        static ulong GetQueenAttacks(Square square, ulong blockers)
        {
            return GetRookAttacks(square, blockers) | GetBishopAttacks(square, blockers);
        }

        static void AddRayAttacks(ref ulong result, int file, int rank, int fileDelta, int rankDelta, ulong blockers)
        {
            file += fileDelta;
            rank += rankDelta;

            while (IsValidCoord(file, rank))
            {
                int index = file + rank * 8;
                ulong mask = 1UL << index;
                result |= mask;

                if ((blockers & mask) != 0UL)
                    break;

                file += fileDelta;
                rank += rankDelta;
            }
        }

        static void AddAttack(ref ulong result, int file, int rank)
        {
            if (IsValidCoord(file, rank))
                result |= 1UL << (file + rank * 8);
        }

        static bool IsValidCoord(int file, int rank)
        {
            return file >= 0 && file < 8 && rank >= 0 && rank < 8;
        }
    }

    public struct Move : IEquatable<Move>
    {
        public readonly Square StartSquare;
        public readonly Square TargetSquare;
        public readonly PieceType MovePieceType;
        public readonly PieceType CapturePieceType;
        public readonly PieceType PromotionPieceType;

        readonly bool _isNull;
        readonly bool _isEnPassant;
        readonly bool _isCastles;

        public bool IsCapture => CapturePieceType != PieceType.None;

        public bool IsEnPassant => _isEnPassant;

        public bool IsPromotion => PromotionPieceType != PieceType.None;

        public bool IsCastles => _isCastles;

        public bool IsNull =>
            _isNull ||
            (StartSquare.Index == 0 &&
             TargetSquare.Index == 0 &&
             MovePieceType == PieceType.None &&
             CapturePieceType == PieceType.None &&
             PromotionPieceType == PieceType.None);

        public ushort RawValue
        {
            get
            {
                if (IsNull)
                    return 0;

                return (ushort)(StartSquare.Index |
                                (TargetSquare.Index << 6) |
                                ((int)PromotionPieceType << 12));
            }
        }

        public static readonly Move NullMove = CreateNullMove();

        static Move CreateNullMove()
        {
            return new Move(new Square(0), new Square(0), PieceType.None, PieceType.None, PieceType.None, false, false, true);
        }

        public Move(string moveName, Board board)
        {
            if (string.IsNullOrEmpty(moveName) || moveName.Length < 4)
            {
                this = NullMove;
                return;
            }

            StartSquare = new Square(moveName.Substring(0, 2));
            TargetSquare = new Square(moveName.Substring(2, 2));
            PromotionPieceType = moveName.Length >= 5 ? ParsePromotion(moveName[4]) : PieceType.None;

            var movePiece = board != null ? board.GetPiece(StartSquare) : new Piece(PieceType.None, false, StartSquare);
            var capturedPiece = board != null ? board.GetPiece(TargetSquare) : new Piece(PieceType.None, false, TargetSquare);

            MovePieceType = movePiece.PieceType;
            CapturePieceType = capturedPiece.PieceType;
            _isEnPassant = false;
            _isCastles = MovePieceType == PieceType.King && Math.Abs(StartSquare.File - TargetSquare.File) == 2;
            _isNull = false;
        }

        public Move(
            Square startSquare,
            Square targetSquare,
            PieceType movePieceType = PieceType.None,
            PieceType capturePieceType = PieceType.None,
            PieceType promotionPieceType = PieceType.None,
            bool isEnPassant = false,
            bool isCastles = false)
            : this(startSquare, targetSquare, movePieceType, capturePieceType, promotionPieceType, isEnPassant, isCastles, false)
        {
        }

        Move(
            Square startSquare,
            Square targetSquare,
            PieceType movePieceType,
            PieceType capturePieceType,
            PieceType promotionPieceType,
            bool isEnPassant,
            bool isCastles,
            bool isNull)
        {
            StartSquare = startSquare;
            TargetSquare = targetSquare;
            MovePieceType = movePieceType;
            CapturePieceType = capturePieceType;
            PromotionPieceType = promotionPieceType;
            _isEnPassant = isEnPassant;
            _isCastles = isCastles;
            _isNull = isNull;
        }

        static PieceType ParsePromotion(char c)
        {
            switch (char.ToLowerInvariant(c))
            {
                case 'q': return PieceType.Queen;
                case 'r': return PieceType.Rook;
                case 'b': return PieceType.Bishop;
                case 'n': return PieceType.Knight;
                default: return PieceType.None;
            }
        }

        public override string ToString()
        {
            return "Move: '" + StartSquare.Name + TargetSquare.Name + PromotionSuffix(PromotionPieceType) + "'";
        }

        static string PromotionSuffix(PieceType type)
        {
            switch (type)
            {
                case PieceType.Queen: return "q";
                case PieceType.Rook: return "r";
                case PieceType.Bishop: return "b";
                case PieceType.Knight: return "n";
                default: return string.Empty;
            }
        }

        public bool Equals(Move other)
        {
            return StartSquare == other.StartSquare &&
                   TargetSquare == other.TargetSquare &&
                   MovePieceType == other.MovePieceType &&
                   CapturePieceType == other.CapturePieceType &&
                   PromotionPieceType == other.PromotionPieceType &&
                   IsNull == other.IsNull;
        }

        public static bool operator ==(Move lhs, Move rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Move lhs, Move rhs)
        {
            return !lhs.Equals(rhs);
        }

        public override bool Equals(object obj)
        {
            return obj is Move && Equals((Move)obj);
        }

        public override int GetHashCode()
        {
            return RawValue;
        }
    }

    public struct Piece : IEquatable<Piece>
    {
        public readonly bool IsWhite;
        public readonly PieceType PieceType;
        public readonly Square Square;

        public bool IsNull => PieceType == PieceType.None;
        public bool IsRook => PieceType == PieceType.Rook;
        public bool IsKnight => PieceType == PieceType.Knight;
        public bool IsBishop => PieceType == PieceType.Bishop;
        public bool IsQueen => PieceType == PieceType.Queen;
        public bool IsKing => PieceType == PieceType.King;
        public bool IsPawn => PieceType == PieceType.Pawn;

        public Piece(PieceType pieceType, bool isWhite, Square square)
        {
            PieceType = pieceType;
            Square = square;
            IsWhite = isWhite;
        }

        public override string ToString()
        {
            if (IsNull)
                return "Null";

            return (IsWhite ? "White " : "Black ") + PieceType;
        }

        public bool Equals(Piece other)
        {
            return IsWhite == other.IsWhite && PieceType == other.PieceType && Square == other.Square;
        }

        public static bool operator ==(Piece lhs, Piece rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Piece lhs, Piece rhs)
        {
            return !lhs.Equals(rhs);
        }

        public override bool Equals(object obj)
        {
            return obj is Piece && Equals((Piece)obj);
        }

        public override int GetHashCode()
        {
            return ((int)PieceType * 397) ^ Square.Index ^ (IsWhite ? 1 : 0);
        }
    }

    public sealed class PieceList : IEnumerable<Piece>
    {
        readonly Board _board;
        readonly List<Square> _squares = new List<Square>();

        public int Count => _squares.Count;

        public readonly bool IsWhitePieceList;
        public readonly PieceType TypeOfPieceInList;

        public PieceList(Board board, PieceType pieceType, bool white)
        {
            _board = board;
            TypeOfPieceInList = pieceType;
            IsWhitePieceList = white;

            for (int i = 0; i < 64; i++)
            {
                var square = new Square(i);
                var piece = board.GetPiece(square);

                if (!piece.IsNull &&
                    piece.PieceType == pieceType &&
                    piece.IsWhite == white)
                {
                    _squares.Add(square);
                }
            }
        }

        public Piece GetPiece(int index)
        {
            return this[index];
        }

        public Piece this[int index] => _board.GetPiece(_squares[index]);

        public IEnumerator<Piece> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return GetPiece(i);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public struct Square : IEquatable<Square>
    {
        public int File => Index & 7;

        public int Rank => Index >> 3;

        public readonly int Index;

        public string Name => string.Concat((char)('a' + File), (char)('1' + Rank));

        public Square(string name)
        {
            int file = name[0] - 'a';
            int rank = name[1] - '1';
            Index = file + rank * 8;
        }

        public Square(int index)
        {
            Index = index;
        }

        public Square(int file, int rank)
        {
            Index = file + rank * 8;
        }

        public override string ToString()
        {
            return "'" + Name + "' (Index = " + Index + ", File = " + File + ", Rank = " + Rank + ")";
        }

        public bool Equals(Square other)
        {
            return Index == other.Index;
        }

        public static bool operator ==(Square lhs, Square rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(Square lhs, Square rhs)
        {
            return !lhs.Equals(rhs);
        }

        public override bool Equals(object obj)
        {
            return obj is Square && Equals((Square)obj);
        }

        public override int GetHashCode()
        {
            return Index;
        }
    }

    public sealed class Timer
    {
        public readonly int GameStartTimeMilliseconds;
        public readonly int IncrementMilliseconds;
        public int MillisecondsElapsedThisTurn => (int)_sw.ElapsedMilliseconds;

        public int MillisecondsRemaining => Math.Max(0, _millisRemainingAtStartOfTurn - MillisecondsElapsedThisTurn);

        public readonly int OpponentMillisecondsRemaining;

        readonly Stopwatch _sw;
        readonly int _millisRemainingAtStartOfTurn;

        public Timer(int millisRemaining)
        {
            _millisRemainingAtStartOfTurn = millisRemaining;
            _sw = Stopwatch.StartNew();
        }

        public Timer(int remainingMs, int opponentRemainingMs, int startingMs, int incrementMs = 0)
        {
            _millisRemainingAtStartOfTurn = remainingMs;
            OpponentMillisecondsRemaining = opponentRemainingMs;
            GameStartTimeMilliseconds = startingMs;
            IncrementMilliseconds = incrementMs;
            _sw = Stopwatch.StartNew();
        }
    }
}
