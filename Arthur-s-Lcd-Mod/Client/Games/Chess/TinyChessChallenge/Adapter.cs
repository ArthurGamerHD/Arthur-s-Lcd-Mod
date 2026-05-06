using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ChessChallenge.API;
using LcdMod.Client.Games.Chess.Enum;
using LcdMod.Client.Games.Chess.TinyChessChallenge;
using LcdMod.Common.Helpers;
using VRageMath;

// ReSharper disable once CheckNamespace
namespace LcdMod.Client.Games.Chess
{
    public partial class ChessGame
    {
        internal enum ChessBotSelection
        {
            None,
            WhateverBot,
            Squeedo,
            Boychesser,
        }
        
        public bool IsChallengeWhiteToMove => (PieceColor)(_currentMove % 2) == PieceColor.White;

        public int ChallengePlyCount => _currentMove;

        public Move[] GetChallengeLegalMoves(bool capturesOnly = false)
        {
            EnsureChallengeMoveGenerationFinished();

            var result = new List<Move>();

            foreach (var pair in _availableMoves)
            {
                var origin = pair.Key;
                var fromCell = GetCell(origin) ?? 0;
                if (fromCell == 0)
                    continue;

                var movePieceType = ToChallengePieceType(GetPieceType(fromCell));

                foreach (var target in pair.Value)
                {
                    var targetCell = GetCell(target) ?? 0;
                    var isEnPassant = movePieceType == PieceType.Pawn &&
                                      origin.X != target.X &&
                                      targetCell == 0;
                    var isCastles = movePieceType == PieceType.King &&
                                    Math.Abs(origin.X - target.X) == 2;
                    var captureType = isEnPassant
                        ? PieceType.Pawn
                        : targetCell != 0 ? ToChallengePieceType(GetPieceType(targetCell)) : PieceType.None;

                    if (capturesOnly && captureType == PieceType.None)
                        continue;

                    var promotionType = GetChallengePromotionType(origin, target, fromCell);

                    if (promotionType != PieceType.None)
                    {
                        result.Add(new Move(ToChallengeSquare(origin), ToChallengeSquare(target), movePieceType,
                            captureType, PieceType.Queen, isEnPassant, isCastles));
                        result.Add(new Move(ToChallengeSquare(origin), ToChallengeSquare(target), movePieceType,
                            captureType, PieceType.Knight, isEnPassant, isCastles));
                        result.Add(new Move(ToChallengeSquare(origin), ToChallengeSquare(target), movePieceType,
                            captureType, PieceType.Rook, isEnPassant, isCastles));
                        result.Add(new Move(ToChallengeSquare(origin), ToChallengeSquare(target), movePieceType,
                            captureType, PieceType.Bishop, isEnPassant, isCastles));
                    }
                    else
                    {
                        result.Add(new Move(
                            ToChallengeSquare(origin),
                            ToChallengeSquare(target),
                            movePieceType,
                            captureType,
                            PieceType.None,
                            isEnPassant,
                            isCastles));
                    }
                }
            }

            return result.ToArray();
        }

        public void MakeChallengeMove(Move move)
        {
            if (move.IsNull)
                return;

            EnsureChallengeMoveGenerationFinished();

            var origin = ToGamePoint(move.StartSquare);
            var target = ToGamePoint(move.TargetSquare);
            var cell = GetCell(origin) ?? 0;

            if (cell == 0)
            {
                LogHelper.LogInfo($"Rejected bot move from empty square: {move}");
                return;
            }

            var currentColor = (PieceColor)(_currentMove % 2);
            if (GetColor(cell) != currentColor)
            {
                LogHelper.LogInfo(
                    $"Rejected bot move for wrong side: {move}, " +
                    $"pieceColor={GetColor(cell)}, currentColor={currentColor}, currentMove={_currentMove}");
                return;
            }

            if (!_availableMoves.ContainsKey(origin) ||
                !_availableMoves[origin].Any(a => a.X == target.X && a.Y == target.Y))
            {
                LogHelper.LogInfo($"Rejected illegal bot move: {move}");
                return;
            }

            var specialMove = move.IsEnPassant
                ? SpecialMoves.EnPassant
                : move.IsCastles ? SpecialMoves.Castling : SpecialMoves.None;

            if (move.IsPromotion)
                specialMove = SpecialMoves.Promotion;

            ExecuteMove(origin, target, Board, specialMove, move.IsPromotion ? move.PromotionPieceType : PieceType.None);
            Save();
        }

