using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Sandbox.ModAPI;

namespace LcdMod.Server
{
    [Serializable]
    [XmlRoot("DroplineCarts")]
    public sealed class DroplineCartFile
    {
        [XmlAttribute("version")]
        public int Version = 1;

        [XmlArray("Players")]
        [XmlArrayItem("Player")]
        public List<DroplineCartPlayer> Players = new List<DroplineCartPlayer>();
    }

    [Serializable]
    public sealed class DroplineCartPlayer
    {
        [XmlAttribute("identityId")]
        public long IdentityId;

        [XmlArray("Lines")]
        [XmlArrayItem("Line")]
        public List<DroplineCartEntry> Lines = new List<DroplineCartEntry>();
    }

    [Serializable]
    public sealed class DroplineCartEntry
    {
        [XmlAttribute("station")]
        public long StationId;

        [XmlAttribute("type")]
        public string TypeId;

        [XmlAttribute("subtype")]
        public string SubtypeId;

        [XmlAttribute("quantity")]
        public int Quantity;

        internal bool Matches(long stationId, string typeId, string subtypeId) =>
            StationId == stationId && TypeId == typeId && SubtypeId == subtypeId;
    }

    // Each player's multi-store delivery cart. Dropline delivers one store per order, so a store's lines (a branch)
    // are bought together and the other branches stay.
    internal sealed class DroplineCartStore
    {
        const string FileName = "DroplineCarts.xml";
        internal const int MaximumLines = 64;
        internal const int MaximumQuantity = 1000;

        DroplineCartFile _file;
        bool _dirty;

        public List<DroplineCartEntry> Lines(long identity)
        {
            Load();
            foreach (var player in _file.Players)
                if (player.IdentityId == identity)
                    return player.Lines;
            var created = new DroplineCartPlayer { IdentityId = identity };
            _file.Players.Add(created);
            return created.Lines;
        }

        public string Add(long identity, long stationId, string typeId, string subtypeId, int quantity)
        {
            var lines = Lines(identity);
            var line = lines.Find(entry => entry.Matches(stationId, typeId, subtypeId));
            if (line == null)
            {
                if (lines.Count >= MaximumLines)
                    return "The cart is full.";
                line = new DroplineCartEntry { StationId = stationId, TypeId = typeId, SubtypeId = subtypeId };
                lines.Add(line);
            }
            line.Quantity = (int)Math.Min(MaximumQuantity, (long)line.Quantity + quantity);
            _dirty = true;
            return null;
        }

        public void Set(long identity, long stationId, string typeId, string subtypeId, int quantity)
        {
            var lines = Lines(identity);
            var line = lines.Find(entry => entry.Matches(stationId, typeId, subtypeId));
            if (line == null)
                return;
            if (quantity <= 0)
                lines.Remove(line);
            else
                line.Quantity = Math.Min(MaximumQuantity, quantity);
            _dirty = true;
        }

        // stationId 0 empties the whole cart.
        public void Clear(long identity, long stationId)
        {
            Lines(identity).RemoveAll(entry => stationId == 0 || entry.StationId == stationId);
            _dirty = true;
        }

        // Takes out what an order bought; units added to the line after the quote stay in the cart.
        public void Remove(long identity, List<DroplineCartEntry> bought)
        {
            var lines = Lines(identity);
            foreach (var item in bought)
            {
                var line = lines.Find(entry => entry.Matches(item.StationId, item.TypeId, item.SubtypeId));
                if (line == null)
                    continue;
                line.Quantity -= item.Quantity;
                if (line.Quantity <= 0)
                    lines.Remove(line);
            }
            _dirty = true;
        }

        public void Save()
        {
            if (!_dirty || _file == null)
                return;
            _file.Players.RemoveAll(player => player.Lines.Count == 0);
            using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(DroplineCartStore)))
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(_file));
            _dirty = false;
        }

        public void Unload()
        {
            _file = null;
            _dirty = false;
        }

        void Load()
        {
            if (_file != null)
                return;
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage(FileName, typeof(DroplineCartStore)))
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, typeof(DroplineCartStore)))
                    _file = MyAPIGateway.Utilities.SerializeFromXML<DroplineCartFile>(reader.ReadToEnd());
            if (_file == null || _file.Version != 1 || _file.Players == null)
                _file = new DroplineCartFile();
        }
    }
}
