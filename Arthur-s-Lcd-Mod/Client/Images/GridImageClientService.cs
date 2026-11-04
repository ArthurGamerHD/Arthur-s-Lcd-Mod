using System;
using System.Collections.Generic;
using System.IO;
using Adk.Video.Apng;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using LcdMod.Common.Images;
using LcdMod.Common.Networking;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Images
{
    internal enum GridImageState
    {
        Ready,
        Loading,
        Blocked,
        Missing,
        Failed
    }

    /// <summary>
    /// Resolves image sprites (<c>LcdImg_&lt;id&gt;</c>) for this session: finds the payload in the local
    /// library, the grid's embedded store, or the server, rebuilds each image once into world storage on a
    /// worker thread, and registers it as an LCD texture. State queries are safe from any thread.
    /// </summary>
    internal sealed class GridImageClientService
    {
        const int MAX_CONCURRENT_BUILDS = 2;
        const int REQUEST_INTERVAL_FRAMES = 600;
        const int MISSING_AFTER_FRAMES = 300;
        const int RELEASE_UNUSED_ANIMATION_FRAMES = 600;

        public static GridImageClientService Instance { get; private set; }

        sealed class ImageRecord
        {
            public string Id;
            public GridImageState State = GridImageState.Loading;
            public GridImageManifestEntry Entry;
            public GridImagePayload Payload;
            public bool Local;
            public bool Building;
            public GridImageAnimation Animation;
            public long FirstSeenFrame = -1;
            public long NextRequestFrame;
        }

        readonly object _lock = new object();
        readonly Dictionary<string, ImageRecord> _records = new Dictionary<string, ImageRecord>(StringComparer.Ordinal);
        readonly Dictionary<string, long> _pendingGrids = new Dictionary<string, long>(StringComparer.Ordinal);
        readonly Queue<ImageRecord> _buildQueue = new Queue<ImageRecord>();
        readonly HashSet<string> _uploaded = new HashSet<string>(StringComparer.Ordinal);
        int _activeBuilds;
        static volatile bool _sessionEnded;

        public void Load()
        {
            _sessionEnded = false;
            Instance = this;
        }

        public void Unload()
        {
            _sessionEnded = true;
            DeleteWorldImages();

            lock (_lock)
            {
                foreach (ImageRecord record in _records.Values)
                {
                    if (record.Animation != null)
                        record.Animation.Dispose();
                }

                _records.Clear();
                _pendingGrids.Clear();
                _buildQueue.Clear();
            }

            _uploaded.Clear();
            GridImageStorage.Clear();
            WorldStorageLocator.Reset();
            if (Instance == this)
                Instance = null;
        }

        public static bool IsImageSprite(string spriteName)
        {
            string id;
            return GridImageCodec.TryParseSpriteName(spriteName, out id);
        }

        public GridImageState GetState(string spriteName, long gridId)
        {
            string id;
            if (!GridImageCodec.TryParseSpriteName(spriteName, out id))
                return GridImageState.Missing;

            lock (_lock)
            {
                ImageRecord record = GetOrCreate(id);
                if (record.State == GridImageState.Ready || record.State == GridImageState.Failed)
                    return record.State;

                if (record.Entry != null && IsBlocked(record.Entry))
                    return GridImageState.Blocked;

                if (record.Payload == null && gridId != 0)
                    _pendingGrids[id] = gridId;

                return record.State;
            }
        }

        // The player of an animated image once it is ready, or null for still images. Game thread only.
        public GridImageAnimation GetAnimation(string spriteName)
        {
            string id;
            if (!GridImageCodec.TryParseSpriteName(spriteName, out id))
                return null;

            lock (_lock)
            {
                ImageRecord record;
                return _records.TryGetValue(id, out record) ? record.Animation : null;
            }
        }

        public bool TryGetSize(string spriteName, out Vector2I size)
        {
            size = Vector2I.Zero;
            string id;
            if (!GridImageCodec.TryParseSpriteName(spriteName, out id))
                return false;

            lock (_lock)
            {
                ImageRecord record;
                if (!_records.TryGetValue(id, out record) || record.Entry == null ||
                    record.Entry.Width <= 0 || record.Entry.Height <= 0)
                {
                    return false;
                }

                size = new Vector2I(record.Entry.Width, record.Entry.Height);
                return true;
            }
        }

        public void RegisterLocal(EncodedGridImage image)
        {
            if (image == null || !GridImageCodec.IsValidId(image.Id))
                return;

            lock (_lock)
            {
                ImageRecord record = GetOrCreate(image.Id);
                record.Local = true;
                if (record.Payload != null)
                    return;

                SetPayload(record, null, image.Payload);
                record.Entry = new GridImageManifestEntry
                {
                    Id = image.Id,
                    OwnerSteamId = LocalSteamId(),
                    Width = image.Width,
                    Height = image.Height,
                    PayloadBytes = image.Payload.Data.Length
                };
            }
        }

        public void HandleImageData(PacketGridImageData packet)
        {
            if (packet == null || packet.Entry == null || packet.Payload == null ||
                !GridImageCodec.IsValidId(packet.Entry.Id))
            {
                return;
            }

            lock (_lock)
            {
                ImageRecord record = GetOrCreate(packet.Entry.Id);
                if (record.Payload == null)
                    SetPayload(record, packet.Entry, packet.Payload);
            }

            if (!MyAPIGateway.Session.IsServer)
                KeepInLocalGridCopy(MyEntities.GetEntityById(packet.GridId) as IMyCubeGrid, packet.Entry, packet.Payload);
        }

        // Mod storage is not replicated after a grid streams in, and blueprints are built from this client's copy of
        // the grid, so images seen or uploaded here are also written into the local copy.
        static void KeepInLocalGridCopy(IMyCubeGrid grid, GridImageManifestEntry entry, GridImagePayload payload)
        {
            if (grid != null && !grid.MarkedForClose && GridImageStorage.FindEntry(grid, entry.Id) == null)
                GridImageStorage.Add(grid, entry, payload);
        }

        // Sends library images referenced by config to the server for the block's grid, once per grid.
        public void UploadReferenced(IMyTerminalBlock block, object config)
        {
            if (block == null || block.CubeGrid == null || config == null)
                return;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            GridImageStorage.CollectReferencedIds(MyAPIGateway.Utilities.SerializeToBinary(config), ids);
            foreach (string id in ids)
            {
                GridImagePayload payload;
                lock (_lock)
                {
                    ImageRecord record;
                    if (!_records.TryGetValue(id, out record) || !record.Local || record.Payload == null)
                        continue;

                    payload = record.Payload;
                }

                string key = block.CubeGrid.EntityId + ":" + id;
                if (GridImageStorage.FindEntry(block.CubeGrid, id) != null || !_uploaded.Add(key))
                    continue;

                var packet = new PacketUploadGridImage(block.EntityId, id, payload);
                if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                {
                    LcdModSessionComponent.Server.HandleLocalUploadGridImage(packet);
                    continue;
                }

                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);
                GridImageManifestEntry entry;
                lock (_lock)
                    entry = _records[id].Entry.Clone();
                KeepInLocalGridCopy(block.CubeGrid, entry, payload);
            }
        }

        public void Update()
        {
            long frame = MyAPIGateway.Session.GameplayFrameCounter;
            List<KeyValuePair<string, long>> pending = null;
            lock (_lock)
            {
                if (_pendingGrids.Count > 0)
                {
                    pending = new List<KeyValuePair<string, long>>(_pendingGrids);
                    _pendingGrids.Clear();
                }
            }

            if (pending != null)
            {
                for (int i = 0; i < pending.Count; i++)
                    Resolve(pending[i].Key, pending[i].Value, frame);
            }

            lock (_lock)
            {
                foreach (ImageRecord record in _records.Values)
                {
                    if (record.Payload == null || record.Building || record.State != GridImageState.Loading ||
                        IsBlocked(record.Entry))
                    {
                        continue;
                    }

                    record.Building = true;
                    _buildQueue.Enqueue(record);
                }

                while (_activeBuilds < MAX_CONCURRENT_BUILDS && _buildQueue.Count > 0)
                {
                    ImageRecord record = _buildQueue.Dequeue();
                    _activeBuilds++;
                    StartBuild(record);
                }

                foreach (ImageRecord record in _records.Values)
                {
                    if (record.Animation != null && frame - record.Animation.LastUsedFrame > RELEASE_UNUSED_ANIMATION_FRAMES)
                        record.Animation.Release();
                }
            }
        }

        void Resolve(string id, long gridId, long frame)
        {
            ImageRecord record;
            lock (_lock)
            {
                record = GetOrCreate(id);
                if (record.Payload != null)
                    return;

                if (record.FirstSeenFrame < 0)
                    record.FirstSeenFrame = frame;
            }

            var grid = MyEntities.GetEntityById(gridId) as IMyCubeGrid;
            GridImageManifestEntry entry;
            GridImagePayload payload;
            if (GridImageStorage.TryGetImage(grid, id, out entry, out payload))
            {
                lock (_lock)
                    SetPayload(record, entry.Clone(), payload);

                return;
            }

            lock (_lock)
            {
                if (record.State == GridImageState.Loading && frame - record.FirstSeenFrame > MISSING_AFTER_FRAMES)
                    record.State = GridImageState.Missing;

                if (frame < record.NextRequestFrame)
                    return;

                record.NextRequestFrame = frame + REQUEST_INTERVAL_FRAMES;
            }

            var request = new PacketRequestGridImage(gridId, id);
            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                LcdModSessionComponent.Server.HandleLocalRequestGridImage(request);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(request, false);
        }

        void StartBuild(ImageRecord record)
        {
            var work = new BuildWork { Id = record.Id, Payload = record.Payload };
            MyAPIGateway.Parallel.Start(() => Build(work), () => CompleteBuild(record, work));
        }

        static void Build(BuildWork work)
        {
            if (GridImageCodec.IsAnimation(work.Payload))
            {
                GridImageCodec.TryDecodeAnimation(work.Id, work.Payload, out work.Animation, out work.Error);
                return;
            }

            try
            {
                string storageRoot;
                if (!WorldStorageLocator.TryGetStorageRoot(out storageRoot))
                {
                    work.Error = "world storage could not be located";
                    return;
                }

                string fileName = GridImageCodec.ToFileName(work.Id);
                if (!WorldFileHasLength(fileName, work.Payload.DdsLength))
                {
                    byte[] dds;
                    if (!GridImageCodec.TryDecode(work.Id, work.Payload, out dds, out work.Error))
                        return;

                    using (BinaryWriter writer = MyAPIGateway.Utilities.WriteBinaryFileInWorldStorage(fileName, typeof(LcdModSessionComponent)))
                        writer.Write(dds);

                    if (_sessionEnded)
                    {
                        MyAPIGateway.Utilities.DeleteFileInWorldStorage(fileName, typeof(LcdModSessionComponent));
                        work.Error = "the session ended";
                        return;
                    }

                    if (!WorldFileHasLength(fileName, dds.Length))
                    {
                        WorldStorageLocator.Reset();
                        work.Error = "the rebuilt texture could not be written to world storage";
                        return;
                    }
                }

                work.Path = WorldStorageLocator.ToRendererPath(storageRoot, fileName);
            }
            catch (Exception e)
            {
                work.Error = e.Message;
            }
        }

        static bool WorldFileHasLength(string fileName, int length)
        {
            if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(fileName, typeof(LcdModSessionComponent)))
                return false;

            using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInWorldStorage(fileName, typeof(LcdModSessionComponent)))
                return reader != null && reader.BaseStream.Length == length;
        }

        void CompleteBuild(ImageRecord record, BuildWork work)
        {
            if (Instance != this)
                return;

            lock (_lock)
            {
                _activeBuilds--;
                record.Building = false;
                if (work.Animation != null)
                {
                    record.Animation = new GridImageAnimation(record.Id, work.Animation);
                    record.State = GridImageState.Ready;
                }
                else if (work.Path == null)
                {
                    record.State = GridImageState.Failed;
                    LogHelper.Log(MyLogSeverity.Warning, "Image " + record.Id + " could not be rebuilt: " + work.Error);
                    return;
                }
            }

            if (work.Animation != null)
            {
                RequestRedrawAll();
                return;
            }

            string spriteName = GridImageCodec.ToSpriteName(record.Id);
            MyDefinitionManager.Static.Definitions.AddOrReplaceDefinition(new MyLCDTextureDefinition
            {
                Id = new MyDefinitionId((MyObjectBuilderType)typeof(MyObjectBuilder_LCDTextureDefinition), spriteName),
                Public = false,
                LocalizationId = spriteName,
                SpritePath = work.Path,
                TexturePath = work.Path,
                Selectable = false
            });

            lock (_lock)
                record.State = GridImageState.Ready;

            RequestRedrawAll();
        }

        static void RequestRedrawAll()
        {
            foreach (SurfaceScriptBase surface in new List<SurfaceScriptBase>(SurfaceScriptBase.Instances))
                surface?.RequestRedraw();
        }

        // Rebuilt images are a per-session cache: on exit every LcdImg_*.dds in this world's storage goes, including
        // leftovers from a session that crashed. Only a path already resolved this session is used, so exiting never
        // runs the canary.
        static void DeleteWorldImages()
        {
            string storageRoot;
            if (!WorldStorageLocator.TryGetResolvedRoot(out storageRoot))
                return;

            try
            {
                string[] files = PathUtils.GetFilesRecursively(storageRoot, GridImageCodec.SPRITE_PREFIX + "*.dds");
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = Path.GetFileName(files[i]);
                    string id;
                    if (GridImageCodec.TryParseSpriteName(Path.GetFileNameWithoutExtension(fileName), out id) &&
                        string.Equals(Path.GetDirectoryName(files[i]).Replace('\\', '/').TrimEnd('/'), storageRoot,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        MyAPIGateway.Utilities.DeleteFileInWorldStorage(fileName, typeof(LcdModSessionComponent));
                    }
                }
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not clear world-storage images: " + e.Message);
            }
        }

        static void SetPayload(ImageRecord record, GridImageManifestEntry entry, GridImagePayload payload)
        {
            if (entry != null)
                record.Entry = entry;

            record.Payload = payload;
            if (record.State == GridImageState.Missing)
                record.State = GridImageState.Loading;
        }

        ImageRecord GetOrCreate(string id)
        {
            ImageRecord record;
            if (!_records.TryGetValue(id, out record))
            {
                record = new ImageRecord { Id = id };
                _records[id] = record;
            }

            return record;
        }

        static bool IsBlocked(GridImageManifestEntry entry)
        {
            if (entry == null || entry.OwnerSteamId == 0 || LocalConfigManager.RenderOtherUserTextures)
                return false;

            return entry.OwnerSteamId != LocalSteamId();
        }

        static ulong LocalSteamId()
        {
            return MyAPIGateway.Session.Player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId;
        }

        sealed class BuildWork
        {
            public string Id;
            public GridImagePayload Payload;
            public string Path;
            public ApngVideo Animation;
            public string Error;
        }
    }
}
