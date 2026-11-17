using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    public enum DroplineRequestKind : byte
    {
        Quote = 1,
        Submit = 2,
        Destinations = 3,
        SelectDestination = 4,
        AddToCart = 5,
        GetCart = 6,
        SetCartQuantity = 7,
        ClearCart = 8,
        QuoteCart = 9
    }

    [ProtoContract]
    [NetworkPayload(25)]
    public sealed partial class PacketDroplineRequest
    {
        [ProtoMember(1)]
        public uint RequestId;

        [ProtoMember(2)]
        public DroplineRequestKind Kind;

        [ProtoMember(3)]
        public long HostBlockEntityId;

        [ProtoMember(4)]
        public long StationId;

        [ProtoMember(5)]
        public string TypeId;

        [ProtoMember(6)]
        public string SubtypeId;

        [ProtoMember(7)]
        public int Quantity;

        [ProtoMember(8)]
        public string QuoteId;

        // 0 = the Shop's current destination.
        [ProtoMember(9)]
        public long PadId;
    }
}
