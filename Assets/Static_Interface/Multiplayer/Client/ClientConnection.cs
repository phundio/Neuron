﻿using System;
using System.Collections.Generic;
using System.Linq;
using Static_Interface.Level;
using Static_Interface.Multiplayer.Protocol;
using Static_Interface.Multiplayer.Server;
using Static_Interface.Multiplayer.Service.ConnectionProviderService;
using Static_Interface.Objects;
using Static_Interface.PlayerFramework;
using Static_Interface.Utils;
using Steamworks;
using UnityEngine;
using Types = Static_Interface.Objects.Types;
using Time = UnityEngine.Time;

namespace Static_Interface.Multiplayer.Client
{
    public class ClientConnection : Connection
    {
        private float[] _pings;
        private float _ping;
        public const int CONNECTION_TRIES = 5;
        private CSteamID _user;
 
        private int _serverQueryAttempts;
        private ISteamMatchmakingPingResponse _serverPingResponse;
        private HServerQuery _serverQuery = HServerQuery.Invalid;
        private static byte[] _clientHash;
        private string _currentPassword;
        private uint _currentIp;
        private ushort _currentPort;
        private ServerInfo _currentServerInfo;

        public ServerInfo CurrentServerInfo
        {
            get { return _currentServerInfo; }
        }

        private ClientMultiplayerProvider _provider;
        private bool _isFavoritedServer;

        public bool IsFavoritedServer
        {
            get { return _isFavoritedServer; }
        }

        public override MultiplayerProvider Provider
        {
            get { return _provider; }
        }

        public static byte[] ClientHash
        {
            get { return _clientHash;  }
        }

        public override void Send(CSteamID receiver, EPacket type, byte[] data, int length, int id)
        {
            var tmp = data.ToList();
            tmp.Insert(0, type.GetID());
            data = tmp.ToArray();
            length += 1;

            if (receiver == ClientID)
            {
                Receive(ClientID, data, 0, length, id);
                return;
            }
            base.Send(receiver, type, data, length, id);
        }

        protected override void Listen()
        {
            if (((Time.realtimeSinceStartup - LastNet) > CLIENT_TIMEOUT))
            {
                Disconnect(); //Timeout
            }
            else if (((Time.realtimeSinceStartup - LastCheck) > CHECKRATE) && (((Time.realtimeSinceStartup - LastPing) > 1f) || (LastPing < 0f)))
            {
                LastCheck = Time.realtimeSinceStartup;
                LastPing = Time.realtimeSinceStartup;
                Send(ServerID, EPacket.TICK, new byte[] {}, 0, 0);
            }
        }

        public override void Disconnect(string reason = null)
        {
            SteamNetworking.CloseP2PSessionWithUser(ServerID);
            foreach(User user in Clients)
            {
                SteamNetworking.CloseP2PSessionWithUser(user.Identity.ID);
            }

            IsConnectedInternal = false;

            //Todo: OnDisconnectedFromServer()
            LevelManager.Instance.GoToMainMenu();

            SteamFriends.SetRichPresence("connect", null);
            SteamFriends.SetRichPresence("status", "Menu");
        }

        protected override void OnAwake()
        {
            _serverPingResponse = new ISteamMatchmakingPingResponse(OnPingResponded, OnPingFailedToRespond);

            if (SteamAPI.RestartAppIfNecessary((AppId_t)Game.ID))
            {
                throw new Exception("Restarting app from Steam.");
            }
            if (!SteamAPI.Init())
            {
                throw new Exception("Steam API initialization failed.");
            }

            SteamAPIWarningMessageHook_t _apiWarningMessageHook = OnAPIWarningMessage;
            SteamUtils.SetWarningMessageHook(_apiWarningMessageHook);
            CurrentTime = SteamUtils.GetServerRealTime();
            Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
            Callback<GameServerChangeRequested_t>.Create(OnGameServerChangeRequested);
            Callback<GameRichPresenceJoinRequested_t>.Create(OnGameRichPresenceJoinRequested);
            _user = Steamworks.SteamUser.GetSteamID();
            ClientID = _user;
            _clientHash = Hash.SHA1(ClientID);
            ClientName = SteamFriends.GetPersonaName();
        }

        private void OnPersonaStateChange(PersonaStateChange_t callback)
        {
            if ((callback.m_nChangeFlags == EPersonaChange.k_EPersonaChangeName) && (callback.m_ulSteamID == ClientID.m_SteamID))
            {
                ClientName = SteamFriends.GetPersonaName();
                //Todo: OnNameChangeEvent
            }
        }

        private void OnGameServerChangeRequested(GameServerChangeRequested_t callback)
        {
            if (!IsConnected)
            {
                //Todo 
            }
        }

        private void OnGameRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback)
        {
            uint ip;
            ushort port;
            string password;
            if (!IsConnected && TryGetConnect(callback.m_rgchConnect, out ip, out port, out password))
            {
                AttemptConnect(ip, port, password);
            }
        }

