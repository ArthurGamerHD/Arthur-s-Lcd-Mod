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
            return (PieceType)(cell.Value & 0xEF);
        }

        public int PointToIndex(Point point) => point.X + point.Y * _boardSide;

        public RectangleF GetGridCell(int index) =>
            _playingAsBlack ? _gridCells[_gridCells.Length - index - 1] : _gridCells[index];


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