using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace LcdMod.Common.Networking
{
    public class NetworkManager : IDisposable
    {
        const int MAX_WIRE_PACKET_BYTES = 96 * 1024;

        ushort _channelId;
        int _nextFragmentTransferId;
        readonly Dictionary<string, FragmentAssembly> _fragmentAssemblies = new Dictionary<string, FragmentAssembly>();
        public Action<ReceivedPacketEventArgs> OnReceivedPacket;

        public NetworkManager(ushort channelId)
        {
            _channelId = channelId;
        }

        public void Init()
        {
            Register();
        }

        public void Register()
        {
            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(_channelId, ReceivedPacket);
        }

        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(_channelId, ReceivedPacket);
        }

        public void Dispose()
        {
            OnReceivedPacket = null;
            Unregister();
        }

        public void TransmitToServer(NetworkPackage data, bool sendToAllPlayers = true, bool sendToSender = false)
        {
            PacketBase packet = new PacketBase(data.Id, sendToAllPlayers, sendToSender);
            packet.Wrap(data);
            var raw = MyAPIGateway.Utilities.SerializeToBinary(packet);
            if (!SendToServer(raw))
                MyLog.Default.WriteLineAndConsole($"[LcdMod] Failed to send packet {data.Code} to server ({raw.Length} bytes)");
        }

        public void TransmitToPlayer(NetworkPackage data, ulong playerId, bool sendToSender = false)
        {
            PacketBase packet = new PacketBase(data.Id, false, sendToSender);
            packet.Wrap(data);
            var raw = MyAPIGateway.Utilities.SerializeToBinary(packet);
            if (!SendToPlayer(raw, playerId))
                MyLog.Default.WriteLineAndConsole($"[LcdMod] Failed to send packet {data.Code} to player {playerId} ({raw.Length} bytes)");
        }

        void ReceivedPacket(ushort handler, byte[] raw, ulong id, bool isFromServer)
        {
            try
            {
                PacketBase packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(raw);
                if (packet.Id == (int)PackageCode.NetworkFragment)
                {
                    if (!TryAssembleFragment(packet, id, isFromServer, out raw))
                        return;

                    packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(raw);
                }

                HandlePacket(packet, id, isFromServer);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"Malformed packet from {id}!");
                MyLog.Default.WriteLineAndConsole($"{e.Message}\n{e.StackTrace}");

                if (MyAPIGateway.Session?.Player != null)
                    MyAPIGateway.Utilities.ShowNotification(
                        $"[ERROR: {GetType().FullName}: {e.Message} | Send SpaceEngineers.Log to mod author]", 10000,
                        MyFontEnum.Red);
            }
        }

        void HandlePacket(PacketBase packet, ulong id, bool isFromServer)
        {
                ReceivedPacketEventArgs receivedPacketEventArgs =
                    new ReceivedPacketEventArgs(packet.Id, packet.Data, id, isFromServer);

                if (receivedPacketEventArgs.IsResolved)
                    return;

                if (packet.SendToAllPlayers && MyAPIGateway.Session.IsServer)
                    TransmitPacketToAllPlayers(id, packet);

                if ((!isFromServer && MyAPIGateway.Session.IsServer) ||
                    (isFromServer && (!MyAPIGateway.Session.IsServer || packet.SendToSender)))
                    OnReceivedPacket?.Invoke(receivedPacketEventArgs);
        }

        void TransmitPacketToAllPlayers(ulong sender, PacketBase packet)
        {
            var tempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            MyAPIGateway.Players.GetPlayers(tempPlayers);

            foreach (var p in tempPlayers)
            {
                if (p.IsBot || p.SteamUserId == MyAPIGateway.Multiplayer.ServerId ||
                    (!packet.SendToSender && p.SteamUserId == sender))
                    continue;

                var raw = MyAPIGateway.Utilities.SerializeToBinary(packet);
                if (!SendToPlayer(raw, p.SteamUserId))
                    MyLog.Default.WriteLineAndConsole($"[LcdMod] Failed to forward packet {(PackageCode)packet.Id} to player {p.SteamUserId} ({raw.Length} bytes)");
            }
        }

        bool SendToServer(byte[] raw)
        {
            if (raw == null)
                return false;

            if (raw.Length <= MAX_WIRE_PACKET_BYTES)
                return MyAPIGateway.Multiplayer.SendMessageToServer(_channelId, raw);

            return SendFragments(raw, null);
        }

        bool SendToPlayer(byte[] raw, ulong playerId)
        {
            if (raw == null)
                return false;

            if (raw.Length <= MAX_WIRE_PACKET_BYTES)
                return MyAPIGateway.Multiplayer.SendMessageTo(_channelId, raw, playerId);

            return SendFragments(raw, playerId);
        }

        bool SendFragments(byte[] raw, ulong? playerId)
        {
            var transferId = ++_nextFragmentTransferId;
            var totalChunks = (raw.Length + MAX_WIRE_PACKET_BYTES - 1) / MAX_WIRE_PACKET_BYTES;
            MyLog.Default.WriteLineAndConsole(
                $"[LcdMod] Sending fragmented packet {transferId} ({raw.Length} bytes, {totalChunks} chunks)");
            for (var chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                var offset = chunkIndex * MAX_WIRE_PACKET_BYTES;
                var size = Math.Min(MAX_WIRE_PACKET_BYTES, raw.Length - offset);
                var chunkData = new byte[size];
                Array.Copy(raw, offset, chunkData, 0, size);

                var fragment = new PacketFragment(transferId, chunkIndex, totalChunks, raw.Length, chunkData);
                var packet = new PacketBase((int)PackageCode.NetworkFragment, false, false);
                packet.Wrap(fragment);
                var fragmentRaw = MyAPIGateway.Utilities.SerializeToBinary(packet);

                var sent = playerId.HasValue
                    ? MyAPIGateway.Multiplayer.SendMessageTo(_channelId, fragmentRaw, playerId.Value)
                    : MyAPIGateway.Multiplayer.SendMessageToServer(_channelId, fragmentRaw);

                if (!sent)
                    return false;
            }

            return true;
        }

        bool TryAssembleFragment(PacketBase packet, ulong senderId, bool isFromServer, out byte[] raw)
        {
            raw = null;
            var fragment = MyAPIGateway.Utilities.SerializeFromBinary<PacketFragment>(packet.Data);
            if (fragment == null ||
                fragment.Data == null ||
                fragment.TotalChunks <= 0 ||
                fragment.ChunkIndex < 0 ||
                fragment.ChunkIndex >= fragment.TotalChunks ||
                fragment.TotalLength <= 0)
            {
                return false;
            }

            var key = senderId + ":" + isFromServer + ":" + fragment.TransferId;
            FragmentAssembly assembly;
            if (!_fragmentAssemblies.TryGetValue(key, out assembly))
            {
                assembly = new FragmentAssembly(fragment.TotalChunks, fragment.TotalLength);
                _fragmentAssemblies[key] = assembly;
            }

            if (!assembly.Add(fragment))
                return false;

            raw = assembly.Assemble();
            _fragmentAssemblies.Remove(key);
            if (raw != null)
                MyLog.Default.WriteLineAndConsole(
                    $"[LcdMod] Reassembled fragmented packet {fragment.TransferId} from {senderId} ({raw.Length} bytes)");
            return raw != null;
        }

        [ProtoContract]
        class PacketBase
        {
            [ProtoMember(1)] public readonly int Id;
            [ProtoMember(2)] public readonly bool SendToAllPlayers;
            [ProtoMember(3)] public readonly bool SendToSender;

            [ProtoMember(4)] public byte[] Data;

            // ReSharper disable once UnusedMember.Local
            public PacketBase()
            {
            } // Needed for Protobuf

            public PacketBase(int id, bool sendToAllPlayers, bool sendToSender)
            {
                Id = id;
                SendToAllPlayers = sendToAllPlayers;
                SendToSender = sendToSender;
            }

            public void Wrap(object data)
            {
                Data = MyAPIGateway.Utilities.SerializeToBinary(data);
            }
        }

        [ProtoContract]
        class PacketFragment
        {
            [ProtoMember(1)] public int TransferId;
            [ProtoMember(2)] public int ChunkIndex;
            [ProtoMember(3)] public int TotalChunks;
            [ProtoMember(4)] public int TotalLength;
            [ProtoMember(5)] public byte[] Data;

            // ReSharper disable once UnusedMember.Local
            public PacketFragment()
            {
            }

            public PacketFragment(int transferId, int chunkIndex, int totalChunks, int totalLength, byte[] data)
            {
                TransferId = transferId;
                ChunkIndex = chunkIndex;
                TotalChunks = totalChunks;
                TotalLength = totalLength;
                Data = data;
            }
        }

        class FragmentAssembly
        {
            readonly byte[][] _chunks;
            readonly int _totalLength;
            int _receivedChunks;

            public FragmentAssembly(int totalChunks, int totalLength)
            {
                _chunks = new byte[totalChunks][];
                _totalLength = totalLength;
            }

            public bool Add(PacketFragment fragment)
            {
                if (fragment.ChunkIndex < 0 || fragment.ChunkIndex >= _chunks.Length)
                    return false;

                if (_chunks[fragment.ChunkIndex] != null)
                    return false;

                _chunks[fragment.ChunkIndex] = fragment.Data;
                _receivedChunks++;
                return _receivedChunks == _chunks.Length;
            }

            public byte[] Assemble()
            {
                var raw = new byte[_totalLength];
                var offset = 0;
                for (var i = 0; i < _chunks.Length; i++)
                {
                    var chunk = _chunks[i];
                    if (chunk == null || offset + chunk.Length > raw.Length)
                        return null;

                    Array.Copy(chunk, 0, raw, offset, chunk.Length);
                    offset += chunk.Length;
                }

                return offset == raw.Length ? raw : null;
            }
        }
    }

    public abstract class NetworkPackage
    {
        public abstract PackageCode Code { get; }
        public int Id => (int)Code;
    }

    public enum PackageCode
    {
        SyncConfig = 1,
        EditFaction = 2,
        PlayerInputBlacklist = 3,
        SortInventory = 4,
        TransferItems = 5,
        RequestTexture = 6,
        SyncTexture = 7,
        FillBlocks = 8,
        RequestNpcMarket = 9,
        SyncNpcMarket = 10,
        NetworkFragment = 100
    }

    public class ReceivedPacketEventArgs : EventArgs
    {
        public bool IsResolved { private set; get; }
        public int PacketId { protected set; get; }

        public PackageCode Code => (PackageCode)PacketId;
        public ulong SenderId { protected set; get; }
        public bool IsFromServer { protected set; get; }

        readonly byte[] _data;

        public ReceivedPacketEventArgs(int packetId, byte[] data, ulong senderId, bool isFromServer)
        {
            PacketId = packetId;
            SenderId = senderId;
            IsFromServer = isFromServer;
            _data = data;
        }

        public T UnWrap<T>()
        {
            return MyAPIGateway.Utilities.SerializeFromBinary<T>(_data);
        }

        public void SetResolved(bool value)
        {
            IsResolved = value;
        }
    }
}
