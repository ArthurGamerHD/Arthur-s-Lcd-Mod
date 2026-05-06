namespace LcdMod.Client.Games.Chess.TinyChessChallenge
{
    // Must match ChessChallenge.API.PieceType exactly.
    // Many original bots rely on these numeric values for packed tables,
    // piece-value arrays, move ordering, and attack lookups.
    public enum PieceType
    {
        None = 0,
        Pawn = 1,
        Knight = 2,
        Bishop = 3,
        Rook = 4,
        Queen = 5,
        King = 6
    }
}
