using System;
using System.Collections.Generic;
using System.Text;
using ChessChallenge.API;
using Graph.Apps.Games.Chess.Enum;
using Graph.Helpers;
using VRage;
using VRageMath;

// ReSharper disable once CheckNamespace
namespace Graph.Apps.Games.Chess
{
    public partial class ChessGame
    {
        public bool IsChallengeWhiteToMove
        {
            get { return (PieceColor)(_currentMove % 2) == PieceColor.White; }
        }

        public int ChallengePlyCount
        {
            get { return _currentMove; }
        }

        public ChessChallenge.API.Move[] GetChallengeLegalMoves(bool capturesOnly = false)
        {
            EnsureChallengeMoveGenerationFinished();

            var result = new List<ChessChallenge.API.Move>();

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
                        : ChessChallenge.API.PieceType.None;

                    if (capturesOnly && captureType == ChessChallenge.API.PieceType.None)
                        continue;

                    var promotionType = GetChallengePromotionType(origin, target, fromCell);

                    result.Add(new ChessChallenge.API.Move(
                        ToChallengeSquare(origin),
                        ToChallengeSquare(target),
                        movePieceType,
                        captureType,
                        promotionType,
                        isEnPassant: movePieceType == ChessChallenge.API.PieceType.Pawn &&
                                     origin.X != target.X &&
                                     targetCell == 0,
                        isCastles: movePieceType == ChessChallenge.API.PieceType.King &&
                                   Math.Abs(origin.X - target.X) == 2));
                }
            }

