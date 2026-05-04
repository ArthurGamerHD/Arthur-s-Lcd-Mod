using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;
using Graph.Apps.Games.Chess.Enum;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public partial class ChessGame
    {
        Point? _checkPosition;

        readonly Dictionary<Point, List<Point>> _availableMoves = new Dictionary<Point, List<Point>>();
        readonly Point[] _rookSteps = { new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1) };

        readonly Point[] _bishopSteps =
            { new Point(1, 1), new Point(-1, -1), new Point(-1, 1), new Point(1, -1) };

        readonly Point[] _knightSteps =
        {
            new Point(2, 1), new Point(2, -1), new Point(-2, 1), new Point(-2, -1),
            new Point(1, 2), new Point(-1, 2), new Point(1, -2), new Point(-1, -2),
        };

        readonly Point[] _queenSteps =
        {
            new Point(1, 0), new Point(-1, 0), new Point(0, 1), new Point(0, -1),
            new Point(1, 1), new Point(-1, -1), new Point(-1, 1), new Point(1, -1)
        };

        IEnumerator<bool> _coroutine;
        readonly byte[] _simulatedBoard = new byte[64];

        IEnumerator<bool> GeneratePathFind()
        {
            var currentColor = (PieceColor)(_currentMove % 2);
            var kingPosition = FindKing(currentColor);
            if(kingPosition == null)
                yield break; // something went wrong, this board should not be without a king

            _checkPosition = IsPositionInDanger(kingPosition.Value, currentColor, Board) ? kingPosition.Value : (Point?)null;

            int index = 0;
            foreach (var pair in _availableMoves)
            {
                var point = pair.Key;
                List<Point> possibleMoves = pair.Value;
                possibleMoves.Clear();

                var cell = GetCell(point) ?? 0;
                if (cell == 0) // empty cell doesn't move
                    continue;

                var color = GetColor(cell);
                if (currentColor != color) // enemy cell doesn't move
                    continue;

                var type = GetPieceType(cell);
                switch (type)
                {
                    case PieceType.Pawn:
                        PawnPathFinder(possibleMoves, point, color);
                        break;
                    case PieceType.Rook:
                        RookPathFinder(possibleMoves, point, color);
                        break;
                    case PieceType.Knight:
                        KnightPathFinder(possibleMoves, point, color);
                        break;
                    case PieceType.Bishop:
                        BishopPathFinder(possibleMoves, point, color);
                        break;
                    case PieceType.Queen:
                        QueenPathFinder(possibleMoves, point, color);
                        break;
                    case PieceType.King:
                        KingPathFinder(possibleMoves, point, color);
                        break;
                    default: // empty cell should never move
                        throw new Exception();
                }
                
                for (int i = 0; i < possibleMoves.Count; i++)
                {
                    if (IsMoveValid(point, possibleMoves[i], kingPosition.Value, color, type)) 
                        continue;
                    possibleMoves.RemoveAtFast(i);
                    i--;
                }

                index++;
                
                if(index % 8 == 0)
                    yield return true;
            }

            if (IsGameOver)
            {

                _overlayOverlay = new GameOverOverlay(this);
                GameOverMessage();
            }
        }

        bool IsMoveValid(Point origin, Point destination, Point king, PieceColor color, PieceType type)
        {
            Board.CopyTo(_simulatedBoard, 0);
            
            Move(origin, destination, _simulatedBoard, GetSpecialMove(origin, destination, _simulatedBoard, type));

            if (origin == king) 
                king = destination;

            return !IsPositionInDanger(king, color, _simulatedBoard);
        }

        public Point? FindKing(PieceColor color)
        {
            var pattern = color == PieceColor.Black ? 0x06 : 0x16;
            
            for (var x = 0; x < _boardSide; x++)
            for (var y = 0; y < _boardSide; y++)
            {
                var piece = Board[x + y * _boardSide];
                if (piece == pattern)
                {
                    return new Point(x, y);
                }
            }
            return null;
        }

        void PawnPathFinder(List<Point> possibleMoves, Point point, PieceColor color)
        {
            var step = color == PieceColor.White ? -1 : 1;

            var y1 = new Point(point.X, point.Y + step);
            var y1Cell = GetCell(y1);
            if (y1Cell == 0)
            {
                possibleMoves.Add(y1);

                if ((color == PieceColor.White && point.Y == 6) ||
                    (color == PieceColor.Black && point.Y == 1))
                {
                    var y2 = new Point(point.X, point.Y + step + step);
                    if (GetCell(y2) == 0)
                        possibleMoves.Add(y2);
                }
            }

            var x1 = new Point(point.X - 1, point.Y + step);
            var x2 = new Point(point.X + 1, point.Y + step);

            var x1Cell = GetCell(x1);
            var x2Cell = GetCell(x2);

            if (x1Cell != null && x1Cell != 0 && GetColor(x1Cell.Value) != color)
                possibleMoves.Add(x1);

            if (x2Cell != null && x2Cell != 0 && GetColor(x2Cell.Value) != color)
                possibleMoves.Add(x2);

            if (!_history.Any() ||
                ((color != PieceColor.White || point.Y != 3) &&
                 (color != PieceColor.Black || point.Y != 4)))
                return;


            var lastMove = _history.Last();
            var pieceType = GetPieceType(GetCell(lastMove.Item2));

            if (pieceType != PieceType.Pawn || Math.Abs(lastMove.Item1.X - point.X) != 1)
                return;

            if ((color == PieceColor.White && lastMove.Item1.Y == 1 && lastMove.Item2.Y == 3) ||
                (color == PieceColor.Black && lastMove.Item1.Y == 6 && lastMove.Item2.Y == 4))
            {
                possibleMoves.Add(new Point(lastMove.Item1.X, lastMove.Item1.Y - step));
            }
        }

        void RookPathFinder(List<Point> possibleMoves, Point point, PieceColor color)
        {
            foreach (var step in _rookSteps)
            {
                var current = point;
                while (true)
                {
                    current = new Point(current.X + step.X, current.Y + step.Y);
                    var cell = GetCell(current);
                    if (cell == null || GetColor(cell.Value) == color)
                        break;
                    possibleMoves.Add(current);

                    if (cell != 0)
                        break;
                }
            }
        }

        void KnightPathFinder(List<Point> possibleMoves, Point point, PieceColor color)
        {
            foreach (var step in _knightSteps)
            {
                var current = new Point(point.X + step.X, point.Y + step.Y);
                var cell = GetCell(current);
                if (cell == null || GetColor(cell.Value) == color)
                    continue;
                possibleMoves.Add(current);
            }
        }

        void BishopPathFinder(List<Point> possibleMoves, Point point, PieceColor color)
        {
            foreach (var step in _bishopSteps)
            {
                var current = point;
                while (true)
                {
                    current = new Point(current.X + step.X, current.Y + step.Y);
                    var cell = GetCell(current);
                    if (cell == null || GetColor(cell.Value) == color)
                        break;
                    possibleMoves.Add(current);

                    if (cell != 0)
                        break;
                }
            }
        }

        void QueenPathFinder(List<Point> possibleMoves, Point point, PieceColor color)
        {
            foreach (var step in _queenSteps)
            {
                var target = point;
                while (true)
                {
                    target = new Point(target.X + step.X, target.Y + step.Y);

                    var cell = GetCell(target);
                    if (cell == null || GetColor(cell.Value) == color)
                        break;
                    possibleMoves.Add(target);

                    if (cell != 0)
                        break;
                }
            }
        }

        void KingPathFinder(List<Point> possibleMoves, Point point, PieceColor color)
        {
            foreach (var step in _queenSteps) // king is just a queen who doesn't practice cardio
            {
                var current = new Point(point.X + step.X, point.Y + step.Y);
                var cell = GetCell(current);
                if (cell == null || GetColor(cell.Value) == color)
                    continue;

                //if (!IsPositionInDanger(current, color, _board)) //(think this is not needed since the simulation already takes care of that)
                possibleMoves.Add(current);
            }
            
            if (CanCastleKingSide(point, color))
                possibleMoves.Add(new Point(point.X + 2, point.Y));

            if (CanCastleQueenSide(point, color))
                possibleMoves.Add(new Point(point.X - 2, point.Y));
        }

        bool CanCastleKingSide(Point king, PieceColor color)
        {
            var flag = color == PieceColor.White ? Castling.WhiteRookRight : Castling.BlackRookLeft;

            if ((_availableCastling & flag) == 0)
                return false;

            if (IsPositionInDanger(king, color, Board))
                return false;

            var step1 = new Point(king.X + 1, king.Y);
            var step2 = new Point(king.X + 2, king.Y);

            if (GetCell(step1) != 0 || GetCell(step2) != 0)
                return false;

            if (IsPositionInDanger(step1, color, Board))
                return false;

            if (IsPositionInDanger(step2, color, Board))
                return false;

            return true;
        }

        bool CanCastleQueenSide(Point king, PieceColor color)
        {
            var flag = color == PieceColor.White ? Castling.WhiteRookLeft : Castling.BlackRookRight;

            if ((_availableCastling & flag) == 0)
                return false;

            if (IsPositionInDanger(king, color, Board))
                return false;

            var step1 = new Point(king.X - 1, king.Y);
            var step2 = new Point(king.X - 2, king.Y);
            var betweenRook = new Point(king.X - 3, king.Y);

            if (GetCell(step1) != 0 || GetCell(step2) != 0 || GetCell(betweenRook) != 0)
                return false;

            if (IsPositionInDanger(step1, color, Board))
                return false;

            if (IsPositionInDanger(step2, color, Board))
                return false;

            return true;
        }

        public bool IsPositionInDanger(Point pos, PieceColor color, byte[] board)
        {
            if (IsPositionInDangerFromPaw(pos, color, board) ||
                IsPositionInDangerFromRook(pos, color, board) ||
                IsPositionInDangerFromBishop(pos, color, board) ||
                IsPositionInDangerFromKnight(pos, color, board))
                return true;

            return false;
        }

        bool IsPositionInDangerFromPaw(Point pos, PieceColor color, byte[] board)
        {
            var pawnAttackVector = color == PieceColor.Black
                ? new[] { new Point(pos.X - 1, pos.Y + 1), new Point(pos.X + 1, pos.Y + 1) }
                : new[] { new Point(pos.X - 1, pos.Y - 1), new Point(pos.X + 1, pos.Y - 1) };

            foreach (var vector in pawnAttackVector)
            {
                var cell = GetCell(vector, board);
                if (cell > 0 && GetColor(cell.Value) != color && GetPieceType(cell) == PieceType.Pawn)
                    return true;
            }

            return false;
        }

        bool IsPositionInDangerFromRook(Point pos, PieceColor color, byte[] board)
        {
            foreach (var step in _rookSteps)
            {
                var distance = 0;
                var targetCell = pos;
                while (true)
                {
                    targetCell = new Point(targetCell.X + step.X, targetCell.Y + step.Y);
                    var cell = GetCell(targetCell, board);
                    if (cell == null || GetColor(cell.Value) == color)
                        break;

                    var cellType = GetPieceType(cell);

                    if (cellType == PieceType.Rook || cellType == PieceType.Queen ||
                        (cellType == PieceType.King && distance == 0))
                        return true;

                    distance++;
                    if (cell != 0)
                        break;
                }
            }

            return false;
        }

        bool IsPositionInDangerFromBishop(Point pos, PieceColor color, byte[] board)
        {
            foreach (var step in _bishopSteps)
            {
                var distance = 0;
                var targetCell = pos;
                while (true)
                {
                    targetCell = new Point(targetCell.X + step.X, targetCell.Y + step.Y);
                    var cell = GetCell(targetCell, board);
                    if (cell == null || GetColor(cell.Value) == color)
                        break;

                    var cellType = GetPieceType(cell);

                    if (cellType == PieceType.Bishop || cellType == PieceType.Queen ||
                        (cellType == PieceType.King && distance == 0))
                        return true;

                    distance++;

                    if (cell != 0)
                        break;
                }
            }

            return false;
        }

        bool IsPositionInDangerFromKnight(Point pos, PieceColor color, byte[] board)
        {
            foreach (var step in _knightSteps)
            {
                var targetCell = new Point(pos.X + step.X, pos.Y + step.Y);
                var cell = GetCell(targetCell, board);

                if (cell == null || GetColor(cell.Value) == color)
                    continue;

                if (GetPieceType(cell) == PieceType.Knight)
                    return true;
            }

            return false;
        }
    }
}