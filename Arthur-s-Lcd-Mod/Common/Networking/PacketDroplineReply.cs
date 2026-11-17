using System.Collections.Generic;
using Generated;
using LcdMod.Common.Market;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(26)]
    public sealed partial class PacketDroplineReply
    {
        [ProtoMember(1)]
        public uint RequestId;

        [ProtoMember(2)]
        public string Error;

        [ProtoMember(3)]
        public string QuoteId;

        [ProtoMember(4)]
        public long Subtotal;

        [ProtoMember(5)]
        public long Fee;

        [ProtoMember(6)]
        public long Total;

        [ProtoMember(7)]
        public int EtaSeconds;

        [ProtoMember(8)]
        public List<DroplineDestinationDto> Destinations;

        [ProtoMember(9)]
        public List<DroplineCartLineDto> CartLines;
    }
}
