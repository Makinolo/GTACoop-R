﻿using System;
using System.IO;
using System.Collections.Generic;

using Lidgren.Network;
using ProtoBuf;

using GTA.Math;

namespace CoopClient
{
    #region CLIENT-ONLY
    public static class VectorExtensions
    {
        public static LVector3 ToLVector(this Vector3 vec)
        {
            return new LVector3()
            {
                X = vec.X,
                Y = vec.Y,
                Z = vec.Z,
            };
        }
    }
    #endregion

    [ProtoContract]
    public struct LVector3
    {
        #region CLIENT-ONLY
        public Vector3 ToVector()
        {
            return new Vector3(X, Y, Z);
        }
        #endregion

        public LVector3(float X, float Y, float Z)
        {
            this.X = X;
            this.Y = Y;
            this.Z = Z;
        }

        [ProtoMember(1)]
        public float X { get; set; }

        [ProtoMember(2)]
        public float Y { get; set; }

        [ProtoMember(3)]
        public float Z { get; set; }
    }

    public enum ModVersion
    {
        V0_1_0
    }

    public enum PacketTypes
    {
        HandshakePacket,
        PlayerConnectPacket,
        PlayerDisconnectPacket,
        FullSyncPlayerPacket,
        FullSyncNpcPacket,
        LightSyncPlayerPacket,
        ChatMessagePacket
    }

    [Flags]
    public enum PedDataFlags
    {
        LastSyncWasFull = 1 << 0,
        IsAiming = 1 << 1,
        IsShooting = 1 << 2,
        IsReloading = 1 << 3,
        IsJumping = 1 << 4,
        IsRagdoll = 1 << 5,
        IsOnFire = 1 << 6
    }

    public interface IPacket
    {
        void PacketToNetOutGoingMessage(NetOutgoingMessage message);
        void NetIncomingMessageToPacket(NetIncomingMessage message);
    }

    public abstract class Packet : IPacket
    {
        public abstract void PacketToNetOutGoingMessage(NetOutgoingMessage message);
        public abstract void NetIncomingMessageToPacket(NetIncomingMessage message);
    }

    [ProtoContract]
    public class HandshakePacket : Packet
    {
        [ProtoMember(1)]
        public string ID { get; set; }

        [ProtoMember(2)]
        public string SocialClubName { get; set; }

        [ProtoMember(3)]
        public string Username { get; set; }

        [ProtoMember(4)]
        public string ModVersion { get; set; }

        [ProtoMember(5)]
        public bool NpcsAllowed { get; set; }

        public override void PacketToNetOutGoingMessage(NetOutgoingMessage message)
        {
            message.Write((byte)PacketTypes.HandshakePacket);

            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, this);
                result = stream.ToArray();
            }

