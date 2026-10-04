using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace LcdMod.Server
{
    [Serializable]
    [XmlRoot("EconomyKnownStations")]
    public sealed class EconomyKnownStationsFile
    {
        [XmlAttribute("version")]
        public int Version = 1;

        [XmlArray("Players")]
        [XmlArrayItem("Player")]
        public List<EconomyKnownStationsPlayer> Players = new List<EconomyKnownStationsPlayer>();
    }

    [Serializable]
    public sealed class EconomyKnownStationsPlayer
    {
        [XmlAttribute("identityId")]
        public long IdentityId;

        [XmlAttribute("name")]
        public string Name;

        [XmlArray("Stations")]
        [XmlArrayItem("Station")]
        public List<long> StationIds = new List<long>();
    }
}
