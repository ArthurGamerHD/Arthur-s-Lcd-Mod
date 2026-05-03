using Graph.Apps.Games.Chess.Enum;
using Sandbox.ModAPI.Ingame;
using VRageMath;

namespace Graph.Apps.Games.Chess
{
    public partial class ChessGame
    {
        static PieceType GetPieceType(byte? cell)
        {
            if (cell == null)
                return PieceType.Empty;
            return (PieceType)(cell.Value & 0xEF);
        }

        public int PointToIndex(Point point) => point.X + point.Y * _boardSide;

        RectangleF GetGridCell(int index) =>
            _playingAsBlack ? _gridCells[_gridCells.Length - index - 1] : _gridCells[index];


        string ToChessMove(Point point) => $"{(char)('a' + point.X)}{_boardSide - point.Y}";

        Point? FromChessMove(string point) => point.Length == 2 && char.IsLetter(point[0]) && char.IsNumber(point[1]) ? 
            (Point?)new Point(point[0] - 'a',
                    _boardSide - // reverse-indexing (chess "y" starts at 8 ends at 1 (indexes 7-0))
                    (point[1] - 48))
                : // magic number to convert chars 0-9 to ints 0-9
                null;

        byte? GetCell(Point origin)
        {
            if (origin.X >= 0 && origin.X < _boardSide && origin.Y >= 0 && origin.Y < _boardSide)
                return _board[PointToIndex(origin)];
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