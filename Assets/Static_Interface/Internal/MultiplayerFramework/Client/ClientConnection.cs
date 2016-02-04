﻿using System;
using System.Linq;
using Static_Interface.API.LevelFramework;
using Static_Interface.API.NetworkFramework;
using Static_Interface.API.PlayerFramework;
using Static_Interface.API.Utils;
using Static_Interface.Internal.MultiplayerFramework.Impl.ENet;
using Static_Interface.Internal.MultiplayerFramework.Impl.Steamworks;
using Static_Interface.Internal.MultiplayerFramework.MultiplayerProvider;
using Static_Interface.Internal.Objects;
using Static_Interface.Neuron;
using UnityEngine;
using Types = Static_Interface.Internal.Objects.Types;

namespace Static_Interface.Internal.MultiplayerFramework.Client
{
    public class ClientConnection : Connection
    {
        private float[] _pings;
        private float _ping;
        public const int CONNECTION_TRIES = 5;
        private int _serverQueryAttempts;

        internal string CurrentPassword;
        private string _currentIp;
        private ushort _currentPort;

        public ServerInfo CurrentServerInfo { get; private set; }
        public bool IsFavoritedServer { get; private set; }

        internal override void Listen()
        {
            if (((Time.realtimeSinceStartup - LastNet) > CLIENT_TIMEOUT))
            {
                LogUtils.Log("Timeout occurred");
                //Disconnect(); //Timeout
            }
            else if (((Time.realtimeSinceStartup - LastCheck) > CHECKRATE) && (((Time.realtimeSinceStartup - LastPing) > 1f) || (LastPing < 0f)))
            {
                LastCheck = Time.realtimeSinceStartup;
                LastPing = Time.realtimeSinceStartup;
                Send(ServerID, EPacket.TICK, new byte[] {}, 0, 0);
            }

            Send(ServerID, EPacket.TICK, new byte[] {}, 0, 0);
        }

        public override void Disconnect(string reason = null)
        {
            LevelManager.Instance.GoToMainMenu();
            Dispose();
        }

        public override void Dispose()
        {
            Provider.Dispose();
            Provider.CloseConnection(ServerID);
            foreach (User user in Clients)
            {
                Provider.CloseConnection(user.Identity);
            }

            ((ClientMultiplayerProvider)Provider).CloseTicket();
            IsConnected = false;

            //Todo: OnDisconnectedFromServer()


            ((ClientMultiplayerProvider)Provider).SetStatus("Menu");
            ((ClientMultiplayerProvider)Provider).SetConnectInfo(null, 0);
            Destroy(this);
        }

        public void AttemptConnect(string ip, ushort port, string password, bool reset = true)
        {
            Provider = new ENetClient(this);
            ClientID = ((ClientMultiplayerProvider)Provider).GetUserID();
            ClientName = ((ClientMultiplayerProvider)Provider).GetClientName();
            CurrentTime = Provider.GetServerRealTime();
            LogUtils.Log("Attempting conncetion to " + ip + ":" + port + " (using password: " + (string.IsNullOrEmpty(password) ? "NO" : "YES") + ")");
            if (IsConnected)
            {
                LogUtils.Debug("Already connnected");
                return;
            }

            if (reset)
            {
                _serverQueryAttempts = 0;
            }

            _currentIp = ip;
            _currentPort = port;
            CurrentPassword = password;

            ((ClientMultiplayerProvider) Provider).AttemptConnect(ip, port, password);
        }

        internal void Connect(ServerInfo info)
        {
            if (IsConnected) return;
            ClientID = ((ClientMultiplayerProvider) Provider).GetUserID();
            LogUtils.Debug("Connected to server: " + info.Name);
            IsConnected = true;
            ResetChannels();
            CurrentServerInfo = info;
            ServerID = info.ServerID;
            _pings = new float[4];
            Lag((info.Ping) / 1000f);
            LastNet = Time.realtimeSinceStartup;
            OffsetNet = 0f;
            SetupPseudoChannel();
            Send(ServerID, EPacket.WORKSHOP, new byte[] { }, 0, 0);
            //Todo: Load Level specified by server
            LevelManager.Instance.LoadLevel("DefaultMap");    
        }

        //Todo
        private void OnLevelLoaded()
        {
            int size;
            ulong group = 0;

            object[] args = { ClientName, group, GameInfo.VERSION, CurrentServerInfo.Ping / 1000f};
            byte[] packet = ObjectSerializer.GetBytes(0, out size, args);
            Send(ServerID, EPacket.CONNECT, packet, size, 0);
        }

        private void Lag(float currentPing)
        {
            NetworkUtils.GetAveragePing(currentPing, out _ping, _pings);
        }

        protected override Transform AddPlayer(Identity ident, string @name, ulong group, Vector3 point, byte angle, int channel)
        {
            var transform = base.AddPlayer(ident, @name, group, point, angle, channel);;
            if (ident != ClientID)
            {
                ((ClientMultiplayerProvider) Provider).SetPlayedWith(ident);
            }
            else
            {
                Player.MainPlayer = transform.GetComponent<Player>();
            }
            return transform;
        }

        internal override void Receive(Identity id, byte[] packet, int offset, int size, int channel)
        {
            base.Receive(id, packet, offset, size, channel);
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
                                //[0] package id, [1] id, [2] name, [3] group, [4] position, [5], angle, [6] channel
                                Types.BYTE_TYPE, Types.UINT64_TYPE, Types.STRING_TYPE, Types.UINT64_TYPE, Types.VECTOR3_TYPE, Types.BYTE_TYPE, Types.INT32_TYPE
                            };

                            object[] args = ObjectSerializer.GetObjects(id, offset, 0, packet, argTypes);
                            var @name = (string) args[2];
                            AddPlayer(id, @name, (ulong)args[3], (Vector3)args[4], (byte)args[5], (int)args[6]);
                            return;
                        }
                    case EPacket.VERIFY:
                        byte[] ticket = ((ClientMultiplayerProvider)Provider).OpenTicket();
                        if (ticket == null)
                        {
                            Disconnect();
                            return;
                        }
                        Send(ServerID, EPacket.AUTHENTICATE, ticket, ticket.Length, 0);
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

                        object[] args = ObjectSerializer.GetObjects(id, offset, 0, packet, Types.UINT64_TYPE);
                        ((ClientMultiplayerProvider)Provider).SetIdentity((ulong) args[1]);    
                        ((ClientMultiplayerProvider) Provider).AdvertiseGame(ServerID, _currentIp, _currentPort);    
                        ((ClientMultiplayerProvider)Provider).SetConnectInfo(_currentIp, _currentPort);
                        IsFavoritedServer = ((ClientMultiplayerProvider)Provider).IsFavoritedServer(_currentIp, _currentPort);
                        ((ClientMultiplayerProvider) Provider).FavoriteServer(_currentIp, _currentPort);

                        //Todo: load extensions
                        break;
                    }
                }
            }
        }

        public bool OnPingFailed()
        {
            if (_serverQueryAttempts >= CONNECTION_TRIES)
            {
                return false;
            }
            _serverQueryAttempts++;
            LogUtils.Log("Retrying #" + _serverQueryAttempts);
            AttemptConnect(_currentIp, _currentPort, CurrentPassword, false);
            Provider.Dispose();
            return true;
        }
    }
}