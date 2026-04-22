using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Graph.Networking
{
    public class NetworkManager : IDisposable
    {
        ushort _channelId;
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
            MyAPIGateway.Multiplayer.SendMessageToServer(_channelId, MyAPIGateway.Utilities.SerializeToBinary(packet));
        }

        public void TransmitToPlayer(NetworkPackage data, ulong playerId, bool sendToSender = false)
        {
            PacketBase packet = new PacketBase(data.Id, false, sendToSender);
            packet.Wrap(data);
            MyAPIGateway.Multiplayer.SendMessageTo(_channelId, MyAPIGateway.Utilities.SerializeToBinary(packet),
                playerId);
        }

        void ReceivedPacket(ushort handler, byte[] raw, ulong id, bool isFromServer)
        {
            try
            {
                PacketBase packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(raw);
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

        void TransmitPacketToAllPlayers(ulong sender, PacketBase packet)
        {
            var tempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            MyAPIGateway.Players.GetPlayers(tempPlayers);

            foreach (var p in tempPlayers)
            {
                if (p.IsBot || p.SteamUserId == MyAPIGateway.Multiplayer.ServerId ||
                    (!packet.SendToSender && p.SteamUserId == sender))
                    continue;

                MyAPIGateway.Multiplayer.SendMessageTo(_channelId, MyAPIGateway.Utilities.SerializeToBinary(packet),
                    p.SteamUserId);
            }
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