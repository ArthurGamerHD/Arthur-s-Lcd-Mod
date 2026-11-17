using ProtoBuf;

namespace LcdMod.Common.Market
{
    [ProtoContract]
    public sealed class DroplineDestinationDto
    {
        public const int SelectedFlag = 1, LinkedFlag = 2, BusyFlag = 4;

        [ProtoMember(1)]
        public long PadId;

        [ProtoMember(2)]
        public string Name;

        [ProtoMember(3)]
        public int Flags;
    }
}
