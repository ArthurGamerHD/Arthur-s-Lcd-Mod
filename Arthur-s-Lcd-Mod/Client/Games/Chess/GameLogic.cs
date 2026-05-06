using System;
using System.Linq;
using LcdMod.Client.Games.Chess.Enum;
using LcdMod.Client.Games.Chess.TinyChessChallenge;
using VRageMath;

namespace LcdMod.Client.Games.Chess
{
    public partial class ChessGame
    {
        Overlay _overlayOverlay;
        Castling _availableCastling = Castling.Full;

        void NewGame()
        {
            for (int i = 16; i < 48; i++)
                Board[i] = 0;

            // Black
            Board[0] = 0x02;
            Board[1] = 0x03;
            Board[2] = 0x04;
            Board[3] = 0x05;
            Board[4] = 0x06;
            Board[5] = 0x04;
            Board[6] = 0x03;
            Board[7] = 0x02;
            for (int i = 8; i < 16; i++)
                Board[i] = 0x01;

            // White
            Board[56] = 0x12;
            Board[57] = 0x13;
            Board[58] = 0x14;
            Board[59] = 0x15;
            Board[60] = 0x16;
            Board[61] = 0x14;
            Board[62] = 0x13;
            Board[63] = 0x12;
            for (int i = 48; i < 56; i++)
                Board[i] = 0x11;

            _history.Clear();
            _historyText = string.Empty;

            _currentMove = 0;

            SelectedTile = null;
            _checkPosition = null;
            _availableCastling = Castling.Full;

            _coroutine?.Dispose();
            _coroutine = GeneratePathFind();
            _overlayOverlay = null;
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

            return TryMove(SelectedTile.Value, point, Board);
        }

        Point? TrySelect(Point selection)
        {
            var originIndex = PointToIndex(selection);
            var cellData = Board[originIndex];
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
                _overlayOverlay = new PromotionOverlay(target, origin, this);
            else
                ExecuteMove(origin, target, board, specialMove);

            return ActionResult.Success;
        }

        public void ExecuteMove(
            Point origin,
            Point target,
            byte[] board,
            SpecialMoves specialMove = SpecialMoves.None,
            PieceType promotionType = PieceType.None)
        {
            var record = CreateMoveRecord(origin, target, board, specialMove, promotionType);

            Move(origin, target, board, specialMove, record.PromotionPieceType);

            if (_availableCastling != Castling.None)
                UpdateCastling(origin, target);

            _currentMove++;

            FinalizeMoveRecord(record);
            _history.Add(record);
            _historyText = FormatHistoryLine(record) + "\n" + _historyText;

            _coroutine = GeneratePathFind();
        }

        ChessMoveRecord CreateMoveRecord(
            Point origin,
            Point target,
            byte[] board,
            SpecialMoves specialMove,
            PieceType promotionType)
        {
            var originIndex = PointToIndex(origin);
            var targetIndex = PointToIndex(target);
            var movingCell = board[originIndex];
            var targetCell = board[targetIndex];
            var movingType = GetPieceType(movingCell);
            var movingColor = GetColor(movingCell);

            if (specialMove == SpecialMoves.Promotion && promotionType == PieceType.None)
                promotionType = PieceType.Queen;

            var capturedType = PieceType.None;
            if (specialMove == SpecialMoves.EnPassant)
                capturedType = PieceType.Pawn;
            else if (targetCell != 0 && GetColor(targetCell) != movingColor)
                capturedType = GetPieceType(targetCell);

            return new ChessMoveRecord
            {
                Ply = _currentMove + 1,
                MovingColor = movingColor,
                MovingPieceType = movingType,
                Origin = origin,
                Target = target,
                CapturedPieceType = capturedType,
                PromotionPieceType = specialMove == SpecialMoves.Promotion ? promotionType : PieceType.None,
                SpecialMove = specialMove,
                San = BuildPgnSan(board, origin, target, movingType, movingColor, specialMove,
                    specialMove == SpecialMoves.Promotion ? promotionType : PieceType.None)
            };
        }

        void FinalizeMoveRecord(ChessMoveRecord record)
        {
            var previousHalfmoveClock = _history.Count == 0 ? 0 : _history[_history.Count - 1].HalfmoveClockAfter;

            record.EnPassantTargetAfter = GetPgnEnPassantTarget(record.Origin, record.Target, record.MovingPieceType);
            record.HalfmoveClockAfter = record.MovingPieceType == PieceType.Pawn || record.CapturedPieceType != PieceType.None
                ? 0
                : previousHalfmoveClock + 1;
            record.CastlingRightsAfter = _availableCastling;
            record.FenAfter = BuildPgnFen(Board, _currentMove, _availableCastling, record.EnPassantTargetAfter,
                record.HalfmoveClockAfter);
        }

        string FormatHistoryLine(ChessMoveRecord record)
        {
            var moveNumber = ((record.Ply - 1) / 2) + 1;
            var movePrefix = record.MovingColor == PieceColor.White
                ? moveNumber + "."
                : moveNumber + "...";
            var san = string.IsNullOrEmpty(record.San)
                ? ToPgnCoordinateMove(record.Origin, record.Target, record.PromotionPieceType)
                : record.San;

            return movePrefix + " " + san;
        }

        SpecialMoves GetSpecialMove(Point origin, Point target, byte[] board, PieceType type) =>
            type == PieceType.Pawn && Math.Abs(origin.X - target.X) == 1 && GetCell(target, board) == 0
                ? SpecialMoves.EnPassant
                : type == PieceType.King && Math.Abs(origin.X - target.X) == 2
                    ? SpecialMoves.Castling
                    : 0;

        void Move(
            Point origin,
            Point target,
            byte[] board,
            SpecialMoves specialMove = SpecialMoves.None,
            PieceType promotionType = PieceType.None)
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

            if (specialMove == SpecialMoves.Promotion)
            {
                if (promotionType == PieceType.None)
                    promotionType = PieceType.Queen;

                cellToMove &= 0xF0;
                cellToMove |= ToGamePromotionPieceValue(promotionType);
            }

            board[targetIndex] = cellToMove;
        }

        void UpdateCastling(Point origin, Point target)
        {
            ClearCastlingRightFromMovedSquare(origin);
            ClearCastlingRightFromCapturedRookSquare(target);
        }

        void ClearCastlingRightFromMovedSquare(Point origin)
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

        void ClearCastlingRightFromCapturedRookSquare(Point target)
        {
            if (target.Y != 0 && target.Y != _boardSide - 1)
                return;

            if (target.X == 0)
                _availableCastling &= target.Y == 0 ? ~Castling.BlackRookRight : ~Castling.WhiteRookLeft;
            else if (target.X == _boardSide - 1)
                _availableCastling &= target.Y == 0 ? ~Castling.BlackRookLeft : ~Castling.WhiteRookRight;
        }
    }
}
