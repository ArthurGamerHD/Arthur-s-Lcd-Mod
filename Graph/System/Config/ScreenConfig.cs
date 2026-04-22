using System;
using System.Linq;
using Graph.Helpers;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;
using Generated;

namespace Graph.System.Config
{
    [ProtoContract]
    public partial class ScreenConfig : IClonableContract<ScreenConfig>
    {
        bool _customizedColors;
        public const float MAX_SCALE = 10f;
        public const float MIN_SCALE = 0.1f;

        public IMyTerminalBlock ParentBlock { private get; set; }
        
        public ScreenConfig()
        {
            //Required by Protobuf
        }

        public ScreenConfig(int i, IMyTerminalBlock parentBlock)
        {
            ScreenIndex = i;
            ParentBlock = parentBlock;
            ResetDefaultColors();
        }

        public void ResetDefaultColors()
        {
            if(ParentBlock != null)
                HeaderColor = FactionHelper.GetIconColor(ParentBlock);

            ErrorColor = new Color(96, 32, 32);
            WarningColor = new Color(224, 160, 16);
        }

        [ProtoMember(1)] public int ScreenIndex { get; set; }

        [ProtoMember(2)] public Color HeaderColor { get; set; }

        [ProtoMember(11)] public bool TitleVisible { get; set; } = true;

        [ProtoMember(3)] public long[] SelectedBlocks { get; set; } = Array.Empty<long>();

        [ProtoMember(4)] public string[] SelectedGroups { get; set; } = Array.Empty<string>();

        [ProtoMember(5)] public string[] SelectedDefinition { get; set; } = Array.Empty<string>();

        [ProtoMember(6)] public string[] SelectedCategories { get; set; } = Array.Empty<string>();

        [ProtoMember(7)] public float InternalScale { get; set; } = 1;

        public MyDefinitionId[] SelectedItems
        {
            get
            {
                try
                {
                    return SelectedDefinition.Select(MyDefinitionId.Parse).ToArray();
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, this);
                }

                return Array.Empty<MyDefinitionId>();
            }
            set { SelectedDefinition = value.Select(a => a.ToString()).ToArray(); }
        }

        public float Scale
        {
            get { return MathHelper.Clamp(InternalScale, MIN_SCALE, MAX_SCALE); }
            set { InternalScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); }
        }

        [ProtoMember(8)] public long ReferenceBlock { get; set; }

        [ProtoMember(9)] public bool DrawLines { get; set; }
        [ProtoMember(10)] public int SortInternal { get; set; }

        [ProtoMember(12)] public int DisplayInternal { get; set; }

        [ProtoMember(13)] public bool HideEmpty { get; set; } = true;

        [ProtoMember(14)] public Color ErrorColor { get; set; } = new Color(96, 32, 32);
        [ProtoMember(15)] public Color WarningColor { get; set; } = new Color(224, 160, 16);
        [ProtoMember(16)] public float Rotation { get; set; }

        [ProtoMember(17)]
        public bool CustomizedColors
        {
            get
            {
                return _customizedColors;
            }
            set
            {
                if(!value)
                    ResetDefaultColors();

                _customizedColors = value;
            }
        }

        [ProtoMember(18)] public int GraphWindowIndex { get; set; } = 2;

        public SortMethod SortMethod
        {
            get { return (SortMethod)SortInternal; }
            set { SortInternal = (int)value; }
        }

        public DisplayMode DisplayMode
        {
            get { return (DisplayMode)DisplayInternal; }
            set { DisplayInternal = (int)value; }
        }

    }
}
