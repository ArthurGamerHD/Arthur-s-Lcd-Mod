using ProtoBuf;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigRaycast : ScreenConfigGeneral
    {
        [ProtoMember(24)] public int RelationOverlay { get; set; } = 1;
        
        [ProtoMember(25)] public float RenderScale { get; set; } = .2f; 
        [ProtoMember(26)] public int RaysPerTick { get; set; } = 32;
        
        public override int Id => 15;
    }
}
