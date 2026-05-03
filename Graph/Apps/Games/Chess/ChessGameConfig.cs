using ProtoBuf;

namespace Graph.Apps.Games.Chess
{

    [ProtoContract]
    public sealed class ChessGameConfig
    {
        [ProtoMember(1)]
        public byte[] Board { get; set; }

        [ProtoMember(2)]
        public int Move { get; set; }

        [ProtoMember(3)]
        public int Castling { get; set; }

        [ProtoMember(4)]
        public string History { get; set; }

        [ProtoMember(5)]
        public long PlayingAsWhitePlayerId { get; set; }

        [ProtoMember(6)]
        public long PlayingAsBlackPlayerId { get; set; }

        [ProtoMember(7)]
        public int SessionId { get; set; }

        [ProtoMember(8)]
        public int SelectedBot { get; set; }

        [ProtoMember(9)]
        public bool ShowDangers { get; set; }

        [ProtoMember(10)]
        public bool PlayingAsBlack { get; set; }
    }
}