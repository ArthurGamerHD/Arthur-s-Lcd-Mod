using System;
using System.Linq;
using Graph.Apps.Games.Chess.Enum;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public partial class ChessGame
    {
        Control _controlOverlay;
        Castling _availableCastling = Castling.Full;

        void NewGame()
        {
            for (int i = 16; i < 48; i++)
                _board[i] = 0;

            // Black
            _board[0] = 0x02;
            _board[1] = 0x03;
            _board[2] = 0x04;
            _board[3] = 0x05;
            _board[4] = 0x06;
            _board[5] = 0x04;
            _board[6] = 0x03;
            _board[7] = 0x02;
            for (int i = 8; i < 16; i++)
                _board[i] = 0x01;

            // White
            _board[56] = 0x12;
            _board[57] = 0x13;
            _board[58] = 0x14;
            _board[59] = 0x15;
            _board[60] = 0x16;
            _board[61] = 0x14;
            _board[62] = 0x13;
            _board[63] = 0x12;
            for (int i = 48; i < 56; i++)
                _board[i] = 0x11;

            _history.Clear();
            _historyText = string.Empty;

            _currentMove = 0;

            SelectedTile = null;
            _checkPosition = null;
            _availableCastling = Castling.Full;

            _coroutine?.Dispose();
            _coroutine = GeneratePathFind();
            _controlOverlay = null;
        }

        ActionResult TryExecuteActionAt(Point point)
        {
            if (SelectedTile == null)
            {
                SelectedTile = TrySelect(point);
                return SelectedTile != null ? ActionResult.Selected : ActionResult.EmptySelection;
            }

            if (SelectedTile.Equals(point))
            {
                SelectedTile = null;
                return ActionResult.Unselected;
            }

            return TryMove(SelectedTile.Value, point, _board);
        }

        Point? TrySelect(Point selection)
        {
            var originIndex = PointToIndex(selection);
            var cellData = _board[originIndex];
            if (cellData == 0)
                return null;

            if ((PieceColor)(_currentMove % 2) == GetColor(cellData))
                return selection;

            return null;
        }

        ActionResult TryMove(Point origin, Point target, byte[] board, bool checkValidity = true)
        {
            if (checkValidity && !_availableMoves.ContainsKey(origin))
                return ActionResult.FailToMove;

            if (checkValidity && !_availableMoves[origin].Any(a => a.X == target.X && a.Y == target.Y))
                return ActionResult.FailToMove;

            SelectedTile = null;

            var cellToMove = GetCell(origin, board) ?? 0;
            if (cellToMove == 0)
                return ActionResult.FailToMove;

            var type = GetPieceType(cellToMove);
            var specialMove = GetSpecialMove(origin, target, board, type);

            var color = GetColor(cellToMove);
            if (type == PieceType.Pawn && ((target.Y == 0 && color == PieceColor.White) ||
                                                          (target.Y == 7 && color == PieceColor.Black)))
                _controlOverlay = new PromotionHandler(target, origin, this);
            else
                ExecuteMove(origin, target, board, specialMove);

            return ActionResult.Success;
        }

        void ExecuteMove(Point origin, Point target, byte[] board, SpecialMoves specialMove = SpecialMoves.None)
        {
            Move(origin, target, board, specialMove);
            
            if (_availableCastling != Castling.None)
                UpdateCastling(origin);

            _currentMove++;
            _history.Add(new MyTuple<Point, Point>(origin, target));
            var text = $"{_currentMove}. {ToChessMove(origin)} > {ToChessMove(target)}";

            switch (specialMove)
            {
                case SpecialMoves.Promotion:
                    text += $" {GetPieceType(GetCell(target))}";
                    break;

                case SpecialMoves.EnPassant:
                    text += $" En Passant";
                    break;

                case SpecialMoves.Castling:
                    text += $" Castling";
                    break;
            }

            _historyText = text + "\n" + _historyText;

            _coroutine = GeneratePathFind();
        }

        SpecialMoves GetSpecialMove(Point origin, Point target, byte[] board, PieceType type) =>
            type == PieceType.Pawn && Math.Abs(origin.X -target.X) == 1 && GetCell(target, board) == 0
                ? SpecialMoves.EnPassant
                : type == PieceType.King && Math.Abs(origin.X - target.X) == 2
                    ? SpecialMoves.Castling
                    : 0;

        void Move(Point origin, Point target, byte[] board, SpecialMoves specialMove = SpecialMoves.None)
        {
            var originIndex = PointToIndex(origin);
            var targetIndex = PointToIndex(target);
            var cellToMove = board[originIndex];

            switch (specialMove)
            {
                case SpecialMoves.EnPassant:
                    board[PointToIndex(new Point(target.X, origin.Y))] = 0;
                    break;
                case SpecialMoves.Castling:
                {
                    var direction = origin.X - target.X < 0 ? 1 : -1;
                    var distance = direction > 0 ? 3 : 4;
                    var rookPos = new Point(origin.X + (distance * direction), origin.Y);
                    var rookIndex = PointToIndex(rookPos);
                    var rook = board[rookIndex];
                    board[rookIndex] = 0;
                    board[PointToIndex(new Point(rookPos.X + (distance - 1) * -direction, origin.Y))] = rook;
                    break;
                }
            }

            board[originIndex] = 0;
            board[targetIndex] = cellToMove;
        }

        void UpdateCastling(Point origin)
        {
            if (origin.Y != 0 && origin.Y != _boardSide - 1)
                return;

            if (origin.X == 0)
                _availableCastling &= origin.Y == 0 ? ~Castling.BlackRookRight : ~Castling.WhiteRookLeft;
            else if (origin.X == _boardSide - 1)
                _availableCastling &= origin.Y == 0 ? ~Castling.BlackRookLeft : ~Castling.WhiteRookRight;
            else if (origin.X == 4)
                _availableCastling &= origin.Y == 0 ? ~Castling.BlackKing : ~Castling.WhiteKing;
        }
    }
}