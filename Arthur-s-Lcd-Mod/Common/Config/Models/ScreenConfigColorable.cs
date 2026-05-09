using LcdMod.Common.Helpers;
using ProtoBuf;
using VRageMath;
using ScreenConfigDiagnostic = LcdMod.Common.Config.Models.Apps.ScreenConfigDiagnostic;
using ScreenConfigDocking = LcdMod.Common.Config.Models.Apps.ScreenConfigDocking;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;
using ScreenConfigRadar = LcdMod.Common.Config.Models.Apps.ScreenConfigRadar;
using ScreenConfigStarMap = LcdMod.Common.Config.Models.Apps.ScreenConfigStarMap;
using ScreenConfigWithFilters = LcdMod.Common.Config.Models.Apps.ScreenConfigWithFilters;
using ScreenConfigWithReferenceBlock = LcdMod.Common.Config.Models.Apps.ScreenConfigWithReferenceBlock;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    [ProtoInclude(102, typeof(ScreenConfigWithReferenceBlock))]
    [ProtoInclude(103, typeof(ScreenConfigWithFilters))]
    [ProtoInclude(105, typeof(ScreenConfigRadar))]
    [ProtoInclude(107, typeof(ScreenConfigPower))]
    [ProtoInclude(108, typeof(ScreenConfigStarMap))]
    [ProtoInclude(110, typeof(ScreenConfigDiagnostic))]
    [ProtoInclude(111, typeof(ScreenConfigDocking))]
    [ProtoInclude(112, typeof(ScreenConfigInteractive))]
    public partial class ScreenConfigColorable : ScreenConfigGeneral
    {
        
        public override int Id => 5;
        
        [ProtoMember(2)] public OptionalValue<Color> HeaderColorInternal { get; set; } = new OptionalValue<Color>();
        [ProtoMember(14)] public OptionalValue<Color> ErrorColorInternal { get; set; } = new OptionalValue<Color>();
        [ProtoMember(15)] public OptionalValue<Color> WarningColorInternal { get; set; } = new OptionalValue<Color>();

        public Color HeaderColor
        {
            get { return HeaderColorInternal.Get(!CustomizedColors, () => DefaultHeaderColor); }
            set { HeaderColorInternal.Set(value); }
        }

        public Color ErrorColor
        {
            get { return ErrorColorInternal.Get(!CustomizedColors, () => _defaultErrorColor); }
            set { ErrorColorInternal.Set(value); }
        }

        public Color WarningColor
        {
            get { return WarningColorInternal.Get(!CustomizedColors, () => _defaultWarningColor); }
            set { WarningColorInternal.Set(value); }
        }

        [ProtoMember(17)] public bool CustomizedColors { get; set; }

        public void ResetDefaultColors()
        {
            HeaderColorInternal.Clear();
            ErrorColorInternal.Clear();
            WarningColorInternal.Clear();
        }

        Color DefaultHeaderColor => ParentBlock == null ? FactionHelperCommon.DefaultColor : FactionHelperCommon.GetIconColor(ParentBlock);

        static Color _defaultErrorColor = new Color(96, 32, 32);

        static Color _defaultWarningColor = new Color(224, 160, 16);
    }
}