            message.Write(result.Length);
            message.Write(result);
        }

        public override void NetIncomingMessageToPacket(NetIncomingMessage message)
        {
            int len = message.ReadInt32();

            HandshakePacket data;
            using (MemoryStream stream = new MemoryStream(message.ReadBytes(len)))
            {
                data = Serializer.Deserialize<HandshakePacket>(stream);
            }

            ID = data.ID;
            SocialClubName = data.SocialClubName;
            Username = data.Username;
            ModVersion = data.ModVersion;
            NpcsAllowed = data.NpcsAllowed;
        }
    }

    [ProtoContract]
    public class PlayerConnectPacket : Packet
    {
        [ProtoMember(1)]
        public string Player { get; set; }

        [ProtoMember(2)]
        public string SocialClubName { get; set; }

        [ProtoMember(3)]
        public string Username { get; set; }

        public override void PacketToNetOutGoingMessage(NetOutgoingMessage message)
        {
            message.Write((byte)PacketTypes.PlayerConnectPacket);

            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, this);
                result = stream.ToArray();
            }

            message.Write(result.Length);
            message.Write(result);
        }

        public override void NetIncomingMessageToPacket(NetIncomingMessage message)
        {
            int len = message.ReadInt32();

            PlayerConnectPacket data;
            using (MemoryStream stream = new MemoryStream(message.ReadBytes(len)))
            {
                data = Serializer.Deserialize<PlayerConnectPacket>(stream);
            }

            Player = data.Player;
            SocialClubName = data.SocialClubName;
            Username = data.Username;
        }
    }

    [ProtoContract]
    public class PlayerDisconnectPacket : Packet
    {
        [ProtoMember(1)]
        public string Player { get; set; }

        public override void PacketToNetOutGoingMessage(NetOutgoingMessage message)
        {
            message.Write((byte)PacketTypes.PlayerDisconnectPacket);

            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, this);
                result = stream.ToArray();
            }

            message.Write(result.Length);
            message.Write(result);
        }

        public override void NetIncomingMessageToPacket(NetIncomingMessage message)
        {
            int len = message.ReadInt32();

            PlayerDisconnectPacket data;
            using (MemoryStream stream = new MemoryStream(message.ReadBytes(len)))
            {
                data = Serializer.Deserialize<PlayerDisconnectPacket>(stream);
            }

            Player = data.Player;
        }
    }

    [ProtoContract]
    public class FullSyncPlayerPacket : Packet
    {
        [ProtoMember(1)]
        public string Player { get; set; }

        [ProtoMember(2)]
        public int ModelHash { get; set; }

        [ProtoMember(3)]
        public Dictionary<int, int> Props { get; set; }

        [ProtoMember(4)]
        public int Health { get; set; }

        [ProtoMember(5)]
        public LVector3 Position { get; set; }

        [ProtoMember(6)]
        public LVector3 Rotation { get; set; }

        [ProtoMember(7)]
        public LVector3 Velocity { get; set; }

        [ProtoMember(8)]
        public byte Speed { get; set; }

        [ProtoMember(9)]
        public LVector3 AimCoords { get; set; }

        [ProtoMember(10)]
        public int CurrentWeaponHash { get; set; }

        [ProtoMember(11)]
        public byte? Flag { get; set; } = 0;

        public override void PacketToNetOutGoingMessage(NetOutgoingMessage message)
        {
            message.Write((byte)PacketTypes.FullSyncPlayerPacket);

            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, this);
                result = stream.ToArray();
            }

            message.Write(result.Length);
            message.Write(result);
        }

        public override void NetIncomingMessageToPacket(NetIncomingMessage message)
        {
            int len = message.ReadInt32();

            FullSyncPlayerPacket data;
            using (MemoryStream stream = new MemoryStream(message.ReadBytes(len)))
            {
                data = Serializer.Deserialize<FullSyncPlayerPacket>(stream);
            }

            Player = data.Player;
            ModelHash = data.ModelHash;
            Props = data.Props;
            Health = data.Health;
            Position = data.Position;
            Rotation = data.Rotation;
            Velocity = data.Velocity;
            Speed = data.Speed;
            AimCoords = data.AimCoords;
            CurrentWeaponHash = data.CurrentWeaponHash;
            Flag = data.Flag;
        }
    }

    [ProtoContract]
    public class FullSyncNpcPacket : Packet
    {
        [ProtoMember(1)]
        public string ID { get; set; }

        [ProtoMember(2)]
        public int ModelHash { get; set; }

        [ProtoMember(3)]
        public Dictionary<int, int> Props { get; set; }

        [ProtoMember(4)]
        public int Health { get; set; }

        [ProtoMember(5)]
        public LVector3 Position { get; set; }

        [ProtoMember(6)]
        public LVector3 Rotation { get; set; }

        [ProtoMember(7)]
        public LVector3 Velocity { get; set; }

        [ProtoMember(8)]
        public byte Speed { get; set; }

        [ProtoMember(9)]
        public LVector3 AimCoords { get; set; }

        [ProtoMember(10)]
        public int CurrentWeaponHash { get; set; }

        [ProtoMember(11)]
        public byte? Flag { get; set; } = 0;

        public override void PacketToNetOutGoingMessage(NetOutgoingMessage message)
        {
            message.Write((byte)PacketTypes.FullSyncNpcPacket);

            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, this);
                result = stream.ToArray();
            }

            message.Write(result.Length);
            message.Write(result);
        }

        public override void NetIncomingMessageToPacket(NetIncomingMessage message)
        {
            int len = message.ReadInt32();

            FullSyncNpcPacket data;
            using (MemoryStream stream = new MemoryStream(message.ReadBytes(len)))
            {
                data = Serializer.Deserialize<FullSyncNpcPacket>(stream);
            }

            ID = data.ID;
            ModelHash = data.ModelHash;
            Props = data.Props;
            Health = data.Health;
            Position = data.Position;
            Rotation = data.Rotation;
            Velocity = data.Velocity;
            Speed = data.Speed;
            AimCoords = data.AimCoords;
            CurrentWeaponHash = data.CurrentWeaponHash;
            Flag = data.Flag;
        }
    }

    [ProtoContract]
    public class LightSyncPlayerPacket : Packet
    {
        [ProtoMember(1)]
        public string Player { get; set; }

        [ProtoMember(2)]
        public int Health { get; set; }

        [ProtoMember(3)]
        public LVector3 Position { get; set; }

        [ProtoMember(4)]
        public LVector3 Rotation { get; set; }

        [ProtoMember(5)]
        public LVector3 Velocity { get; set; }

        [ProtoMember(6)]
        public byte Speed { get; set; }

        [ProtoMember(7)]
        public LVector3 AimCoords { get; set; }

        [ProtoMember(8)]
        public int CurrentWeaponHash { get; set; }

        [ProtoMember(9)]
        public byte? Flag { get; set; } = 0;

        public override void PacketToNetOutGoingMessage(NetOutgoingMessage message)
        {
            message.Write((byte)PacketTypes.LightSyncPlayerPacket);

            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, this);
                result = stream.ToArray();
            }

            message.Write(result.Length);
            message.Write(result);
        }

        public override void NetIncomingMessageToPacket(NetIncomingMessage message)
        {
            int len = message.ReadInt32();

            LightSyncPlayerPacket data;
            using (MemoryStream stream = new MemoryStream(message.ReadBytes(len)))
            {
                data = Serializer.Deserialize<LightSyncPlayerPacket>(stream);
            }

            Player = data.Player;
            Health = data.Health;
            Position = data.Position;
            Rotation = data.Rotation;
            Velocity = data.Velocity;
            Speed = data.Speed;
            AimCoords = data.AimCoords;
            CurrentWeaponHash = data.CurrentWeaponHash;
            Flag = data.Flag;
        }
    }

    [ProtoContract]
    public class ChatMessagePacket : Packet
    {
        [ProtoMember(1)]
        public string Username { get; set; }

        [ProtoMember(2)]
        public string Message { get; set; }

        public override void PacketToNetOutGoingMessage(NetOutgoingMessage message)
        {
            message.Write((byte)PacketTypes.ChatMessagePacket);

            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                Serializer.Serialize(stream, this);
                result = stream.ToArray();
            }

            message.Write(result.Length);
            message.Write(result);
        }

        public override void NetIncomingMessageToPacket(NetIncomingMessage message)
        {
            int len = message.ReadInt32();

            ChatMessagePacket data;
            using (MemoryStream stream = new MemoryStream(message.ReadBytes(len)))
            {
                data = Serializer.Deserialize<ChatMessagePacket>(stream);
            }

            Username = data.Username;
            Message = data.Message;
        }
    }
}
