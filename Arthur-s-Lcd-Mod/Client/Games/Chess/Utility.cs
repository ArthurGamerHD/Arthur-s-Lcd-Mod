using LcdMod.Client.Games.Chess.Enum;
using LcdMod.Client.Games.Chess.TinyChessChallenge;
using VRageMath;

namespace LcdMod.Client.Games.Chess
{
    public partial class ChessGame
    {
        static PieceType GetPieceType(byte? cell)
        {
            if (cell == null)
                return PieceType.None;

            // Board bytes use the original LCD texture ids in the low nibble:
            // 1 pawn, 2 rook, 3 knight, 4 bishop, 5 queen, 6 king.
            // ChessChallenge.API.PieceType uses the challenge order:
            // 1 pawn, 2 knight, 3 bishop, 4 rook, 5 queen, 6 king.
            // Keep these separate so original bots that use (int)PieceType keep working.
            switch (cell.Value & 0x0F)
            {
                case 0x01:
                    return PieceType.Pawn;
                case 0x02:
                    return PieceType.Rook;
                case 0x03:
                    return PieceType.Knight;
                case 0x04:
                    return PieceType.Bishop;
                case 0x05:
                    return PieceType.Queen;
                case 0x06:
                    return PieceType.King;
                default:
                    return PieceType.None;
            }
        }

        public int PointToIndex(Point point) => point.X + point.Y * _boardSide;

        Point BoardPointFromBoardIndex(int index) =>
            new Point(index % _boardSide, index / _boardSide);

        int BoardIndexToVisualIndex(int boardIndex) =>
            _playingAsBlack ? _gridCells.Length - boardIndex - 1 : boardIndex;

        int VisualIndexToBoardIndex(int visualIndex) =>
            _playingAsBlack ? _gridCells.Length - visualIndex - 1 : visualIndex;

        public RectangleF GetGridCell(int index) =>
            _gridCells[BoardIndexToVisualIndex(index)];


        string ToChessMove(Point point) => $"{(char)('a' + point.X)}{_boardSide - point.Y}";

        byte? GetCell(Point origin)
        {
            if (origin.X >= 0 && origin.X < _boardSide && origin.Y >= 0 && origin.Y < _boardSide)
                return Board[PointToIndex(origin)];
            return null;
        }

        byte? GetCell(Point origin, byte[] board)
        {
            if (origin.X >= 0 && origin.X < _boardSide && origin.Y >= 0 && origin.Y < _boardSide)
                return board[PointToIndex(origin)];
            return null;
        }
        
        static PieceColor GetColor(byte cell) =>
            cell == 0 ? PieceColor.None : (cell & 0x10) == 0x10 ? PieceColor.White : PieceColor.Black;
    }
}