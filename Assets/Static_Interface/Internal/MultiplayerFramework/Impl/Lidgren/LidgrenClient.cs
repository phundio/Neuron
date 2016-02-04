﻿using System;
using System.Collections.Generic;
using System.Threading;
using ENet;
using Lidgren.Network;
using Static_Interface.API.LevelFramework;
using Static_Interface.API.NetworkFramework;
using Static_Interface.API.PlayerFramework;
using Static_Interface.API.Utils;
using Static_Interface.Internal.MultiplayerFramework.Client;
using Static_Interface.Internal.MultiplayerFramework.MultiplayerProvider;
using Static_Interface.Neuron;

namespace Static_Interface.Internal.MultiplayerFramework.Impl.Lidgren
{
    public class LidgrenClient : ClientMultiplayerProvider
    {
        private readonly Dictionary<int, List<QueuedData>> _queue = new Dictionary<int, List<QueuedData>>();
        private readonly Dictionary<IPIdentity, NetConnection> _peers = new Dictionary<IPIdentity, NetConnection>();
        private static readonly string RandName = "Player" + new Random().Next(MAX_PLAYERS);
        private NetClient _client;
        private IPIdentity _ident;
        private bool _listen;
        private Thread _listenerThread;
        private string _ip;
        private ushort _port;
        private uint _startTime;
        private bool _connected;
        public LidgrenClient(Connection connection) : base(connection)
        {
        }


        public override void AttemptConnect(string ip, ushort port, string password)
        {
            _ip = ip;
            _port = port;

            NetPeerConfiguration config = new NetPeerConfiguration(GameInfo.NAME + "_Network_Client")
            {
                Port = port
            };
            _client = new NetClient(config);
            _startTime = GetServerRealTime();
            NetConnection conn = _client.Connect(_ip, _port);
            _listenerThread = new Thread(OnConnect);
            _listenerThread.Start();
            LogUtils.Debug("Adding server connection");

            var servIdent = new IPIdentity(0);
            _peers.Add(servIdent, conn);
        }

        private void OnConnect()
        {
            ListenLoop();
        }
        
        public void ListenLoop()
        {
            while (_listen)
            {
                List<NetIncomingMessage> msgs;
                LidgrenCommon.Listen(_client, Connection, _queue, _peers, out msgs);
                
                foreach (NetIncomingMessage msg in msgs)
                {
                    NetConnectionStatus status = (NetConnectionStatus)msg.ReadByte();
                    if (msg.MessageType == NetIncomingMessageType.StatusChanged)
                    {
                        if (status == NetConnectionStatus.Connected)
                            _connected = true;
                            
                        if (status == NetConnectionStatus.Disconnected)
                            _connected = false;
                    }
                }

                if (_connected || GetServerRealTime() - _startTime <= 1000 * 5) continue;

                if (((ClientConnection)Connection).OnPingFailed()) return;
                LogUtils.Debug("Couldn't connect to host");
                LevelManager.Instance.GoToMainMenu();
                _listen = false;
                break;
            }
        }

        public override bool Read(out Identity user, byte[] data, out ulong length, int channel)
        {
            return LidgrenCommon.Read(out user, data, out length, channel, _queue);
        }

        public override bool Write(Identity target, byte[] data, ulong length, SendMethod method, int channel)
        {
            return LidgrenCommon.Write(target, data, length, method, channel, _client, _peers);
        }

        public override void CloseConnection(Identity user)
        {
            LidgrenCommon.CloseConnection(user, _peers);
        }

        public override uint GetServerRealTime()
        {
            return Convert.ToUInt32(DateTime.Now.Millisecond);
        }

        public override void Dispose()
        {
            _listen = false;
            _listenerThread = null;
            _client.Shutdown(nameof(Dispose));
            _client = null;
        }
        public override Identity GetUserID()
        {
            return _ident;
        }

        public override string GetClientName()
        {
            return RandName;
        }

        public override void SetIdentity(ulong serializedIdent)
        {
            _ident = new IPIdentity(serializedIdent);
        }

        public override byte[] OpenTicket()
        {
            return new byte[] { };
        }

        public override bool IsFavoritedServer(string ip, ushort port)
        {
            return false;
        }

        public override void AdvertiseGame(Identity serverID, string ip, ushort port)
        {
           //do nothing
        }

        public override void SetPlayedWith(Identity ident)
        {
            //do nothing
        }


        public override void SetStatus(string status)
        {
            //do nothing
        }


        public override void SetConnectInfo(string ip, ushort port)
        {
            //do nothing
        }

        public override void CloseTicket()
        {
            //do nothing
        }

        public override void FavoriteServer(string ip, ushort port)
        {
            //do nothing
        }
    }
}