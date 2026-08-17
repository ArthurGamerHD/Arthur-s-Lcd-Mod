using System;
using System.Collections.Generic;

namespace LcdMod.Client.GridData
{
    /// <summary>Owns media-player partitions independently of grid topology and inventory state.</summary>
    public sealed class MediaPlayerRegistry
    {
        readonly Dictionary<PartitionKey, GridMediaPlayer> _players =
            new Dictionary<PartitionKey, GridMediaPlayer>();

        public GridMediaPlayer Get(long blockId, int screenIndex)
        {
            if (screenIndex < 0)
                screenIndex = 0;

            var key = new PartitionKey(blockId, screenIndex);
            GridMediaPlayer player;
            if (!_players.TryGetValue(key, out player))
            {
                player = new GridMediaPlayer();
                _players[key] = player;
            }

            return player;
        }

        public void Update()
        {
            foreach (var player in _players.Values)
                player.Update();
        }

        public void Unload()
        {
            foreach (var player in _players.Values)
                player.Unload();
            _players.Clear();
        }

        struct PartitionKey : IEquatable<PartitionKey>
        {
            readonly long _blockId;
            readonly int _screenIndex;

            public PartitionKey(long blockId, int screenIndex)
            {
                _blockId = blockId;
                _screenIndex = screenIndex;
            }

            public bool Equals(PartitionKey other)
            {
                return _blockId == other._blockId && _screenIndex == other._screenIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is PartitionKey && Equals((PartitionKey)obj);
            }

            public override int GetHashCode()
            {
                return (_blockId.GetHashCode() * 397) ^ _screenIndex;
            }
        }
    }
}
