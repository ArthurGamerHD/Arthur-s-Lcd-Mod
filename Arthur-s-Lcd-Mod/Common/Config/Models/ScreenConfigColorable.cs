using System.Xml.Serialization;
using LcdMod.Common.Helpers;
using ProtoBuf;
using VRageMath;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    [ProtoInclude(112, typeof(ScreenConfigInteractive))]
    [XmlInclude(typeof(ScreenConfigInteractive))]
    public partial class ScreenConfigColorable : ScreenConfigGeneral
    {
        public override int Id => 5;

        [ProtoMember(2)] public OptionalValue<Color> HeaderColorInternal { get; set; } = new OptionalValue<Color>();
        [ProtoMember(14)] public OptionalValue<Color> ErrorColorInternal { get; set; } = new OptionalValue<Color>();
        [ProtoMember(15)] public OptionalValue<Color> WarningColorInternal { get; set; } = new OptionalValue<Color>();

        [XmlIgnore]
        public Color HeaderColor
        {
            get { return HeaderColorInternal.Get(!CustomizedColors, () => DefaultHeaderColor); }
            set { HeaderColorInternal.Set(value); }
        }

        [XmlIgnore]
        public Color ErrorColor
        {
            get { return ErrorColorInternal.Get(!CustomizedColors, () => _defaultErrorColor); }
            set { ErrorColorInternal.Set(value); }
        }

        [XmlIgnore]
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

        Color DefaultHeaderColor => ParentBlock == null
            ? FactionHelperCommon.DefaultColor
            : FactionHelperCommon.GetIconColor(ParentBlock);

        static Color _defaultErrorColor = new Color(96, 32, 32);

        static Color _defaultWarningColor = new Color(224, 160, 16);
    }
}
