using System;
using Acrux.Render3D;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using LcdMod.Common.Snapshots;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Apps.IntegrityMonitor
{
    // One grid snapshot a screen keeps under a key: read back from its block's storage, or taken of a grid and,
    // when persisted, stored there through the server (saved with the world and seen by every player). Decoding,
    // capturing and encoding run on background workers; the game thread only steps them.
    internal sealed class GridSnapshotSlot
    {
        sealed class Encoding
        {
            public GridSnapshot Snapshot;
            public byte[] Bytes;
            public volatile bool Done;
        }

        readonly string _key;
        byte[] _stored;
        bool _loaded;
        GridSnapshotCapture _decode;
        GridSnapshotCapture _capture;
        bool _persistCapture;
        Encoding _encoding;

        public GridSnapshotSlot(string key)
        {
            _key = key;
        }

        public GridSnapshot Snapshot { get; private set; }

        // Changes whenever Snapshot does.
        public int Version { get; private set; }

        public bool IsBusy => _capture != null || _encoding != null || _decode != null;

        // Why the last capture or store failed, or null.
        public string Error { get; private set; }

        // Game thread, every update.
        public void Update(IMyCubeBlock block)
        {
            if (block == null)
                return;
            byte[] stored = GridSnapshotStorage.Get(block, _key);
            // Another player's snapshot (or the world's) replaces this one, unless one of ours is on its way.
            if ((!_loaded || !ReferenceEquals(stored, _stored)) && _capture == null && _encoding == null)
            {
                _loaded = true;
                _stored = stored;
                _decode = stored == null ? null : new GridSnapshotCapture(stored);
                if (stored == null)
                    SetSnapshot(null);
            }
            if (_decode != null && _decode.Step())
            {
                if (_decode.Result != null)
                    SetSnapshot(_decode.Result);
                _decode = null;
            }
            if (_capture != null && _capture.Step())
            {
                GridSnapshot result = _capture.Result;
                Error = _capture.Error;
                _capture = null;
                if (result != null)
                {
                    SetSnapshot(result);
                    if (_persistCapture)
                        StartEncoding(result);
                }
            }
            if (_encoding != null && _encoding.Done)
            {
                byte[] bytes = _encoding.Bytes;
                _encoding = null;
                if (bytes == null)
                    Error = "Snapshot could not be encoded";
                else if (bytes.Length > Constants.MAX_GRID_SNAPSHOT_BYTES)
                    Error = "Snapshot too large to keep";
                else
                    Store(block, bytes);
            }
        }

        // Starts a snapshot of the grid, kept relative to the pivot block (zero picks one). A persisted one is stored on
        // the screen's block once taken.
        public void Capture(IMyCubeGrid grid, long pivotBlockId, bool persist)
        {
            if (grid == null || _capture != null)
                return;
            Error = null;
            _decode = null;
            _capture = new GridSnapshotCapture(grid, pivotBlockId);
            _persistCapture = persist;
        }

        // Keeps a snapshot taken elsewhere, persisted or only in memory.
        public void Set(IMyCubeBlock block, GridSnapshot snapshot, bool persist)
        {
            _capture = null;
            _decode = null;
            SetSnapshot(snapshot);
            if (snapshot == null)
                Store(block, null);
            else if (persist)
                StartEncoding(snapshot);
        }

        void StartEncoding(GridSnapshot snapshot)
        {
            var encoding = new Encoding { Snapshot = snapshot };
            _encoding = encoding;
            Action work = () =>
            {
                try
                {
                    encoding.Bytes = encoding.Snapshot.ToBytes();
                }
                catch (Exception)
                {
                    // Caught on the worker: the engine may treat uncaught background exceptions as fatal.
                    encoding.Bytes = null;
                }
                encoding.Done = true;
            };
            if (MyAPIGateway.Parallel == null)
            {
                work();
                return;
            }
            try
            {
                MyAPIGateway.Parallel.StartBackground(work);
            }
            catch (Exception)
            {
                work();
            }
        }

        void Store(IMyCubeBlock block, byte[] bytes)
        {
            if (block == null)
                return;
            GridSnapshotStorage.Set(block, _key, bytes);
            _stored = GridSnapshotStorage.Get(block, _key);
            var packet = new PacketStoreGridSnapshot { BlockId = block.EntityId, Key = _key, Data = bytes ?? Array.Empty<byte>() };
            if (MyAPIGateway.Session.IsServer)
            {
                if (LcdModSessionComponent.Server != null)
                    LcdModSessionComponent.Server.HandleLocalStoreGridSnapshot(packet);
            }
            else
            {
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);
            }
        }

        void SetSnapshot(GridSnapshot snapshot)
        {
            if (snapshot == null && Snapshot == null)
                return;
            Snapshot = snapshot;
            Version++;
        }
    }
}
