using System;
using System.Collections.Generic;
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
                    var captureType = targetCell != 0
                        ? ToChallengePieceType(GetPieceType(targetCell))
                        : PieceType.None;

                    if (capturesOnly && captureType == PieceType.None)
                        continue;

                    var promotionType = GetChallengePromotionType(origin, target, fromCell);

                    result.Add(new Move(
                        ToChallengeSquare(origin),
                        ToChallengeSquare(target),
                        movePieceType,
                        captureType,
                        promotionType,
                        isEnPassant: movePieceType == PieceType.Pawn &&
                                     origin.X != target.X &&
                                     targetCell == 0,
                        isCastles: movePieceType == PieceType.King &&
                                   Math.Abs(origin.X - target.X) == 2));
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

            if (move.IsPromotion && GetCell(origin) != null)
            {
                var originIndex = PointToIndex(origin);
                Board[originIndex] &= 0xF0;
                Board[originIndex] |= ToGamePromotionPieceValue(move.PromotionPieceType);
            }

            if (TryMove(origin, target, Board) == ActionResult.Success)
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
            var colorMask = white ? Castling.WhiteRookRight : Castling.BlackRookRight;
            return (_availableCastling & colorMask) != 0;
        }

        public bool HasChallengeQueensideCastleRight(bool white)
        {
            var colorMask = white ? Castling.WhiteRookLeft : Castling.BlackRookLeft;
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
            unchecked
            {
                ulong hash = 1469598103934665603UL;

                for (int i = 0; i < Board.Length; i++)
                {
                    hash ^= Board[i];
                    hash *= 1099511628211UL;
                }

                hash ^= (ulong)_currentMove;
                hash *= 1099511628211UL;

                hash ^= (ulong)_availableCastling;
                hash *= 1099511628211UL;

                return hash;
            }
        }

        public Move[] GetChallengeMoveHistory()
        {
            var result = new List<Move>(_history.Count);

            foreach (var pair in _history)
            {
                var fromCell = GetCell(pair.Item1) ?? 0;
                var targetCell = GetCell(pair.Item2) ?? 0;

                result.Add(new Move(
                    ToChallengeSquare(pair.Item1),
                    ToChallengeSquare(pair.Item2),
                    fromCell != 0 ? ToChallengePieceType(GetPieceType(fromCell)) : PieceType.None,
                    targetCell != 0 ? ToChallengePieceType(GetPieceType(targetCell)) : PieceType.None));
            }

            return result.ToArray();
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

            sb.Append(IsChallengeWhiteToMove ? " w " : " b ");
            sb.Append(BuildFenCastlingRights());
            sb.Append(" - 0 ");
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
}
