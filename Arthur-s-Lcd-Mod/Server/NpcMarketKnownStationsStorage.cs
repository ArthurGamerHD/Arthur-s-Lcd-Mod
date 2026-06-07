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

    internal static class NpcMarketKnownStationsStorage
    {
        public static EconomyKnownStationsFile CreateStorageModel(
            Dictionary<long, HashSet<long>> ledger,
            Dictionary<long, string> playerNameHints)
        {
            var file = new EconomyKnownStationsFile();
            if (ledger == null)
                return file;

            var identities = new List<long>(ledger.Keys);
            identities.Sort();

            for (var i = 0; i < identities.Count; i++)
            {
                var identityId = identities[i];
                if (identityId == 0)
                    continue;

                HashSet<long> known;
                if (!ledger.TryGetValue(identityId, out known) || known == null || known.Count == 0)
                    continue;

                var stationIds = new List<long>(known);
                stationIds.RemoveAll(stationId => stationId == 0);
                stationIds.Sort();
                if (stationIds.Count == 0)
                    continue;

                string name;
                if (playerNameHints == null || !playerNameHints.TryGetValue(identityId, out name))
                    name = null;

                file.Players.Add(new EconomyKnownStationsPlayer
                {
                    IdentityId = identityId,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name,
                    StationIds = stationIds
                });
            }

            return file;
        }

        public static int MergeStorageModel(
            EconomyKnownStationsFile file,
            Dictionary<long, HashSet<long>> ledger,
            Dictionary<long, string> playerNameHints)
        {
            if (file == null || file.Players == null || ledger == null)
                return 0;

            var added = 0;
            for (var i = 0; i < file.Players.Count; i++)
            {
                var player = file.Players[i];
                if (player == null || player.IdentityId == 0 || player.StationIds == null)
                    continue;

                if (playerNameHints != null && !string.IsNullOrWhiteSpace(player.Name))
                    playerNameHints[player.IdentityId] = player.Name;

                HashSet<long> stations = null;
                for (var j = 0; j < player.StationIds.Count; j++)
                {
                    var stationId = player.StationIds[j];
                    if (stationId == 0)
                        continue;

                    if (stations == null && !ledger.TryGetValue(player.IdentityId, out stations))
                    {
                        stations = new HashSet<long>();
                        ledger[player.IdentityId] = stations;
                    }

                    if (stations.Add(stationId))
                        added++;
                }
            }

            return added;
        }
    }
}
