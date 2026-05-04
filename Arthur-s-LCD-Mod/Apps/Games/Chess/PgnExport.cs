using System;
using System.Collections.Generic;
using System.Text;
using ChessChallenge.API;
using Graph.Apps.Games.Chess.Enum;
using VRage;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public partial class ChessGame
    {
        sealed class PgnReplayState
        {
            public readonly List<string> Moves = new List<string>();
            public int HalfmoveClock;
            public string EnPassantTarget = "-";
            public bool ReplayWasClean = true;
        }

        public string Export()
        {
            EnsureChallengeMoveGenerationFinished();

            var replay = BuildPgnReplayState();
            var result = GetPgnResult();
            var currentFen = BuildPgnFen(Board, _currentMove, _availableCastling, replay.EnPassantTarget, replay.HalfmoveClock);
            var moveText = BuildPgnMoveText(replay.Moves, result, currentFen, replay.ReplayWasClean);

            var utc = DateTime.UtcNow;
            var pgn = new StringBuilder();
            pgn.AppendLine("[Event \"Space Engineers Chess\"]");
            pgn.AppendLine("[Site \"Space Engineers\"]");
            pgn.AppendLine($"[Date \"{utc:yyyy.MM.dd}\"]");
            pgn.AppendLine("[Round \"-\"]");
            pgn.AppendLine("[White \"White\"]");
            pgn.AppendLine("[Black \"Black\"]");
            pgn.Append("[Result \"");
            pgn.Append(EscapePgnTagValue(result));
            pgn.AppendLine("\"]");
            pgn.Append("[CurrentFEN \"");
            pgn.Append(EscapePgnTagValue(currentFen));
            pgn.AppendLine("\"]");
            pgn.Append("[PlyCount \"");
            pgn.Append(_currentMove);
            pgn.AppendLine("\"]");
            pgn.AppendLine();
            AppendWrappedPgnMoveText(pgn, moveText, 78);

            return pgn.ToString();
        }

        PgnReplayState BuildPgnReplayState()
        {
            var state = new PgnReplayState();
            var board = BuildPgnInitialBoard();

            for (int i = 0; i < _history.Count; i++)
            {
                var move = _history[i];
                var origin = move.Item1;
                var target = move.Item2;

                if (!IsPgnPointOnBoard(origin) || !IsPgnPointOnBoard(target))
                {
                    state.Moves.Add(ToPgnCoordinateMove(origin, target, PieceType.None));
                    state.ReplayWasClean = false;
                    continue;
                }

                var originIndex = PointToIndex(origin);
                var targetIndex = PointToIndex(target);
                var movingCell = board[originIndex];

                if (movingCell == 0)
                {
                    state.Moves.Add(ToPgnCoordinateMove(origin, target, PieceType.None));
                    state.ReplayWasClean = false;
                    continue;
                }

                var movingType = GetPieceType(movingCell);
                var movingColor = GetColor(movingCell);
                var specialMove = GetPgnSpecialMove(board, origin, target, movingType);
                var promotionType = specialMove == SpecialMoves.Promotion
                    ? InferPgnPromotionType(i, target, movingColor)
                    : PieceType.None;

                bool isCapture = IsPgnCapture(board, origin, target, specialMove);
                state.Moves.Add(BuildPgnSan(board, origin, target, movingType, movingColor, specialMove, promotionType));

                ApplyPgnMove(board, origin, target, specialMove, promotionType);

                state.HalfmoveClock = movingType == PieceType.Pawn || isCapture ? 0 : state.HalfmoveClock + 1;
                state.EnPassantTarget = GetPgnEnPassantTarget(origin, target, movingType);
            }

            return state;
        }

        string BuildPgnMoveText(List<string> moves, string result, string currentFen, bool replayWasClean)
        {
            var sb = new StringBuilder();

            for (int i = 0; i < moves.Count; i++)
            {
                if (i > 0)
                    sb.Append(' ');

                if ((i & 1) == 0)
                {
                    sb.Append((i / 2) + 1);
                    sb.Append(". ");
                }

                sb.Append(moves[i]);
            }

            if (sb.Length > 0)
                sb.Append(' ');

            sb.Append(result);

            if (!string.IsNullOrEmpty(currentFen))
            {
                sb.Append(" { Current FEN: ");
                sb.Append(EscapePgnComment(currentFen));
                sb.Append(" }");
            }

            if (!replayWasClean)
                sb.Append(" { Some historical moves could not be replayed as SAN and were exported in coordinate form. }");

            return sb.ToString();
        }

        string BuildPgnSan(
            byte[] board,
            Point origin,
            Point target,
            PieceType movingType,
            PieceColor movingColor,
            SpecialMoves specialMove,
            PieceType promotionType)
        {
            bool isCapture = IsPgnCapture(board, origin, target, specialMove);
            string san;

            if (specialMove == SpecialMoves.Castling)
            {
                san = target.X > origin.X ? "O-O" : "O-O-O";
            }
            else
            {
                var sb = new StringBuilder();

                if (movingType == PieceType.Pawn)
                {
                    if (isCapture)
                        sb.Append(GetPgnFile(origin.X));
                }
                else
                {
                    sb.Append(GetPgnPieceLetter(movingType));
                    sb.Append(GetPgnDisambiguation(board, origin, target, movingType, movingColor));
                }

                if (isCapture)
                    sb.Append('x');

                sb.Append(ToChessMove(target));

                if (promotionType != PieceType.None && promotionType != PieceType.Pawn)
                {
                    sb.Append('=');
                    sb.Append(GetPgnPieceLetter(promotionType));
                }

                san = sb.ToString();
            }

            var boardAfter = CopyPgnBoard(board);
            ApplyPgnMove(boardAfter, origin, target, specialMove, promotionType);

            var opponent = GetPgnOppositeColor(movingColor);
            if (IsPgnKingInCheck(boardAfter, opponent))
                san += HasAnyPgnLegalMove(boardAfter, opponent) ? "+" : "#";

            return san;
        }

        string GetPgnDisambiguation(byte[] board, Point origin, Point target, PieceType movingType, PieceColor movingColor)
        {
            bool hasAmbiguousPiece = false;
            bool sameFile = false;
            bool sameRank = false;

            for (int i = 0; i < board.Length; i++)
            {
                var otherOrigin = IndexToPoint(i);
                if (otherOrigin == origin)
                    continue;

                var cell = board[i];
                if (cell == 0 || GetColor(cell) != movingColor || GetPieceType(cell) != movingType)
                    continue;

                if (!CanPgnPieceLegallyMove(board, otherOrigin, target, movingType, movingColor, PieceType.None))
                    continue;

                hasAmbiguousPiece = true;

                if (otherOrigin.X == origin.X)
                    sameFile = true;
                if (otherOrigin.Y == origin.Y)
                    sameRank = true;
            }

            if (!hasAmbiguousPiece)
                return string.Empty;

            if (!sameFile)
                return GetPgnFile(origin.X).ToString();

            if (!sameRank)
                return (_boardSide - origin.Y).ToString();

            return GetPgnFile(origin.X).ToString() + (_boardSide - origin.Y);
        }

        bool HasAnyPgnLegalMove(byte[] board, PieceColor color)
        {
            for (int i = 0; i < board.Length; i++)
            {
                var origin = IndexToPoint(i);
                var cell = board[i];

                if (cell == 0 || GetColor(cell) != color)
                    continue;

                var type = GetPieceType(cell);

                for (int y = 0; y < _boardSide; y++)
                {
                    for (int x = 0; x < _boardSide; x++)
                    {
                        var target = new Point(x, y);
                        if (CanPgnPieceLegallyMove(board, origin, target, type, color, PieceType.Queen))
                            return true;
                    }
                }
            }

            return false;
        }

        bool CanPgnPieceLegallyMove(
            byte[] board,
            Point origin,
            Point target,
            PieceType movingType,
            PieceColor movingColor,
            PieceType promotionType)
        {
            if (!CanPgnPieceReach(board, origin, target, movingType, movingColor))
                return false;

            var specialMove = GetPgnSpecialMove(board, origin, target, movingType);
            var boardAfter = CopyPgnBoard(board);
            ApplyPgnMove(boardAfter, origin, target, specialMove, promotionType);

            return !IsPgnKingInCheck(boardAfter, movingColor);
        }

        bool CanPgnPieceReach(byte[] board, Point origin, Point target, PieceType movingType, PieceColor movingColor)
        {
            if (!IsPgnPointOnBoard(origin) || !IsPgnPointOnBoard(target) || origin == target)
                return false;

            var targetCell = board[PointToIndex(target)];
            if (targetCell != 0 && GetColor(targetCell) == movingColor)
                return false;

            int dx = target.X - origin.X;
            int dy = target.Y - origin.Y;
            int absDx = Math.Abs(dx);
            int absDy = Math.Abs(dy);

            switch (movingType)
            {
                case PieceType.Pawn:
                {
                    int step = movingColor == PieceColor.White ? -1 : 1;
                    int startRank = movingColor == PieceColor.White ? _boardSide - 2 : 1;

                    if (dx == 0 && dy == step && targetCell == 0)
                        return true;

                    if (dx == 0 && dy == step * 2 && origin.Y == startRank && targetCell == 0)
                    {
                        var between = new Point(origin.X, origin.Y + step);
                        return board[PointToIndex(between)] == 0;
                    }

                    if (absDx == 1 && dy == step)
                        return targetCell != 0 && GetColor(targetCell) != movingColor;

                    return false;
                }

                case PieceType.Knight:
                    return (absDx == 1 && absDy == 2) || (absDx == 2 && absDy == 1);

                case PieceType.Bishop:
                    return absDx == absDy && IsPgnPathClear(board, origin, target);

                case PieceType.Rook:
                    return (dx == 0 || dy == 0) && IsPgnPathClear(board, origin, target);

                case PieceType.Queen:
                    return (dx == 0 || dy == 0 || absDx == absDy) && IsPgnPathClear(board, origin, target);

                case PieceType.King:
                    return absDx <= 1 && absDy <= 1;

                default:
                    return false;
            }
        }

        bool IsPgnPathClear(byte[] board, Point origin, Point target)
        {
            int dx = Math.Sign(target.X - origin.X);
            int dy = Math.Sign(target.Y - origin.Y);
            var current = new Point(origin.X + dx, origin.Y + dy);

            while (current != target)
            {
                if (!IsPgnPointOnBoard(current) || board[PointToIndex(current)] != 0)
                    return false;

                current = new Point(current.X + dx, current.Y + dy);
            }

            return true;
        }

        SpecialMoves GetPgnSpecialMove(byte[] board, Point origin, Point target, PieceType movingType)
        {
            if (movingType == PieceType.King && Math.Abs(origin.X - target.X) == 2)
                return SpecialMoves.Castling;

            if (movingType == PieceType.Pawn)
            {
                if ((target.Y == 0 && GetColor(board[PointToIndex(origin)]) == PieceColor.White) ||
                    (target.Y == _boardSide - 1 && GetColor(board[PointToIndex(origin)]) == PieceColor.Black))
                    return SpecialMoves.Promotion;

                if (Math.Abs(origin.X - target.X) == 1 && board[PointToIndex(target)] == 0)
                    return SpecialMoves.EnPassant;
            }

            return SpecialMoves.None;
        }

        void ApplyPgnMove(byte[] board, Point origin, Point target, SpecialMoves specialMove, PieceType promotionType)
        {
            var originIndex = PointToIndex(origin);
            var targetIndex = PointToIndex(target);
            var movingCell = board[originIndex];

            if (specialMove == SpecialMoves.EnPassant)
                board[PointToIndex(new Point(target.X, origin.Y))] = 0;
            else if (specialMove == SpecialMoves.Castling)
            {
                var direction = origin.X - target.X < 0 ? 1 : -1;
                var distance = direction > 0 ? 3 : 4;
                var rookPos = new Point(origin.X + distance * direction, origin.Y);
                var rookIndex = PointToIndex(rookPos);
                var rook = board[rookIndex];
                board[rookIndex] = 0;
                board[PointToIndex(new Point(rookPos.X + (distance - 1) * -direction, origin.Y))] = rook;
            }

            if (specialMove == SpecialMoves.Promotion && promotionType != PieceType.None)
            {
                movingCell = (byte)(movingCell & 0xF0);
                movingCell |= ToPgnPieceValue(promotionType);
            }

            board[originIndex] = 0;
            board[targetIndex] = movingCell;
        }

        bool IsPgnCapture(byte[] board, Point origin, Point target, SpecialMoves specialMove)
        {
            if (specialMove == SpecialMoves.EnPassant)
                return true;

            var targetCell = board[PointToIndex(target)];
            return targetCell != 0 && GetColor(targetCell) != GetColor(board[PointToIndex(origin)]);
        }

        bool IsPgnKingInCheck(byte[] board, PieceColor color)
        {
            var king = FindPgnKing(board, color);
            return king.HasValue && IsPositionInDanger(king.Value, color, board);
        }

        Point? FindPgnKing(byte[] board, PieceColor color)
        {
            var pattern = color == PieceColor.Black ? (byte)0x06 : (byte)0x16;

            for (int i = 0; i < board.Length; i++)
            {
                if (board[i] == pattern)
                    return IndexToPoint(i);
            }

            return null;
        }

        PieceType InferPgnPromotionType(int moveIndex, Point promotionSquare, PieceColor movingColor)
        {
            for (int i = moveIndex + 1; i < _history.Count; i++)
            {
                var future = _history[i];

                if (future.Item1 == promotionSquare)
                    return InferPgnPromotionTypeFromMove(promotionSquare, future.Item2);

                if (future.Item2 == promotionSquare)
                    break;
            }

            var finalCell = GetCell(promotionSquare) ?? 0;
            if (finalCell != 0 && GetColor(finalCell) == movingColor)
            {
                var finalType = GetPieceType(finalCell);
                if (finalType == PieceType.Queen || finalType == PieceType.Rook ||
                    finalType == PieceType.Bishop || finalType == PieceType.Knight)
                    return finalType;
            }

            return PieceType.Queen;
        }

        PieceType InferPgnPromotionTypeFromMove(Point origin, Point target)
        {
            int dx = Math.Abs(target.X - origin.X);
            int dy = Math.Abs(target.Y - origin.Y);

            if ((dx == 1 && dy == 2) || (dx == 2 && dy == 1))
                return PieceType.Knight;

            if (dx == 0 || dy == 0)
                return PieceType.Rook;

            if (dx == dy)
                return PieceType.Bishop;

            return PieceType.Queen;
        }

        string GetPgnResult()
        {
            if (!IsGameOver)
                return "*";

            var sideToMove = (PieceColor)(_currentMove % 2);
            var king = FindKing(sideToMove);

            if (king.HasValue && IsPositionInDanger(king.Value, sideToMove, Board))
                return sideToMove == PieceColor.White ? "0-1" : "1-0";

            return "1/2-1/2";
        }

        string BuildPgnFen(byte[] board, int plyCount, Castling castling, string enPassantTarget, int halfmoveClock)
        {
            var sb = new StringBuilder();

            for (int y = 0; y < _boardSide; y++)
            {
                int empty = 0;

                for (int x = 0; x < _boardSide; x++)
                {
                    var cell = board[x + y * _boardSide];

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

            sb.Append((plyCount & 1) == 0 ? " w " : " b ");
            sb.Append(BuildPgnFenCastlingRights(castling));
            sb.Append(' ');
            sb.Append(string.IsNullOrEmpty(enPassantTarget) ? "-" : enPassantTarget);
            sb.Append(' ');
            sb.Append(halfmoveClock < 0 ? 0 : halfmoveClock);
            sb.Append(' ');
            sb.Append((plyCount / 2) + 1);

            return sb.ToString();
        }

        string BuildPgnFenCastlingRights(Castling castling)
        {
            var sb = new StringBuilder();

            if ((castling & Castling.WhiteRookRight) != 0)
                sb.Append('K');
            if ((castling & Castling.WhiteRookLeft) != 0)
                sb.Append('Q');
            if ((castling & Castling.BlackRookRight) != 0)
                sb.Append('k');
            if ((castling & Castling.BlackRookLeft) != 0)
                sb.Append('q');

            return sb.Length == 0 ? "-" : sb.ToString();
        }

        string GetPgnEnPassantTarget(Point origin, Point target, PieceType movingType)
        {
            if (movingType == PieceType.Pawn && Math.Abs(origin.Y - target.Y) == 2)
                return ToChessMove(new Point(origin.X, (origin.Y + target.Y) / 2));

            return "-";
        }

        byte[] BuildPgnInitialBoard()
        {
            var board = new byte[64];

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

        byte[] CopyPgnBoard(byte[] board)
        {
            var copy = new byte[board.Length];
            Array.Copy(board, copy, board.Length);
            return copy;
        }

        string ToPgnCoordinateMove(Point origin, Point target, PieceType promotionType)
        {
            var sb = new StringBuilder();
            sb.Append(ToChessMove(origin));
            sb.Append(ToChessMove(target));

            if (promotionType != PieceType.None && promotionType != PieceType.Pawn)
            {
                sb.Append('=');
                sb.Append(GetPgnPieceLetter(promotionType));
            }

            return sb.ToString();
        }

        char GetPgnPieceLetter(PieceType type)
        {
            switch (type)
            {
                case PieceType.Knight:
                    return 'N';
                case PieceType.Bishop:
                    return 'B';
                case PieceType.Rook:
                    return 'R';
                case PieceType.Queen:
                    return 'Q';
                case PieceType.King:
                    return 'K';
                default:
                    return ' ';
            }
        }

        byte ToPgnPieceValue(PieceType type)
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

        PieceColor GetPgnOppositeColor(PieceColor color)
        {
            return color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        }

        bool IsPgnPointOnBoard(Point point)
        {
            return point.X >= 0 && point.X < _boardSide && point.Y >= 0 && point.Y < _boardSide;
        }

        char GetPgnFile(int x)
        {
            return (char)('a' + x);
        }

        string EscapePgnTagValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        string EscapePgnComment(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("}", ")");
        }

        void AppendWrappedPgnMoveText(StringBuilder sb, string moveText, int maxLineLength)
        {
            if (string.IsNullOrEmpty(moveText))
            {
                sb.AppendLine();
                return;
            }

            var tokens = moveText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int lineLength = 0;

            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];

                if (lineLength > 0 && lineLength + token.Length + 1 > maxLineLength)
                {
                    sb.AppendLine();
                    lineLength = 0;
                }
                else if (lineLength > 0)
                {
                    sb.Append(' ');
                    lineLength++;
                }

                sb.Append(token);
                lineLength += token.Length;
            }

            sb.AppendLine();
        }
    }
}
