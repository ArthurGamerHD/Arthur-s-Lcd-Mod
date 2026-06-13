using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Generated;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    [ProtoInclude(102, typeof(ScreenConfigWithReferenceBlock))]
    [XmlInclude(typeof(ScreenConfigWithReferenceBlock))]
    [ProtoInclude(103, typeof(ScreenConfigWithFilters))]
    [XmlInclude(typeof(ScreenConfigWithFilters))]
    [ProtoInclude(105, typeof(ScreenConfigRadar))]
    [XmlInclude(typeof(ScreenConfigRadar))]
    [ProtoInclude(107, typeof(ScreenConfigPower))]
    [XmlInclude(typeof(ScreenConfigPower))]
    [ProtoInclude(108, typeof(ScreenConfigStarMap))]
    [XmlInclude(typeof(ScreenConfigStarMap))]
    [ProtoInclude(110, typeof(ScreenConfigDiagnostic))]
    [XmlInclude(typeof(ScreenConfigDiagnostic))]
    [ProtoInclude(111, typeof(ScreenConfigDocking))]
    [XmlInclude(typeof(ScreenConfigDocking))]
    [ProtoInclude(115, typeof(ScreenConfigMarkdown))]
    [XmlInclude(typeof(ScreenConfigMarkdown))]
    [ProtoInclude(113, typeof(ScreenConfigRaycast))]
    [XmlInclude(typeof(ScreenConfigRaycast))]
    [ProtoInclude(114, typeof(ScreenConfigRenderProxy))]
    [XmlInclude(typeof(ScreenConfigRenderProxy))]
    [ProtoInclude(116, typeof(ScreenConfigButtonPanel))]
    [XmlInclude(typeof(ScreenConfigButtonPanel))]
    [ProtoInclude(117, typeof(ScreenConfigDigitalPictureFrames))]
    [XmlInclude(typeof(ScreenConfigDigitalPictureFrames))]
    [ProtoInclude(118, typeof(ScreenConfigCargoActions))]
    [XmlInclude(typeof(ScreenConfigCargoActions))]
    [ProtoInclude(119, typeof(ScreenConfigNpcMarket))]
    [XmlInclude(typeof(ScreenConfigNpcMarket))]
    public partial class ScreenConfigGeneral : IClonableContract, IScreenConfig
    {
        public const float MAX_SCALE = 10f;
        public const float MIN_SCALE = 0.1f;

        [XmlIgnore] // runtime-only
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
        
        [ProtoMember(1)] public int ScreenIndex { get; set; }
        [ProtoMember(11)] public bool TitleVisible { get; set; } = true;
        [ProtoMember(7)] public float InternalScale { get; set; } = 1;

        public float Scale
        {
            get { return MathHelper.Clamp(InternalScale, MIN_SCALE, MAX_SCALE); }
            set { InternalScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); }
        }

        [ProtoMember(9)] public bool DrawLines { get; set; }
        
        [ProtoMember(22)] public float CursorScale { get; set; } = 1f;
        [ProtoMember(23)] public bool RequiresAlt { get; set; } = true;
        [ProtoMember(27)] public int ReferenceMode { get; set; } = 0;
        
        [ProtoMember(12)] public int DisplayMode { get; set; }

        [ProtoMember(99)]
        [XmlIgnore]
        public Dictionary<string, byte[]> CustomData { get; set; } = new Dictionary<string, byte[]>();
        
        [ProtoIgnore]
        [XmlArray("CustomData")]
        [XmlArrayItem("Entry")]
        public CustomDataXmlEntry[] CustomDataXml
        {
            get
            {
                if (CustomData == null || CustomData.Count == 0)
                    return null;

                return CustomData
                    .Where(entry => !string.IsNullOrEmpty(entry.Key) && entry.Value != null)
                    .Select(entry => new CustomDataXmlEntry
                    {
                        Key = entry.Key,
                        Value = Convert.ToBase64String(entry.Value)
                    })
                    .ToArray();
            }
            set
            {
                CustomData = DecodeCustomData(value);
            }
        }

        static Dictionary<string, byte[]> DecodeCustomData(IEnumerable<CustomDataXmlEntry> entries)
        {
            var result = new Dictionary<string, byte[]>();

            if (entries == null)
                return result;

            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Key) || string.IsNullOrEmpty(entry.Value))
                    continue;

                try
                {
                    result[entry.Key] = Convert.FromBase64String(entry.Value);
                }
                catch (FormatException)
                {
                    // Keep XML editing resilient: a malformed custom-data entry should
                    // not make the entire screen configuration impossible to load.
                }
            }

            return result;
        }

        public byte[] GetCustomData(string key)
        {
            byte[] data;
            if (CustomData != null && CustomData.TryGetValue(key, out data))
                return data;
            return null;
        }

        public void SetCustomData(string key, byte[] data)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (CustomData == null)
                CustomData = new Dictionary<string, byte[]>();

            if (data == null)
                CustomData.Remove(key);
            else
                CustomData[key] = data;
        }
    }

    public class CustomDataXmlEntry
    {
        [XmlAttribute]
        public string Key { get; set; }

        [XmlText]
        public string Value { get; set; }
    }
}
