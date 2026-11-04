using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    public enum MediaStreamControlIntent
    {
        Request = 1,
        Invite = 2,
        Refused = 3,
        Accepted = 4,
        Clients = 5,
        Refresh = 6,
        Close = 7
    }

    public enum MediaStreamContentKind
    {
        Pcm = 0,

        // A chunked Matroska video: segment -1 is the chunk index, the others are the chunks around the sender's
        // playhead, each sent in pieces by byte offset.
        Video = 1
    }

    [ProtoContract]
    [NetworkPayload(17)]
    public sealed partial class PacketMediaStreamControl
    {
        [ProtoMember(1)] public MediaStreamControlIntent Intent;
        [ProtoMember(2)] public long ClientStreamId;
        [ProtoMember(3)] public long ServerStreamId;
        [ProtoMember(4)] public ulong RequestedBySteamId;
        [ProtoMember(5)] public long BlockEntityId;
        [ProtoMember(6)] public int SurfaceIndex;
        [ProtoMember(7)] public int AppTypeId;
        [ProtoMember(8)] public string Title;
        [ProtoMember(9)] public long TotalDurationTicks;
        [ProtoMember(10)] public long ServerFrame;
        [ProtoMember(11)] public bool StopPlayback;
        [ProtoMember(12)] public ulong[] ListenerSteamIds;
        [ProtoMember(13)] public MediaStreamContentKind ContentKind;

    }

    [ProtoContract]
    [NetworkPayload(18)]
    public sealed partial class PacketRequestMediaStreamChunk
    {
        [ProtoMember(1)] public long ClientStreamId;
        [ProtoMember(2)] public int ChunkIndex;
        [ProtoMember(3)] public byte[] PcmBytes;
        [ProtoMember(4)] public long DurationTicks;
        [ProtoMember(5)] public bool IsFinal;
        [ProtoMember(6)] public int Offset;
        [ProtoMember(7)] public int Segment;
        [ProtoMember(8)] public int SegmentLength;
        [ProtoMember(9)] public long PositionTicks;

    }

    [ProtoContract]
    [NetworkPayload(19)]
    public sealed partial class PacketSyncMediaStreamChunk
    {
        [ProtoMember(1)] public long ServerStreamId;
        [ProtoMember(2)] public int ChunkIndex;
        [ProtoMember(3)] public byte[] PcmBytes;
        [ProtoMember(4)] public long DurationTicks;
        [ProtoMember(5)] public bool IsFinal;
        [ProtoMember(6)] public long ServerFrame;
        [ProtoMember(7)] public int Offset;
        [ProtoMember(8)] public int Segment;
        [ProtoMember(9)] public int SegmentLength;
        [ProtoMember(10)] public long PositionTicks;

    }
}
