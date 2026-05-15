using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Generated;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Common.Config.Models
{
    [ProtoContract]
    [ProtoInclude(101, typeof(ScreenConfigColorable))]
    [XmlInclude(typeof(ScreenConfigColorable))]
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


        [ProtoMember(1)] public int ScreenIndex { get; set; }
        [ProtoMember(11)] public bool TitleVisible { get; set; } = true;
        [ProtoMember(7)] public float InternalScale { get; set; } = 1;

        public float Scale
        {
            get { return MathHelper.Clamp(InternalScale, MIN_SCALE, MAX_SCALE); }
            set { InternalScale = MathHelper.Clamp(value, MIN_SCALE, MAX_SCALE); }
        }

        [ProtoMember(9)] public bool DrawLines { get; set; }
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
