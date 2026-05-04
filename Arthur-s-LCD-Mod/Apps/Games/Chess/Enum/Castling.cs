using System;

namespace Graph.Apps.Games.Chess.Enum
{
    [Flags]
    public enum Castling
    {
        None = 0x0,
        WhiteRookLeft = 0x1,
        WhiteRookRight = 0x2,
        WhiteKing = WhiteRookLeft | WhiteRookRight,

        BlackRookRight = 0x4,
        BlackRookLeft = 0x8,
        BlackKing = BlackRookRight | BlackRookLeft,
        
        Full = WhiteKing | BlackKing
    }
}