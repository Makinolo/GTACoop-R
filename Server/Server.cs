﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using System.IO;
using System.Net.Http;

using Newtonsoft.Json;

using Lidgren.Network;
using System.Text;

namespace CoopServer
{
    internal class IpInfo
    {
        public string Country { get; set; }
    }

    internal class Server
    {
        private static readonly string CompatibleVersion = "V0_8_0_1";

        public static readonly Settings MainSettings = Util.Read<Settings>("CoopSettings.xml");
        private readonly Blocklist MainBlocklist = Util.Read<Blocklist>("Blocklist.xml");
        private readonly Allowlist MainAllowlist = Util.Read<Allowlist>("Allowlist.xml");

        public static NetServer MainNetServer;

        public static Resource MainResource;
        public static Dictionary<Command, Action<CommandContext>> Commands;

        public static readonly List<Client> Clients = new();

        public Server()
        {
            Logging.Info("================");
            Logging.Info($"Server version: {Assembly.GetCallingAssembly().GetName().Version}");
            Logging.Info($"Compatible GTACoOp:R versions: {CompatibleVersion.Replace('_', '.')}.x");
            Logging.Info("================");

            // 6d4ec318f1c43bd62fe13d5a7ab28650 = GTACOOP:R
            NetPeerConfiguration config = new("6d4ec318f1c43bd62fe13d5a7ab28650")
            {
                MaximumConnections = MainSettings.MaxPlayers,
                Port = MainSettings.ServerPort,
                EnableUPnP = MainSettings.UPnP
            };

            config.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
            config.EnableMessageType(NetIncomingMessageType.ConnectionLatencyUpdated);

            MainNetServer = new NetServer(config);
            MainNetServer.Start();

            Logging.Info(string.Format("Server listening on {0}:{1}", config.LocalAddress.ToString(), config.Port));

            if (MainSettings.UPnP)
            {
                Logging.Info(string.Format("Attempting to forward port {0}", MainSettings.ServerPort));

                if (MainNetServer.UPnP.ForwardPort(MainSettings.ServerPort, "GTACOOP:R server"))
                {
                    Logging.Info(string.Format("Server available on {0}:{1}", MainNetServer.UPnP.GetExternalIP().ToString(), config.Port));
                }
                else
                {
                    Logging.Error("Port forwarding failed!");
                    Logging.Warning("If you and your friends can join this server, please ignore this error or set UPnP in CoopSettings.xml to \"false\"!");
                }
            }

            if (1 == 1) // TODO
            {
                #region -- MASTERSERVER --
                new Thread(async () =>
                {
                    try
                    {
                        HttpClient httpClient = new();

                        IpInfo info;

                        try
                        {
                            string data = await httpClient.GetStringAsync("https://ipinfo.io/json");

                            info = JsonConvert.DeserializeObject<IpInfo>(data);
                        }
                        catch
                        {
                            info = new() { Country = "?" };
                        }

                        bool responseError = false;

                        while (!responseError)
                        {
                            string msg =
                                "{ " +
                                "\"port\": \"" + MainSettings.ServerPort + "\", " +
                                "\"name\": \"" + MainSettings.ServerName + "\", " +
                                "\"version\": \"" + CompatibleVersion.Replace("_", ".") + "\", " +
                                "\"players\": \"" + MainNetServer.ConnectionsCount + "\", " +
                                "\"maxPlayers\": \"" + MainSettings.MaxPlayers + "\", " +
                                "\"allowlist\": \"" + MainSettings.Allowlist + "\", " +
                                "\"mods\": \"" + MainSettings.ModsAllowed + "\", " +
                                "\"npcs\": \"" + MainSettings.NpcsAllowed + "\", " +
                                "\"country\": \"" + info.Country + "\"" +
                                " }";

                            string responseContent = "";
                            try
                            {
                                HttpResponseMessage response = await httpClient.PostAsync("http://gtacoopr.000webhostapp.com/", new StringContent(msg, Encoding.UTF8, "application/json"));

                                responseContent = await response.Content.ReadAsStringAsync();
                            }
                            catch (Exception ex)
                            {
                                Logging.Error(ex.Message);
                                continue;
                            }

                            if (responseContent != "OK")
                            {
                                Logging.Error(responseContent);
                                responseError = true;
                            }
                            else
                            {
                                // Sleep for 12.5s
                                Thread.Sleep(12500);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Error(ex.Message);
                    }
                }).Start();
                #endregion
            }

            if (!string.IsNullOrEmpty(MainSettings.Resource))
            {
                try
                {
                    Logging.Info("Loading resource...");

                    Assembly asm = Assembly.LoadFrom(AppDomain.CurrentDomain.BaseDirectory + "resources" + Path.DirectorySeparatorChar + MainSettings.Resource + ".dll");
                    Type[] types = asm.GetExportedTypes();
                    IEnumerable<Type> validTypes = types.Where(t => !t.IsInterface && !t.IsAbstract).Where(t => typeof(ServerScript).IsAssignableFrom(t));
                    Type[] enumerable = validTypes as Type[] ?? validTypes.ToArray();

                    if (!enumerable.Any())
                    {
                        Logging.Error("ERROR: No classes that inherit from ServerScript have been found in the assembly. Starting freeroam.");
                    }
                    else
                    {
                        Commands = new();

                        if (Activator.CreateInstance(enumerable.ToArray()[0]) is ServerScript script)
                        {
                            MainResource = new(script);
                        }
                        else
                        {
                            Logging.Warning("Could not create resource: it is null.");
                        }
                    }
                }
                catch (Exception e)
                {
                    Logging.Error(e.Message);
                }
            }

            Listen();
        }

        private void Listen()
        {
            Logging.Info("Listening for clients");

            while (true)
            {
                NetIncomingMessage message;

                while ((message = MainNetServer.ReadMessage()) != null)
                {
                    switch (message.MessageType)
                    {
                        case NetIncomingMessageType.ConnectionApproval:
                            Logging.Info("New incoming connection from: " + message.SenderConnection.RemoteEndPoint.ToString());
                            if (message.ReadByte() != (byte)PacketTypes.HandshakePacket)
                            {
                                Logging.Info(string.Format("Player with IP {0} blocked, reason: Wrong packet!", message.SenderConnection.RemoteEndPoint.Address.ToString()));
                                message.SenderConnection.Deny("Wrong packet!");
                            }
                            else
                            {
                                try
                                {
                                    Packet approvalPacket;
                                    approvalPacket = new HandshakePacket();
                                    approvalPacket.NetIncomingMessageToPacket(message);
                                    GetHandshake(message.SenderConnection, (HandshakePacket)approvalPacket);
                                }
                                catch (Exception e)
                                {
                                    Logging.Info(string.Format("Player with IP {0} blocked, reason: {1}", message.SenderConnection.RemoteEndPoint.Address.ToString(), e.Message));
                                    message.SenderConnection.Deny(e.Message);
                                }
                            }
                            break;
                        case NetIncomingMessageType.StatusChanged:
                            NetConnectionStatus status = (NetConnectionStatus)message.ReadByte();

                            long clientID = message.SenderConnection.RemoteUniqueIdentifier;

                            if (status == NetConnectionStatus.Disconnected && Clients.Any(x => x.ID == clientID))
                            {
                                SendPlayerDisconnectPacket(new PlayerDisconnectPacket() { ID = clientID });
                            }
                            break;
                        case NetIncomingMessageType.Data:
                            // Get packet type
                            byte type = message.ReadByte();

                            // Create packet
                            Packet packet;

                            switch (type)
                            {
                                case (byte)PacketTypes.PlayerConnectPacket:
                                    try
                                    {
                                        packet = new PlayerConnectPacket();
                                        packet.NetIncomingMessageToPacket(message);
                                        SendPlayerConnectPacket(message.SenderConnection, (PlayerConnectPacket)packet);
                                    }
                                    catch (Exception e)
                                    {
                                        message.SenderConnection.Disconnect(e.Message);
                                    }
                                    break;
                                case (byte)PacketTypes.PlayerDisconnectPacket:
                                    try
                                    {
                                        packet = new PlayerDisconnectPacket();
                                        packet.NetIncomingMessageToPacket(message);
                                        SendPlayerDisconnectPacket((PlayerDisconnectPacket)packet);
                                    }
                                    catch (Exception e)
                                    {
                                        message.SenderConnection.Disconnect(e.Message);
                                    }
                                    break;
                                case (byte)PacketTypes.FullSyncPlayerPacket:
                                    try
                                    {
                                        packet = new FullSyncPlayerPacket();
                                        packet.NetIncomingMessageToPacket(message);
                                        FullSyncPlayer((FullSyncPlayerPacket)packet);
                                    }
                                    catch (Exception e)
                                    {
                                        message.SenderConnection.Disconnect(e.Message);
                                    }
                                    break;
                                case (byte)PacketTypes.FullSyncPlayerVehPacket:
                                    try
                                    {
                                        packet = new FullSyncPlayerVehPacket();
                                        packet.NetIncomingMessageToPacket(message);
                                        FullSyncPlayerVeh((FullSyncPlayerVehPacket)packet);
                                    }
                                    catch (Exception e)
                                    {
                                        message.SenderConnection.Disconnect(e.Message);
                                    }
                                    break;
                                case (byte)PacketTypes.LightSyncPlayerPacket:
                                    try
                                    {
                                        packet = new LightSyncPlayerPacket();
                                        packet.NetIncomingMessageToPacket(message);
                                        LightSyncPlayer((LightSyncPlayerPacket)packet);
                                    }
                                    catch (Exception e)
                                    {
                                        message.SenderConnection.Disconnect(e.Message);
                                    }
                                    break;
                                case (byte)PacketTypes.LightSyncPlayerVehPacket:
                                    try
                                    {
                                        packet = new LightSyncPlayerVehPacket();
                                        packet.NetIncomingMessageToPacket(message);
                                        LightSyncPlayerVeh((LightSyncPlayerVehPacket)packet);
                                    }
                                    catch (Exception e)
                                    {
                                        message.SenderConnection.Disconnect(e.Message);
                                    }
                                    break;
                                case (byte)PacketTypes.ChatMessagePacket:
                                    try
                                    {
                                        packet = new ChatMessagePacket();
                                        packet.NetIncomingMessageToPacket(message);
                                        SendChatMessage((ChatMessagePacket)packet);
                                    }
                                    catch (Exception e)
                                    {
                                        message.SenderConnection.Disconnect(e.Message);
                                    }
                                    break;
                                case (byte)PacketTypes.FullSyncNpcPacket:
                                    if (MainSettings.NpcsAllowed)
                                    {
                                        try
                                        {
                                            packet = new FullSyncNpcPacket();
                                            packet.NetIncomingMessageToPacket(message);
                                            FullSyncNpc(message.SenderConnection, (FullSyncNpcPacket)packet);
                                        }
                                        catch (Exception e)
                                        {
                                            message.SenderConnection.Disconnect(e.Message);
                                        }
                                    }
                                    else
                                    {
                                        message.SenderConnection.Disconnect("Npcs are not allowed!");
                                    }
                                    break;
                                case (byte)PacketTypes.FullSyncNpcVehPacket:
                                    if (MainSettings.NpcsAllowed)
                                    {
                                        try
                                        {
                                            packet = new FullSyncNpcVehPacket();
                                            packet.NetIncomingMessageToPacket(message);
                                            FullSyncNpcVeh(message.SenderConnection, (FullSyncNpcVehPacket)packet);
                                        }
                                        catch (Exception e)
                                        {
                                            message.SenderConnection.Disconnect(e.Message);
                                        }
                                    }
                                    else
                                    {
                                        message.SenderConnection.Disconnect("Npcs are not allowed!");
                                    }
                                    break;
                                case (byte)PacketTypes.ModPacket:
                                    if (MainSettings.ModsAllowed)
                                    {
                                        try
                                        {
                                            packet = new ModPacket();
                                            packet.NetIncomingMessageToPacket(message);
                                            ModPacket modPacket = (ModPacket)packet;
                                            if (MainResource != null &&
                                                MainResource.InvokeModPacketReceived(modPacket.ID, modPacket.Target, modPacket.Mod, modPacket.CustomPacketID, modPacket.Bytes))
                                            {
                                                // Was canceled
                                            }
                                            else if (modPacket.Target != -1)
                                            {
                                                NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();
                                                modPacket.PacketToNetOutGoingMessage(outgoingMessage);

                                                if (modPacket.Target != 0)
                                                {
                                                    NetConnection target = MainNetServer.Connections.FirstOrDefault(x => x.RemoteUniqueIdentifier == modPacket.Target);
                                                    if (target.Equals(default(Client)))
                                                    {
                                                        Logging.Error($"[ModPacket] target \"{modPacket.Target}\" not found!");
                                                    }
                                                    else
                                                    {
                                                        // Send back to target
                                                        MainNetServer.SendMessage(outgoingMessage, target, NetDeliveryMethod.ReliableOrdered, 0);
                                                    }
                                                }
                                                else
                                                {
                                                    // Send back to all players
                                                    MainNetServer.SendMessage(outgoingMessage, MainNetServer.Connections, NetDeliveryMethod.ReliableOrdered, 0);
                                                }
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            message.SenderConnection.Disconnect(e.Message);
                                        }
                                    }
                                    else
                                    {
                                        message.SenderConnection.Disconnect("Mods are not allowed!");
                                    }
                                    break;
                                default:
                                    Logging.Error("Unhandled Data / Packet type");
                                    break;
                            }
                            break;
                        case NetIncomingMessageType.ConnectionLatencyUpdated:
                            Client client = Clients.FirstOrDefault(x => x.ID == message.SenderConnection.RemoteUniqueIdentifier);
                            if (!client.Equals(default(Client)))
                            {
                                client.Latency = message.ReadFloat();
                            }
                            break;
                        case NetIncomingMessageType.ErrorMessage:
                            Logging.Error(message.ReadString());
                            break;
                        case NetIncomingMessageType.WarningMessage:
                            Logging.Warning(message.ReadString());
                            break;
                        case NetIncomingMessageType.DebugMessage:
                        case NetIncomingMessageType.VerboseDebugMessage:
                            Logging.Debug(message.ReadString());
                            break;
                        default:
                            Logging.Error(string.Format("Unhandled type: {0} {1} bytes {2} | {3}", message.MessageType, message.LengthBytes, message.DeliveryMethod, message.SequenceChannel));
                            break;
                    }

                    MainNetServer.Recycle(message);
                }

                // 16 milliseconds to sleep to reduce CPU usage
                Thread.Sleep(1000 / 60);
            }
        }

        #region -- PLAYER --
        // Before we approve the connection, we must shake hands
        private void GetHandshake(NetConnection local, HandshakePacket packet)
        {
            Logging.Debug("New handshake from: [SC: " + packet.SocialClubName + " | Name: " + packet.Username + " | Address: " + local.RemoteEndPoint.Address.ToString() + "]");

            if (string.IsNullOrWhiteSpace(packet.Username))
            {
                local.Deny("Username is empty or contains spaces!");
                return;
            }
            else if (packet.Username.Any(p => !char.IsLetterOrDigit(p)))
            {
                local.Deny("Username contains special chars!");
                return;
            }

            if (MainSettings.Allowlist)
            {
                if (!MainAllowlist.SocialClubName.Contains(packet.SocialClubName))
                {
                    local.Deny("This Social Club name is not on the allow list!");
                    return;
                }
            }

            if (!packet.ModVersion.StartsWith(CompatibleVersion))
            {
                local.Deny($"GTACoOp:R version {CompatibleVersion.Replace('_', '.')}.x required!");
                return;
            }

            if (MainBlocklist.SocialClubName.Contains(packet.SocialClubName))
            {
                local.Deny("This Social Club name has been blocked by this server!");
                return;
            }
            else if (MainBlocklist.Username.Contains(packet.Username))
            {
                local.Deny("This Username has been blocked by this server!");
                return;
            }
            else if (MainBlocklist.IP.Contains(local.RemoteEndPoint.ToString().Split(":")[0]))
            {
                local.Deny("This IP was blocked by this server!");
                return;
            }

            if (Clients.Any(x => x.Player.SocialClubName == packet.SocialClubName))
            {
                local.Deny("The name of the Social Club is already taken!");
                return;
            }
            else if (Clients.Any(x => x.Player.Username == packet.Username))
            {
                local.Deny("Username is already taken!");
                return;
            }

            long localID = local.RemoteUniqueIdentifier;

            Client tmpClient;

            // Add the player to Players
            lock (Clients)
            {
                Clients.Add(
                    tmpClient = new Client()
                    {
                        ID = localID,
                        Player = new()
                        {
                            SocialClubName = packet.SocialClubName,
                            Username = packet.Username
                        }
                    }
                );
            }

            NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();

            // Create a new handshake packet
            new HandshakePacket()
            {
                ID = localID,
                SocialClubName = string.Empty,
                Username = string.Empty,
                ModVersion = string.Empty,
                NpcsAllowed = MainSettings.NpcsAllowed
            }.PacketToNetOutGoingMessage(outgoingMessage);

            // Accept the connection and send back a new handshake packet with the connection ID
            local.Approve(outgoingMessage);

            if (MainResource != null)
            {
                MainResource.InvokePlayerHandshake(tmpClient);
            }
        }

        // The connection has been approved, now we need to send all other players to the new player and the new player to all players
        private static void SendPlayerConnectPacket(NetConnection local, PlayerConnectPacket packet)
        {
            Client localClient = Clients.FirstOrDefault(x => x.ID == packet.ID);
            if (localClient.Equals(default(Client)))
            {
                local.Disconnect("No data found!");
                return;
            }

            if (!string.IsNullOrEmpty(MainSettings.WelcomeMessage))
            {
                SendChatMessage(new ChatMessagePacket() { Username = "Server", Message = MainSettings.WelcomeMessage }, new List<NetConnection>() { local });
            }

            List<NetConnection> clients;
            if ((clients = Util.FilterAllLocal(local)).Count > 0)
            {
                // Send all players to local
                clients.ForEach(targetPlayer =>
                {
                    long targetPlayerID = targetPlayer.RemoteUniqueIdentifier;

                    Client targetClient = Clients.FirstOrDefault(x => x.ID == targetPlayerID);
                    if (!targetClient.Equals(default(Client)))
                    {
                        NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();
                        new PlayerConnectPacket()
                        {
                            ID = targetPlayerID,
                            SocialClubName = targetClient.Player.SocialClubName,
                            Username = targetClient.Player.Username
                        }.PacketToNetOutGoingMessage(outgoingMessage);
                        MainNetServer.SendMessage(outgoingMessage, local, NetDeliveryMethod.ReliableOrdered, 0);
                    }
                });

                // Send local to all players
                NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();
                new PlayerConnectPacket()
                {
                    ID = packet.ID,
                    SocialClubName = localClient.Player.SocialClubName,
                    Username = localClient.Player.Username
                }.PacketToNetOutGoingMessage(outgoingMessage);
                MainNetServer.SendMessage(outgoingMessage, clients, NetDeliveryMethod.ReliableOrdered, 0);
            }

            if (MainResource != null)
            {
                MainResource.InvokePlayerConnected(localClient);
            }
        }

        // Send all players a message that someone has left the server
        private static void SendPlayerDisconnectPacket(PlayerDisconnectPacket packet)
        {
            List<NetConnection> clients;
            if ((clients = Util.FilterAllLocal(packet.ID)).Count > 0)
            {
                NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();
                packet.PacketToNetOutGoingMessage(outgoingMessage);
                MainNetServer.SendMessage(outgoingMessage, clients, NetDeliveryMethod.ReliableOrdered, 0);
            }

            Client localClient = Clients.FirstOrDefault(x => x.ID == packet.ID);
            if (localClient.Equals(default(Client)))
            {
                return;
            }

            if (MainResource != null)
            {
                MainResource.InvokePlayerDisconnected(localClient);
            }

            lock (Clients)
            {
                Clients.Remove(localClient);
            }
        }

        private static void FullSyncPlayer(FullSyncPlayerPacket packet)
        {
            Client tmpClient = Clients.FirstOrDefault(x => x.ID == packet.Extra.ID);
            if (tmpClient.Equals(default(Client)))
            {
                NetConnection localConn = MainNetServer.Connections.Find(x => packet.Extra.ID == x.RemoteUniqueIdentifier);
                if (localConn != null)
                {
                    localConn.Disconnect("No data found!");
                }
                return;
            }
            tmpClient.Player.Position = packet.Extra.Position;
            tmpClient.Player.Health = packet.Extra.Health;

            PlayerPacket playerPacket = packet.Extra;
            playerPacket.Latency = tmpClient.Latency;

            packet.Extra = playerPacket;

            MainNetServer.Connections.FindAll(x => x.RemoteUniqueIdentifier != packet.Extra.ID).ForEach(x =>
            {
                NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();

                if (Clients.First(y => y.ID == x.RemoteUniqueIdentifier).Player.IsInRangeOf(packet.Extra.Position, 550f))
                {
                    packet.PacketToNetOutGoingMessage(outgoingMessage);
                }
                else
                {
                    new SuperLightSyncPlayerPacket()
                    {
                        Extra = packet.Extra
                    }.PacketToNetOutGoingMessage(outgoingMessage);
                }

                MainNetServer.SendMessage(outgoingMessage, x, NetDeliveryMethod.UnreliableSequenced, 0);
            });

            if (MainResource != null)
            {
                MainResource.InvokePlayerUpdate(tmpClient);
            }
        }

        private static void FullSyncPlayerVeh(FullSyncPlayerVehPacket packet)
        {
            Client tmpClient = Clients.FirstOrDefault(x => x.ID == packet.Extra.ID);
            if (tmpClient.Equals(default(Client)))
            {
                NetConnection localConn = MainNetServer.Connections.Find(x => packet.Extra.ID == x.RemoteUniqueIdentifier);
                if (localConn != null)
                {
                    localConn.Disconnect("No data found!");
                }
                return;
            }
            tmpClient.Player.Position = packet.Extra.Position;
            tmpClient.Player.Health = packet.Extra.Health;

            PlayerPacket playerPacket = packet.Extra;
            playerPacket.Latency = tmpClient.Latency;

            packet.Extra = playerPacket;

            MainNetServer.Connections.FindAll(x => x.RemoteUniqueIdentifier != packet.Extra.ID).ForEach(x =>
            {
                NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();

                if (Clients.First(y => y.ID == x.RemoteUniqueIdentifier).Player.IsInRangeOf(packet.Extra.Position, 550f))
                {
                    packet.PacketToNetOutGoingMessage(outgoingMessage);
                }
                else
                {
                    new SuperLightSyncPlayerPacket()
                    {
                        Extra = packet.Extra
                    }.PacketToNetOutGoingMessage(outgoingMessage);
                }

                MainNetServer.SendMessage(outgoingMessage, x, NetDeliveryMethod.UnreliableSequenced, 0);
            });

            if (MainResource != null)
            {
                MainResource.InvokePlayerUpdate(tmpClient);
            }
        }

        private static void LightSyncPlayer(LightSyncPlayerPacket packet)
        {
            Client tmpClient = Clients.FirstOrDefault(x => x.ID == packet.Extra.ID);
            if (tmpClient.Equals(default(Client)))
            {
                NetConnection localConn = MainNetServer.Connections.Find(x => packet.Extra.ID == x.RemoteUniqueIdentifier);
                if (localConn != null)
                {
                    localConn.Disconnect("No data found!");
                }
                return;
            }
            tmpClient.Player.Position = packet.Extra.Position;
            tmpClient.Player.Health = packet.Extra.Health;

            PlayerPacket playerPacket = packet.Extra;
            playerPacket.Latency = tmpClient.Latency;

            packet.Extra = playerPacket;

            MainNetServer.Connections.FindAll(x => x.RemoteUniqueIdentifier != packet.Extra.ID).ForEach(x =>
            {
                NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();

                if (Clients.First(y => y.ID == x.RemoteUniqueIdentifier).Player.IsInRangeOf(packet.Extra.Position, 550f))
                {
                    packet.PacketToNetOutGoingMessage(outgoingMessage);
                }
                else
                {
                    new SuperLightSyncPlayerPacket()
                    {
                        Extra = packet.Extra
                    }.PacketToNetOutGoingMessage(outgoingMessage);
                }

                MainNetServer.SendMessage(outgoingMessage, x, NetDeliveryMethod.UnreliableSequenced, 0);
            });

            if (MainResource != null)
            {
                MainResource.InvokePlayerUpdate(tmpClient);
            }
        }

        private static void LightSyncPlayerVeh(LightSyncPlayerVehPacket packet)
        {
            Client tmpClient = Clients.FirstOrDefault(x => x.ID == packet.Extra.ID);
            if (tmpClient.Equals(default(Client)))
            {
                NetConnection localConn = MainNetServer.Connections.Find(x => packet.Extra.ID == x.RemoteUniqueIdentifier);
                if (localConn != null)
                {
                    localConn.Disconnect("No data found!");
                }
                return;
            }
            tmpClient.Player.Position = packet.Extra.Position;
            tmpClient.Player.Health = packet.Extra.Health;

            PlayerPacket playerPacket = packet.Extra;
            playerPacket.Latency = tmpClient.Latency;

            packet.Extra = playerPacket;

            MainNetServer.Connections.FindAll(x => x.RemoteUniqueIdentifier != packet.Extra.ID).ForEach(x =>
            {
                NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();

                if (Clients.First(y => y.ID == x.RemoteUniqueIdentifier).Player.IsInRangeOf(packet.Extra.Position, 550f))
                {
                    packet.PacketToNetOutGoingMessage(outgoingMessage);
                }
                else
                {
                    new SuperLightSyncPlayerPacket()
                    {
                        Extra = packet.Extra
                    }.PacketToNetOutGoingMessage(outgoingMessage);
                }

                MainNetServer.SendMessage(outgoingMessage, x, NetDeliveryMethod.UnreliableSequenced, 0);
            });

            if (MainResource != null)
            {
                MainResource.InvokePlayerUpdate(tmpClient);
            }
        }

        // Send a message to targets or all players
        private static void SendChatMessage(ChatMessagePacket packet, List<NetConnection> targets = null)
        {
            NetOutgoingMessage outgoingMessage;

            if (MainResource != null)
            {
                if (packet.Message.StartsWith('/'))
                {
                    string[] cmdArgs = packet.Message.Split(" ");
                    string cmdName = cmdArgs[0].Remove(0, 1);
                    if (Commands.Any(x => x.Key.Name == cmdName))
                    {
                        string[] argsWithoutCmd = cmdArgs.Skip(1).ToArray();

                        CommandContext ctx = new()
                        {
                            Client = Clients.FirstOrDefault(x => x.Player.Username == packet.Username),
                            Args = argsWithoutCmd
                        };

                        KeyValuePair<Command, Action<CommandContext>> command = Commands.First(x => x.Key.Name == cmdName);

                        if (command.Key.Usage != null && command.Key.ArgsLength != argsWithoutCmd.Length)
                        {
                            NetConnection userConnection = Util.GetConnectionByUsername(packet.Username);
                            if (userConnection == default)
                            {
                                return;
                            }

                            outgoingMessage = MainNetServer.CreateMessage();
                            new ChatMessagePacket()
                            {
                                Username = "Server",
                                Message = command.Key.Usage
                            }.PacketToNetOutGoingMessage(outgoingMessage);
                            MainNetServer.SendMessage(outgoingMessage, userConnection, NetDeliveryMethod.ReliableOrdered, 0);
                            return;
                        }

                        command.Value.Invoke(ctx);
                    }
                    else
                    {
                        NetConnection userConnection = Util.GetConnectionByUsername(packet.Username);
                        if (userConnection == default)
                        {
                            return;
                        }

                        outgoingMessage = MainNetServer.CreateMessage();
                        new ChatMessagePacket()
                        {
                            Username = "Server",
                            Message = "Command not found!"
                        }.PacketToNetOutGoingMessage(outgoingMessage);
                        MainNetServer.SendMessage(outgoingMessage, userConnection, NetDeliveryMethod.ReliableOrdered, 0);
                    }

                    return;
                }

                if (MainResource.InvokeChatMessage(packet.Username, packet.Message))
                {
                    return;
                }
            }

            packet.Message = packet.Message.Replace("~", "");

            outgoingMessage = MainNetServer.CreateMessage();
            packet.PacketToNetOutGoingMessage(outgoingMessage);
            MainNetServer.SendMessage(outgoingMessage, targets ?? MainNetServer.Connections, NetDeliveryMethod.ReliableOrdered, 0);

            Logging.Info(packet.Username + ": " + packet.Message);
        }
        #endregion

        #region -- NPC --
        private static void FullSyncNpc(NetConnection local, FullSyncNpcPacket packet)
        {
            List<NetConnection> clients;
            if ((clients = Util.GetAllInRange(packet.Position, 550f, local)).Count == 0)
            {
                return;
            }

            NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();
            packet.PacketToNetOutGoingMessage(outgoingMessage);
            MainNetServer.SendMessage(outgoingMessage, clients, NetDeliveryMethod.UnreliableSequenced, 0);
        }

        private static void FullSyncNpcVeh(NetConnection local, FullSyncNpcVehPacket packet)
        {
            List<NetConnection> clients;
            if ((clients = Util.GetAllInRange(packet.Position, 550f, local)).Count == 0)
            {
                return;
            }

            NetOutgoingMessage outgoingMessage = MainNetServer.CreateMessage();
            packet.PacketToNetOutGoingMessage(outgoingMessage);
            MainNetServer.SendMessage(outgoingMessage, clients, NetDeliveryMethod.UnreliableSequenced, 0);
        }
        #endregion

        public static void RegisterCommand(string name, string usage, short argsLength, Action<CommandContext> callback)
        {
            Command command = new(name) { Usage = usage, ArgsLength = argsLength };

            if (Commands.ContainsKey(command))
            {
                throw new Exception("Command \"" + command.Name + "\" was already been registered!");
            }

            Commands.Add(command, callback);
        }
        public static void RegisterCommand(string name, Action<CommandContext> callback)
        {
            Command command = new(name);

            if (Commands.ContainsKey(command))
            {
                throw new Exception("Command \"" + command.Name + "\" was already been registered!");
            }

            Commands.Add(command, callback);
        }

        public static void RegisterCommands<T>()
        {
            IEnumerable<MethodInfo> commands = typeof(T).GetMethods().Where(method => method.GetCustomAttributes(typeof(Command), false).Any());

            foreach (MethodInfo method in commands)
            {
                Command attribute = method.GetCustomAttribute<Command>(true);

                RegisterCommand(attribute.Name, attribute.Usage, attribute.ArgsLength, (Action<CommandContext>)Delegate.CreateDelegate(typeof(Action<CommandContext>), method));
            }
        }
    }
}
