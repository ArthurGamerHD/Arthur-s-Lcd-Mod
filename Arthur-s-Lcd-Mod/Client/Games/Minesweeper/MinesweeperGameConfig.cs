using ProtoBuf;

namespace LcdMod.Client.Games.Minesweeper
{
    [ProtoContract]
    public sealed class MinesweeperGameConfig
    {
        [ProtoMember(1)]
        public int Width { get; set; }

        [ProtoMember(2)]
        public int Height { get; set; }

        [ProtoMember(3)]
        public int MineCount { get; set; }

        [ProtoMember(4)]
        public int State { get; set; }

        [ProtoMember(5)]
        public int RevealedCount { get; set; }

        [ProtoMember(6)]
        public int FlagsUsed { get; set; }

        [ProtoMember(7)]
        public int Seed { get; set; }

        [ProtoMember(8)]
        public byte[] Cells { get; set; }

        [ProtoMember(9)]
        public string History { get; set; }

        [ProtoMember(10)]
        public bool FlagMode { get; set; }

        [ProtoMember(11)]
        public bool MinesPlaced { get; set; }

        [ProtoMember(12)]
        public string UnknownCells { get; set; }

        [ProtoMember(13)]
        public int ElapsedSeconds { get; set; }

        [ProtoMember(14)]
        public long TimerStartedFrame { get; set; }

        [ProtoMember(15)]
        public bool TimerRunning { get; set; }
    }
}