        public Piece GetChallengePiece(Square square)
        {
            var point = ToGamePoint(square);
            var cell = GetCell(point) ?? 0;

            if (cell == 0)
                return new Piece(PieceType.None, false, square);

            return new Piece(
                ToChallengePieceType(GetPieceType(cell)),
                GetColor(cell) == PieceColor.White,
                square);
        }

        public Square GetChallengeKingSquare(bool white)
        {
            var color = white ? PieceColor.White : PieceColor.Black;
            var king = FindKing(color);

            return king.HasValue ? ToChallengeSquare(king.Value) : new Square(0);
        }

        public bool IsChallengeInCheck(bool white)
        {
            var color = white ? PieceColor.White : PieceColor.Black;
            var king = FindKing(color);

            return king.HasValue && IsPositionInDanger(king.Value, color, Board);
        }

        public bool IsChallengeSquareAttackedByOpponent(Square square)
        {
            var point = ToGamePoint(square);
            var opponent = IsChallengeWhiteToMove ? PieceColor.Black : PieceColor.White;
            return IsPositionInDanger(point, opponent, Board);
        }

        public bool HasChallengeKingsideCastleRight(bool white)
        {
            // The game-space Castling enum names black rook sides from Black's
            // perspective. In API/chess coordinates, black kingside is h8, which is
            // BlackRookLeft in this enum.
            var colorMask = white ? Castling.WhiteRookRight : Castling.BlackRookLeft;
            return (_availableCastling & colorMask) != 0;
        }

        public bool HasChallengeQueensideCastleRight(bool white)
        {
            // In API/chess coordinates, black queenside is a8, which is
            // BlackRookRight in the game-space enum.
            var colorMask = white ? Castling.WhiteRookLeft : Castling.BlackRookRight;
            return (_availableCastling & colorMask) != 0;
        }

        public ulong GetChallengeColorBitboard(bool white)
        {
            ulong result = 0UL;
            var color = white ? PieceColor.White : PieceColor.Black;

            for (int i = 0; i < Board.Length; i++)
            {
                var cell = Board[i];
                if (cell != 0 && GetColor(cell) == color)
                    result |= 1UL << ToChallengeSquare(IndexToPoint(i)).Index;
            }

            return result;
        }

        public ulong GetChallengePositionHash()
        {
            return GetChallengeReplayState().CurrentPositionHash;
        }

        public Move[] GetChallengeMoveHistory()
        {
            return GetChallengeReplayState().MoveHistory.ToArray();
        }

        public ChallengeReplayState GetChallengeReplayState()
        {
            var state = new ChallengeReplayState();
            var replayBoard = CreateChallengeStartBoard();
            var replayCastling = Castling.Full;
            var enPassantSquareIndex = -1;
            var fiftyMoveCounter = 0;
            var ply = 0;

            state.RepetitionHistory.Add(HashChallengePosition(replayBoard, true, replayCastling, enPassantSquareIndex));

            for (int i = 0; i < _history.Count; i++)
            {
                var record = _history[i];
                var origin = record.Origin;
                var target = record.Target;
                var originIndex = PointToIndex(origin);
                var targetIndex = PointToIndex(target);

                if (originIndex < 0 || originIndex >= replayBoard.Length ||
                    targetIndex < 0 || targetIndex >= replayBoard.Length)
                    continue;

                var fromCell = replayBoard[originIndex];
                if (fromCell == 0)
                    continue;

                var moveType = record.MovingPieceType != PieceType.None
                    ? record.MovingPieceType
                    : GetPieceType(fromCell);
                var isEnPassant = record.SpecialMove == SpecialMoves.EnPassant;
                var isCastles = record.SpecialMove == SpecialMoves.Castling;
                var captureType = record.CapturedPieceType;
                var promotionType = record.PromotionPieceType;

                state.MoveHistory.Add(new Move(
                    ToChallengeSquare(origin),
                    ToChallengeSquare(target),
                    ToChallengePieceType(moveType),
                    ToChallengePieceType(captureType),
                    ToChallengePieceType(promotionType),
                    isEnPassant,
                    isCastles));

                ApplyReplayMove(replayBoard, origin, target, isEnPassant, isCastles, promotionType);

                replayCastling = record.CastlingRightsAfter;
                enPassantSquareIndex = ParseReplayEnPassantSquare(record.EnPassantTargetAfter);
                fiftyMoveCounter = record.HalfmoveClockAfter;

                if (moveType == PieceType.Pawn || captureType != PieceType.None)
                    state.RepetitionHistory.Clear();

                ply++;
                state.RepetitionHistory.Add(HashChallengePosition(replayBoard, (ply % 2) == 0, replayCastling, enPassantSquareIndex));
            }

            state.FiftyMoveCounter = fiftyMoveCounter;
            state.EnPassantSquareIndex = enPassantSquareIndex;
            state.CurrentPositionHash = HashChallengePosition(Board, IsChallengeWhiteToMove, _availableCastling, enPassantSquareIndex);

            if (state.RepetitionHistory.Count == 0 ||
                state.RepetitionHistory[state.RepetitionHistory.Count - 1] != state.CurrentPositionHash)
            {
                state.RepetitionHistory.Add(state.CurrentPositionHash);
            }

            return state;
        }