        private static bool TryGetConnect(string line, out uint ip, out ushort port, out string pass)
        {
            ip = 0;
            port = 0;
            pass = string.Empty;
            var index = line.ToLower().IndexOf("+connect", StringComparison.Ordinal);
            if (index == -1)
            {
                return false;
            }
            var num2 = line.IndexOf(':', index + 9);
            string str = line.Substring(index + 9, (num2 - index) - 9);
            if (CheckIp(str))
            {
                ip = GetUInt32FromIp(str);
            }
            else if (!uint.TryParse(str, out ip))
            {
                return false;
            }
            var num3 = line.IndexOf(' ', num2 + 1);
            if (num3 == -1)
            {
                if (!ushort.TryParse(line.Substring(num2 + 1, (line.Length - num2) - 1), out port))
                {
                    return false;
                }
                var pwIndex = line.ToLower().IndexOf("+password", StringComparison.Ordinal);
                if (pwIndex != -1)
                {
                    pass = line.Substring(pwIndex + 10, (line.Length - pwIndex) - 10);
                }
                return true;
            }
            if (!ushort.TryParse(line.Substring(num2 + 1, (num3 - num2) - 1), out port))
            {
                return false;
            }
            var passwordIndex = line.ToLower().IndexOf("+password", StringComparison.Ordinal);
            if (passwordIndex != -1)
            {
                pass = line.Substring(passwordIndex + 10, (line.Length - passwordIndex) - 10);
            }
            return true;
        }

        private static bool CheckIp(string ip)
        {
            int index = ip.IndexOf('.');
            if (index == -1)
            {
                return false;
            }
            int num2 = ip.IndexOf('.', index + 1);
            if (num2 == -1)
            {
                return false;
            }
            int num3 = ip.IndexOf('.', num2 + 1);
            if (num3 == -1)
            {
                return false;
            }
            if (ip.IndexOf('.', num3 + 1) != -1)
            {
                return false;
            }
            return true;
        }

        public static uint GetUInt32FromIp(string ip)
        {
            string[] strArray = GetComponentsFromSerial(ip, '.');
            return ((((uint.Parse(strArray[0]) << 0x18) | (uint.Parse(strArray[1]) << 0x10)) | (uint.Parse(strArray[2]) << 8)) | uint.Parse(strArray[3]));
        }

        public static string[] GetComponentsFromSerial(string serial, char delimiter)
        {
            int index;
            List<string> list = new List<string>();
            for (int i = 0; i < serial.Length; i = index + 1)
            {
                index = serial.IndexOf(delimiter, i);
                if (index == -1)
                {
                    list.Add(serial.Substring(i, serial.Length - i));
                    break;
                }
                list.Add(serial.Substring(i, index - i));
            }
            return list.ToArray();
        }


        public void AttemptConnect(uint ip, ushort port, string password)
        {
            if (!IsConnected) return;
            _serverQueryAttempts = 0;
            CleanupServerQuery();
            
            _currentIp = ip;
            _currentPort = port;
            _currentPassword = password;
  
            _serverQuery = SteamMatchmakingServers.PingServer(ip, (ushort)(port + 1), this._serverPingResponse);
            _serverQueryAttempts++;
            //Todo: OnConnect event?
            IsConnectedInternal = true;
        }

        private void CleanupServerQuery()
        {
            if (_serverQuery == HServerQuery.Invalid) return;
            SteamMatchmakingServers.CancelServerQuery(_serverQuery);
            _serverQuery = HServerQuery.Invalid;
        }

        private void OnPingResponded(gameserveritem_t data)
        {
            CleanupServerQuery();
            if (data.m_nAppID == Game.ID)
            {
                ServerInfo info = new ServerInfo(data);

                if (!data.m_bPassword || (_currentPassword != string.Empty))
                {
                    if (((info.Players >= info.MaxPlayers) || (info.MaxPlayers < MultiplayerProvider.MIN_PLAYERS)) ||
                        (info.MaxPlayers > MultiplayerProvider.MAX_PLAYERS)) return;
                    Connect(info);
                    return;
                    // Todo: server full
                }
                else
                {
                    // Todo: no password
                }
            }
            else
            {
                CleanupServerQuery();
                //Todo: Timeout
            }
        }


        private void OnPingFailedToRespond()
        {
            if (_serverQueryAttempts < CONNECTION_TRIES)
            {
                AttemptConnect(_currentIp, _currentPort, _currentPassword);
            }
            else
            {
                CleanupServerQuery();
                //Todo: Timeout
            }
        }

        private void Connect(ServerInfo info)
        {
            if (IsConnected) return;
            IsConnectedInternal = true;
            ResetChannels();
            _currentServerInfo = info;
            ServerID = info.SteamID;
            _pings = new float[4];
            Lag((info.Ping) / 1000f);
            _provider = new ClientMultiplayerProvider(info);

            LastNet = Time.realtimeSinceStartup;
            OffsetNet = 0f;

            Send(ServerID, EPacket.WORKSHOP, new byte[] {}, 0, 0);

            //Todo: Load Level specified by server
            LevelManager.Instance.LoadLevel("DefaultMap");    
        }

