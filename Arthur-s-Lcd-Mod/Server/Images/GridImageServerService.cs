using System;
using System.Collections.Generic;
using Adk.Video.Apng;
using LcdMod.Common.Helpers;
using LcdMod.Common.Images;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace LcdMod.Server.Images
{
    /// <summary>
    /// Server authority for grid-embedded images: verifies uploads, answers client requests,
    /// keeps every grid holding the images its screens reference, and drops unreferenced ones on save.
    /// </summary>
    internal sealed class GridImageServerService
    {
        readonly Func<IEnumerable<IMyCubeGrid>> _grids;
        readonly HashSet<string> _verifying = new HashSet<string>(StringComparer.Ordinal);

        public GridImageServerService(Func<IEnumerable<IMyCubeGrid>> grids)
        {
            _grids = grids;
        }

        public void Unload()
        {
            _verifying.Clear();
            GridImageStorage.Clear();
        }

        public void HandleUpload(ulong senderSteamId, PacketUploadGridImage packet)
        {
            if (packet == null || packet.Payload == null || packet.Payload.Data == null ||
                !GridImageCodec.IsValidId(packet.ImageId) ||
                packet.Payload.Data.Length > Constants.MAX_IMAGE_PAYLOAD_BYTES)
            {
                return;
            }

            var block = MyEntities.GetEntityById(packet.BlockId) as IMyTerminalBlock;
            if (block == null || block.CubeGrid == null)
                return;

            long identity = MyAPIGateway.Players.TryGetIdentityId(senderSteamId);
            if (identity == 0 || block.GetUserRelationToOwner(identity) > MyRelationsBetweenPlayerAndBlock.FactionShare)
                return;

            IMyCubeGrid grid = block.CubeGrid;
            if (GridImageStorage.FindEntry(grid, packet.ImageId) != null)
                return;

            if (GridImageStorage.GetTotalPayloadBytes(grid) + packet.Payload.Data.Length > Constants.MAX_GRID_IMAGE_PAYLOAD_BYTES)
            {
                LogHelper.Log(MyLogSeverity.Warning,
                    "Rejected image " + packet.ImageId + " from " + senderSteamId + ": grid " + grid.EntityId +
                    " is at its image storage limit");
                return;
            }

            string key = grid.EntityId + ":" + packet.ImageId;
            if (!_verifying.Add(key))
                return;

            var work = new VerifyWork { Id = packet.ImageId, Payload = packet.Payload };
            MyAPIGateway.Parallel.Start(
                () => Verify(work),
                () => CompleteUpload(key, grid, senderSteamId, work));
        }

        static void Verify(VerifyWork work)
        {
            if (GridImageCodec.IsAnimation(work.Payload))
            {
                ApngVideo video;
                if (!GridImageCodec.TryDecodeAnimation(work.Id, work.Payload, out video, out work.Error))
                    return;

                work.Width = video.Width;
                work.Height = video.Height;
                work.Valid = true;
                return;
            }

            byte[] dds;
            if (!GridImageCodec.TryDecode(work.Id, work.Payload, out dds, out work.Error))
                return;

            GridImageCodec.TryReadDdsSize(dds, out work.Width, out work.Height);
            work.Valid = true;
        }

        void CompleteUpload(string key, IMyCubeGrid grid, ulong owner, VerifyWork work)
        {
            _verifying.Remove(key);
            if (!work.Valid)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Rejected image " + work.Id + " from " + owner + ": " + work.Error);
                return;
            }

            if (grid.MarkedForClose || grid.Closed ||
                GridImageStorage.GetTotalPayloadBytes(grid) + work.Payload.Data.Length > Constants.MAX_GRID_IMAGE_PAYLOAD_BYTES)
            {
                return;
            }

            GridImageStorage.Add(grid, new GridImageManifestEntry
            {
                Id = work.Id,
                OwnerSteamId = owner,
                Width = work.Width,
                Height = work.Height,
                PayloadBytes = work.Payload.Data.Length
            }, work.Payload);
        }

        public void HandleRequest(ulong requester, PacketRequestGridImage packet, Action<PacketGridImageData> reply)
        {
            if (packet == null || !GridImageCodec.IsValidId(packet.ImageId))
                return;

            GridImageManifestEntry entry;
            GridImagePayload payload;
            var grid = MyEntities.GetEntityById(packet.GridId) as IMyCubeGrid;
            if (!GridImageStorage.TryGetImage(grid, packet.ImageId, out entry, out payload) &&
                !TryFindImage(packet.ImageId, null, out entry, out payload))
            {
                return;
            }

            reply(new PacketGridImageData(packet.GridId, entry.Clone(), payload));
        }

        // Copies images referenced by ids into grid from any other grid holding them.
        public void EnsureImages(IMyCubeGrid grid, HashSet<string> ids)
        {
            if (grid == null || ids == null)
                return;

            foreach (string id in ids)
            {
                if (GridImageStorage.FindEntry(grid, id) != null)
                    continue;

                GridImageManifestEntry entry;
                GridImagePayload payload;
                if (TryFindImage(id, grid, out entry, out payload))
                    GridImageStorage.Add(grid, entry, payload);
            }
        }

        public void EnsureReferencedImages(IMyCubeGrid grid)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            GridImageStorage.CollectReferencedIds(grid, ids);
            EnsureImages(grid, ids);
        }

        public void EnsureConfigImages(IMyCubeGrid grid, object config)
        {
            if (grid == null || config == null)
                return;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            GridImageStorage.CollectReferencedIds(MyAPIGateway.Utilities.SerializeToBinary(config), ids);
            EnsureImages(grid, ids);
        }

        public void HandleGridSplit(IMyCubeGrid original, IMyCubeGrid split)
        {
            EnsureReferencedImages(split);
        }

        public void HandleGridMerge(IMyCubeGrid kept, IMyCubeGrid removed)
        {
            EnsureReferencedImages(kept);
        }

        public void Prune()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (IMyCubeGrid grid in _grids())
            {
                if (grid == null || grid.MarkedForClose)
                    continue;

                ids.Clear();
                GridImageStorage.CollectReferencedIds(grid, ids);
                GridImageStorage.RemoveExcept(grid, ids);
            }
        }

        bool TryFindImage(string id, IMyCubeGrid exclude, out GridImageManifestEntry entry, out GridImagePayload payload)
        {
            foreach (IMyCubeGrid grid in _grids())
            {
                if (grid == null || grid == exclude || grid.MarkedForClose)
                    continue;

                if (GridImageStorage.TryGetImage(grid, id, out entry, out payload))
                    return true;
            }

            entry = null;
            payload = null;
            return false;
        }

        sealed class VerifyWork
        {
            public string Id;
            public GridImagePayload Payload;
            public bool Valid;
            public int Width;
            public int Height;
            public string Error;
        }
    }
}