            return result.ToArray();
        }

        public void MakeChallengeMove(ChessChallenge.API.Move move)
        {
            if (move.IsNull)
                return;

            EnsureChallengeMoveGenerationFinished();

            var origin = ToGamePoint(move.StartSquare);
            var target = ToGamePoint(move.TargetSquare);
            var cell = GetCell(origin) ?? 0;

            if (cell == 0)
            {
                LogHelper.Log($"Rejected bot move from empty square: {move}");
                return;
            }

            var currentColor = (PieceColor)(_currentMove % 2);
            if (GetColor(cell) != currentColor)
            {
                LogHelper.Log(
                    $"Rejected bot move for wrong side: {move}, " +
                    $"pieceColor={GetColor(cell)}, currentColor={currentColor}, currentMove={_currentMove}");
                return;
            }

            if (move.IsPromotion && GetCell(origin) != null)
            {
                var originIndex = PointToIndex(origin);
                _board[originIndex] &= 0xF0;
                _board[originIndex] |= ToGamePromotionPieceValue(move.PromotionPieceType);
            }

            if (TryMove(origin, target, _board) == ActionResult.Success)
                Save();
        }

        public ChessChallenge.API.Piece GetChallengePiece(ChessChallenge.API.Square square)
        {
            var point = ToGamePoint(square);
            var cell = GetCell(point) ?? 0;

            if (cell == 0)
                return new ChessChallenge.API.Piece(ChessChallenge.API.PieceType.None, false, square);

            return new ChessChallenge.API.Piece(
                ToChallengePieceType(GetPieceType(cell)),
                GetColor(cell) == PieceColor.White,
                square);
        }

        public ChessChallenge.API.Square GetChallengeKingSquare(bool white)
        {
            var color = white ? PieceColor.White : PieceColor.Black;
            var king = FindKing(color);

            return king.HasValue ? ToChallengeSquare(king.Value) : new ChessChallenge.API.Square(0);
        }

        public bool IsChallengeInCheck(bool white)
        {
            var color = white ? PieceColor.White : PieceColor.Black;
            var king = FindKing(color);

            return king.HasValue && IsPositionInDanger(king.Value, color, _board);
        }

        public bool IsChallengeSquareAttackedByOpponent(ChessChallenge.API.Square square)
        {
            var point = ToGamePoint(square);
            var opponent = IsChallengeWhiteToMove ? PieceColor.Black : PieceColor.White;
            return IsPositionInDanger(point, opponent, _board);
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

            for (int i = 0; i < _board.Length; i++)
            {
                var cell = _board[i];
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

                for (int i = 0; i < _board.Length; i++)
                {
                    hash ^= _board[i];
                    hash *= 1099511628211UL;
                }

                hash ^= (ulong)_currentMove;
                hash *= 1099511628211UL;

                hash ^= (ulong)_availableCastling;
                hash *= 1099511628211UL;

                return hash;
            }
        }

        public ChessChallenge.API.Move[] GetChallengeMoveHistory()
        {
            var result = new List<ChessChallenge.API.Move>(_history.Count);

            foreach (var pair in _history)
            {
                var fromCell = GetCell(pair.Item1) ?? 0;
                var targetCell = GetCell(pair.Item2) ?? 0;

                result.Add(new ChessChallenge.API.Move(
                    ToChallengeSquare(pair.Item1),
                    ToChallengeSquare(pair.Item2),
                    fromCell != 0 ? ToChallengePieceType(GetPieceType(fromCell)) : ChessChallenge.API.PieceType.None,
                    targetCell != 0 ? ToChallengePieceType(GetPieceType(targetCell)) : ChessChallenge.API.PieceType.None));
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

        static ChessChallenge.API.PieceType ToChallengePieceType(Graph.Apps.Games.Chess.Enum.PieceType type)
        {
            switch (type)
            {
                case Graph.Apps.Games.Chess.Enum.PieceType.Pawn:
                    return ChessChallenge.API.PieceType.Pawn;
                case Graph.Apps.Games.Chess.Enum.PieceType.Rook:
                    return ChessChallenge.API.PieceType.Rook;
                case Graph.Apps.Games.Chess.Enum.PieceType.Knight:
                    return ChessChallenge.API.PieceType.Knight;
                case Graph.Apps.Games.Chess.Enum.PieceType.Bishop:
                    return ChessChallenge.API.PieceType.Bishop;
                case Graph.Apps.Games.Chess.Enum.PieceType.Queen:
                    return ChessChallenge.API.PieceType.Queen;
                case Graph.Apps.Games.Chess.Enum.PieceType.King:
                    return ChessChallenge.API.PieceType.King;
                default:
                    return ChessChallenge.API.PieceType.None;
            }
        }

        static byte ToGamePromotionPieceValue(ChessChallenge.API.PieceType type)
        {
            switch (type)
            {
                case ChessChallenge.API.PieceType.Rook:
                    return 0x02;
                case ChessChallenge.API.PieceType.Knight:
                    return 0x03;
                case ChessChallenge.API.PieceType.Bishop:
                    return 0x04;
                case ChessChallenge.API.PieceType.Queen:
                    return 0x05;
                default:
                    return 0x05;
            }
        }

        ChessChallenge.API.PieceType GetChallengePromotionType(Point origin, Point target, byte fromCell)
        {
            if (GetPieceType(fromCell) != Graph.Apps.Games.Chess.Enum.PieceType.Pawn)
                return ChessChallenge.API.PieceType.None;

            var color = GetColor(fromCell);
            bool reachesPromotionRank =
                (target.Y == 0 && color == PieceColor.White) ||
                (target.Y == _boardSide - 1 && color == PieceColor.Black);

            return reachesPromotionRank ? ChessChallenge.API.PieceType.Queen : ChessChallenge.API.PieceType.None;
        }

        Point ToGamePoint(ChessChallenge.API.Square square)
        {
            return new Point(square.File, _boardSide - square.Rank - 1);
        }

        ChessChallenge.API.Square ToChallengeSquare(Point point)
        {
            return new ChessChallenge.API.Square(point.X, _boardSide - point.Y - 1);
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
                case Graph.Apps.Games.Chess.Enum.PieceType.Pawn:
                    c = 'p';
                    break;
                case Graph.Apps.Games.Chess.Enum.PieceType.Rook:
                    c = 'r';
                    break;
                case Graph.Apps.Games.Chess.Enum.PieceType.Knight:
                    c = 'n';
                    break;
                case Graph.Apps.Games.Chess.Enum.PieceType.Bishop:
                    c = 'b';
                    break;
                case Graph.Apps.Games.Chess.Enum.PieceType.Queen:
                    c = 'q';
                    break;
                case Graph.Apps.Games.Chess.Enum.PieceType.King:
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