        int ParseReplayEnPassantSquare(string squareName)
        {
            if (string.IsNullOrEmpty(squareName) || squareName == "-" || squareName.Length < 2)
                return -1;

            int file = squareName[0] - 'a';
            int rank;
            if (!int.TryParse(squareName.Substring(1), out rank))
                return -1;

            if (file < 0 || file >= _boardSide || rank < 1 || rank > _boardSide)
                return -1;

            return ToChallengeSquare(new Point(file, _boardSide - rank)).Index;
        }

        byte[] CreateChallengeStartBoard()
        {
            var board = new byte[_boardSide * _boardSide];

            // Black
            board[0] = 0x02;
            board[1] = 0x03;
            board[2] = 0x04;
            board[3] = 0x05;
            board[4] = 0x06;
            board[5] = 0x04;
            board[6] = 0x03;
            board[7] = 0x02;
            for (int i = 8; i < 16; i++)
                board[i] = 0x01;

            // White
            board[56] = 0x12;
            board[57] = 0x13;
            board[58] = 0x14;
            board[59] = 0x15;
            board[60] = 0x16;
            board[61] = 0x14;
            board[62] = 0x13;
            board[63] = 0x12;
            for (int i = 48; i < 56; i++)
                board[i] = 0x11;

            return board;
        }

        void ApplyReplayMove(byte[] replayBoard, Point origin, Point target, bool isEnPassant, bool isCastles, PieceType promotionType)
        {
            var originIndex = PointToIndex(origin);
            var targetIndex = PointToIndex(target);
            var cellToMove = replayBoard[originIndex];

            if (isEnPassant)
                replayBoard[PointToIndex(new Point(target.X, origin.Y))] = 0;

            if (isCastles)
            {
                var direction = origin.X - target.X < 0 ? 1 : -1;
                var distance = direction > 0 ? 3 : 4;
                var rookPos = new Point(origin.X + (distance * direction), origin.Y);
                var rookIndex = PointToIndex(rookPos);
                var rook = replayBoard[rookIndex];
                replayBoard[rookIndex] = 0;
                replayBoard[PointToIndex(new Point(rookPos.X + (distance - 1) * -direction, origin.Y))] = rook;
            }

            replayBoard[originIndex] = 0;

            if (promotionType != PieceType.None)
            {
                cellToMove &= 0xF0;
                cellToMove |= ToGamePromotionPieceValue(promotionType);
            }

            replayBoard[targetIndex] = cellToMove;
        }

        ulong HashChallengePosition(byte[] board, bool whiteToMove, Castling castling, int enPassantSquareIndex)
        {
            unchecked
            {
                ulong hash = 1469598103934665603UL;
                var pieces = new PieceType[64];
                var whitePieces = new bool[64];

                for (int i = 0; i < board.Length; i++)
                {
                    var cell = board[i];
                    var square = ToChallengeSquare(IndexToPoint(i)).Index;

                    if (cell == 0)
                    {
                        pieces[square] = PieceType.None;
                        whitePieces[square] = false;
                    }
                    else
                    {
                        pieces[square] = ToChallengePieceType(GetPieceType(cell));
                        whitePieces[square] = GetColor(cell) == PieceColor.White;
                    }
                }

                for (int i = 0; i < 64; i++)
                {
                    hash ^= (ulong)((int)pieces[i] + 1);
                    hash *= 1099511628211UL;
                    hash ^= whitePieces[i] ? 1UL : 0UL;
                    hash *= 1099511628211UL;
                }

                hash ^= whiteToMove ? 1UL : 0UL;
                hash *= 1099511628211UL;
                hash ^= (ulong)(HasReplayKingsideCastleRight(castling, true) ? 1 : 0);
                hash *= 1099511628211UL;
                hash ^= (ulong)(HasReplayQueensideCastleRight(castling, true) ? 1 : 0);
                hash *= 1099511628211UL;
                hash ^= (ulong)(HasReplayKingsideCastleRight(castling, false) ? 1 : 0);
                hash *= 1099511628211UL;
                hash ^= (ulong)(HasReplayQueensideCastleRight(castling, false) ? 1 : 0);
                hash *= 1099511628211UL;
                hash ^= (ulong)(enPassantSquareIndex + 1);
                hash *= 1099511628211UL;

                return hash;
            }
        }