        //Todo
        private void OnLevelLoaded()
        {
            int size;
            const string serverPasswordHash = "";
            CSteamID group = CSteamID.Nil;

            object[] args = { ClientName, serverPasswordHash, Game.VERSION, _currentServerInfo.Ping / 1000f, group};
            byte[] packet = ObjectSerializer.GetBytes(0, out size, args);
            Send(ServerID, EPacket.CONNECT, packet, size, 0);
        }

        private void Lag(float currentPing)
        {
            NetworkUtils.GetAveragePing(currentPing, out _ping, _pings);
        }

        protected override Transform AddPlayer(UserIdentity ident, Vector3 point, byte angle, int channel)
        {
            if (ident.ID != ClientID)
            {
                SteamFriends.SetPlayedWith(ident.ID);
            }
            return base.AddPlayer(ident, point, angle, channel);
        }

        protected override void Receive(CSteamID id, byte[] packet, int offset, int size, int channel)
        {
            EPacket parsedPacket = (EPacket) packet[offset];
            if (parsedPacket.IsUpdate())
            {
                foreach (Channel ch in Receivers.Where(ch => ch.ID == channel))
                {
                    ch.Receive(id, packet, offset, size);
                    return;
                }
            }
            else if (id == ServerID)
            {
                uint ip;
                ushort port;
                switch (parsedPacket)
                {
                    case EPacket.TICK:
                        {
                            Send(ServerID, EPacket.TIME, new byte[] { }, 0, 0);
                            return;
                        }
                    case EPacket.TIME:
                        if (LastPing > 0f)
                        {
                            Type[] argTypes = { Types.BYTE_TYPE, Types.SINGLE_TYPE };
                            object[] args = ObjectSerializer.GetObjects(id, offset, 0, packet, argTypes);
                            LastNet = Time.realtimeSinceStartup;
                            OffsetNet = ((float)args[1]) + ((Time.realtimeSinceStartup - LastPing) / 2f);
                            Lag(Time.realtimeSinceStartup - LastPing);
                            LastPing = -1f;
                        }
                        return;

                    case EPacket.SHUTDOWN:
                        Disconnect();
                        return;

                    case EPacket.CONNECTED:
                        {
                            Type[] argTypes = {
                                //[0] package id, [1] steamID, [2] name, [3] group, [4] position, [5], angle, [6] channel
                                Types.STRING_TYPE, Types.STEAM_ID_TYPE, Types.STRING_TYPE, Types.VECTOR3_TYPE, Types.BYTE_TYPE, Types.INT32_TYPE
                            };

                            object[] args = ObjectSerializer.GetObjects(id, offset, 0, packet, argTypes);
                            AddPlayer(new UserIdentity((CSteamID)args[1], (string) args[2], (CSteamID)args[3]), (Vector3)args[4], (byte)args[5], (int)args[6]);
                            return;
                        }
                    case EPacket.VERIFY:
                        break;
                    case EPacket.DISCONNECTED:
                        RemovePlayer(packet[offset + 1]);
                        return;
                    default:
                    {
                        if (parsedPacket != EPacket.ACCEPTED)
                        {
                            if (parsedPacket != EPacket.REJECTED)
                            {
                                //Todo: handle reason
                                Disconnect();
                            }

                            return;
                        }
                        Type[] args = {Types.BYTE_TYPE, Types.UINT32_TYPE, Types.UINT16_TYPE};
                        object[] objects = ObjectSerializer.GetObjects(id, offset, 0, packet, args);
                        ip = (uint) objects[1];
                        port = (ushort) objects[2];

                        //Todo: OnConnectedToServer

                        Steamworks.SteamUser.AdvertiseGame(ServerID, ip, port);

                        //Todo: implement a command line parser
                        SteamFriends.SetRichPresence("connect", string.Concat("+connect ", ip, ":", port));
                        var favoriteIP = ip;
                        var favoritePort = port;
                        _isFavoritedServer = false;
                        for (var game = 0; game < SteamMatchmaking.GetFavoriteGameCount(); game++)
                        {
                            AppId_t appIdT;
                            uint pnIp;
                            ushort connPort;
                            ushort pnQueryPort;
                            uint punFlags;
                            uint lastPlayedOnServer;
                            SteamMatchmaking.GetFavoriteGame(game, out appIdT, out pnIp, out connPort, out pnQueryPort,
                                out punFlags, out lastPlayedOnServer);
                            if (((appIdT != (AppId_t) Game.ID) || (pnIp != favoriteIP)) ||
                                (favoritePort != connPort)) continue;
                            _isFavoritedServer = true;
                            break;
                        }
                        SteamMatchmaking.AddFavoriteGame((AppId_t) Game.ID, ip, port, (ushort) (port + 1), 2,
                            SteamUtils.GetServerRealTime());
                        break;
                    }
                }
            }
        }
    }
}