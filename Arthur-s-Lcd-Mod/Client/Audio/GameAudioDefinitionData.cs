#if EXPERIMENTAL
using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.ModAPI;
using VRage.Game;

namespace LcdMod.Client.Audio
{
    [XmlRoot("Sound")]
    public sealed class AudioWavesDefinition
    {
        [XmlArray("Waves")]
        [XmlArrayItem("Wave")]
        public List<WaveData> Waves { get; set; }

        public static implicit operator AudioWavesDefinition(MyAudioDefinition definition)
        {
            if (definition == null || MyAPIGateway.Utilities == null)
                return null;

            var builder = definition.GetObjectBuilder() as
                MyObjectBuilder_AudioDefinition;
            if (builder == null)
                return null;

            string xml = MyAPIGateway.Utilities.SerializeToXML(builder);
            return MyAPIGateway.Utilities
                .SerializeFromXML<AudioWavesDefinition>(xml);
        }
    }

    public sealed class WaveData
    {
        public string Start { get; set; }
        public string Loop { get; set; }
        public string End { get; set; }
    }
}
#endif