        static bool HasReplayKingsideCastleRight(Castling castling, bool white)
        {
            var colorMask = white ? Castling.WhiteRookRight : Castling.BlackRookLeft;
            return (castling & colorMask) != 0;
        }

        static bool HasReplayQueensideCastleRight(Castling castling, bool white)
        {
            var colorMask = white ? Castling.WhiteRookLeft : Castling.BlackRookRight;
            return (castling & colorMask) != 0;
        }

        public string GetChallengeFenString()
        {
            var sb = new StringBuilder();

            for (int y = 0; y < _boardSide; y++)
            {
                int empty = 0;

                for (int x = 0; x < _boardSide; x++)
                {
                    var cell = GetCell(new Point(x, y)) ?? 0;

                    if (cell == 0)
                    {
                        empty++;
                        continue;
                    }

                    if (empty > 0)
                    {
                        sb.Append(empty);
                        empty = 0;
                    }

                    sb.Append(ToFenPiece(cell));
                }

                if (empty > 0)
                    sb.Append(empty);

                if (y != _boardSide - 1)
                    sb.Append('/');
            }

            var replayState = GetChallengeReplayState();

            sb.Append(IsChallengeWhiteToMove ? " w " : " b ");
            sb.Append(BuildFenCastlingRights());
            sb.Append(' ');
            sb.Append(replayState.EnPassantSquareIndex >= 0
                ? new Square(replayState.EnPassantSquareIndex).Name
                : "-");
            sb.Append(' ');
            sb.Append(replayState.FiftyMoveCounter);
            sb.Append(' ');
            sb.Append((_currentMove / 2) + 1);
            return sb.ToString();
        }

        void EnsureChallengeMoveGenerationFinished()
        {
            while (_coroutine != null)
                HandleCoroutine();
        }

        static PieceType ToChallengePieceType(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn:
                    return PieceType.Pawn;
                case PieceType.Rook:
                    return PieceType.Rook;
                case PieceType.Knight:
                    return PieceType.Knight;
                case PieceType.Bishop:
                    return PieceType.Bishop;
                case PieceType.Queen:
                    return PieceType.Queen;
                case PieceType.King:
                    return PieceType.King;
                default:
                    return PieceType.None;
            }
        }

        static byte ToGamePromotionPieceValue(PieceType type)
        {
            // Return LCD board-byte/texture ids, not ChessChallenge enum values.
            switch (type)
            {
                case PieceType.Rook:
                    return 0x02;
                case PieceType.Knight:
                    return 0x03;
                case PieceType.Bishop:
                    return 0x04;
                case PieceType.Queen:
                    return 0x05;
                default:
                    return 0x05;
            }
        }

        // ReSharper disable once UnusedParameter.Local
        PieceType GetChallengePromotionType(Point origin, Point target, byte fromCell)
        {
            if (GetPieceType(fromCell) != PieceType.Pawn)
                return PieceType.None;

            var color = GetColor(fromCell);
            bool reachesPromotionRank =
                (target.Y == 0 && color == PieceColor.White) ||
                (target.Y == _boardSide - 1 && color == PieceColor.Black);

            return reachesPromotionRank ? PieceType.Queen : PieceType.None;
        }

        Point ToGamePoint(Square square)
        {
            return new Point(square.File, _boardSide - square.Rank - 1);
        }

        Square ToChallengeSquare(Point point)
        {
            return new Square(point.X, _boardSide - point.Y - 1);
        }

        Point IndexToPoint(int index)
        {
            return new Point(index % _boardSide, index / _boardSide);
        }

        char ToFenPiece(byte cell)
        {
            char c;
            switch (GetPieceType(cell))
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

            return GetColor(cell) == PieceColor.White ? char.ToUpper(c) : c;
        }

        string BuildFenCastlingRights()
        {
            var sb = new StringBuilder();

            if (HasChallengeKingsideCastleRight(true))
                sb.Append('K');
            if (HasChallengeQueensideCastleRight(true))
                sb.Append('Q');
            if (HasChallengeKingsideCastleRight(false))
                sb.Append('k');
            if (HasChallengeQueensideCastleRight(false))
                sb.Append('q');

            return sb.Length == 0 ? "-" : sb.ToString();
        }
    }

    public sealed class ChallengeReplayState
    {
        public readonly List<Move> MoveHistory = new List<Move>();
        public readonly List<ulong> RepetitionHistory = new List<ulong>();
        public int EnPassantSquareIndex = -1;
        public int FiftyMoveCounter;
        public ulong CurrentPositionHash;
    }
}
