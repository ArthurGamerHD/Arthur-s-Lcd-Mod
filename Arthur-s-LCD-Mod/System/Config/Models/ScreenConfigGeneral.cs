using Generated;
using Graph.Apps.Games.Chess;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace Graph.System.Config.Models
{
    [ProtoContract]
    [ProtoInclude(101, typeof(ScreenConfigColorable))]
    public partial class ScreenConfigGeneral : IClonableContract, IScreenConfig
    {
        public const float MAX_SCALE = 10f;
        public const float MIN_SCALE = 0.1f;

        public IMyTerminalBlock ParentBlock { protected get; set; }
        public virtual int Id => 4;

        public ScreenConfigGeneral()
        {
        }

        public ScreenConfigGeneral(int i, IMyTerminalBlock parentBlock)
        {
            ScreenIndex = i;
            ParentBlock = parentBlock;
        }
        

        [ProtoMember(1)] public int ScreenIndex { get; set; }
        [ProtoMember(11)] public bool TitleVisible { get; set; } = true;
        [ProtoMember(7)] public float InternalScale { get; set; } = 1;

        public float Scale
        {
            get { return MathHelper.Clamp(InternalScale, MIN_SCALE, MAX_SCALE); }
            set { InternalScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); }
        }

        [ProtoMember(9)] public bool DrawLines { get; set; }

        [ProtoMember(20)] public long OreScannerReferenceId { get; set; }
        [ProtoMember(21)] public float OreScannerConeBias { get; set; } = 0f;
        
        public DisplayMode DisplayMode
        {
            get { return (DisplayMode)DisplayInternal; }
            set { DisplayInternal = (int)value; }
        }
        
        [ProtoMember(12)] public int DisplayInternal { get; set; }
        
        [ProtoMember(99)]
        public byte[] CustomData { get; set; }
    }
}
