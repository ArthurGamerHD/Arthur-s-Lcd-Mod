using System.Collections.Generic;
using Generated;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Common.Config.Models
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
        
        [ProtoMember(12)] public int DisplayMode { get; set; }

        [ProtoMember(99)]
        public Dictionary<string, byte[]> CustomData { get; set; } = new Dictionary<string, byte[]>();

        public byte[] GetCustomData(string key)
        {
            byte[] data;
            if (CustomData != null && CustomData.TryGetValue(key, out data))
                return data;
            return null;
        }

        public void SetCustomData(string key, byte[] data)
        {
            if (CustomData == null)
                CustomData = new Dictionary<string, byte[]>();

            CustomData[key] = data;
        }
    }
}
