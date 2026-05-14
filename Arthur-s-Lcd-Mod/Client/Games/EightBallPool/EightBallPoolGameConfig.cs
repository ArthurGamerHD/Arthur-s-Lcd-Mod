using ProtoBuf;

namespace LcdMod.Client.Games.EightBallPool
{
    [ProtoContract]
    public sealed class EightBallPoolGameConfig
    {
        [ProtoMember(1)] public float[] Positions { get; set; }
        [ProtoMember(2)] public float[] Velocities { get; set; }
        [ProtoMember(3)] public bool[] Pocketed { get; set; }
        [ProtoMember(4)] public bool CueBallInHand { get; set; }
        [ProtoMember(5)] public int ShotCount { get; set; }
        [ProtoMember(6)] public int LastPocketedBall { get; set; }
        [ProtoMember(7)] public int CurrentPlayer { get; set; }
        [ProtoMember(8)] public int PlayerOneGroup { get; set; }
        [ProtoMember(9)] public bool GameOver { get; set; }
        [ProtoMember(10)] public int Winner { get; set; }
        [ProtoMember(11)] public int[] CapturedBy { get; set; }
        [ProtoMember(12)] public string LastStatusMessage { get; set; }
        [ProtoMember(13)] public bool ShotInProgress { get; set; }
        [ProtoMember(14)] public bool ShotCueBallPocketed { get; set; }
        [ProtoMember(15)] public int FirstHitBall { get; set; }
        [ProtoMember(16)] public bool[] ShotStartPocketed { get; set; }
        [ProtoMember(17)] public int[] ShotPocketedBalls { get; set; }
        [ProtoMember(18)] public int LastPlayer { get; set; }
        [ProtoMember(19)] public bool CheatMode { get; set; }
        [ProtoMember(20)] public int[] CaptureOrder { get; set; }
    }
}
