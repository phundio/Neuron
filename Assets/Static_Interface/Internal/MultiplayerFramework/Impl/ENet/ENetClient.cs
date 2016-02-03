﻿using System;
using System.Collections.Generic;
using System.Threading;
using ENet;
using Static_Interface.API.Player;
using Static_Interface.Internal.MultiplayerFramework.MultiplayerProvider;

namespace Static_Interface.Internal.MultiplayerFramework.Impl.ENet
{
    public class ENetClient : ClientMultiplayerProvider
    {
        private readonly Dictionary<byte, List<ENetQueuedData>> _queue = new Dictionary<byte, List<ENetQueuedData>>();
        private readonly Dictionary<ENetIdentity, Peer> _peers = new Dictionary<ENetIdentity, Peer>();
        private static ENetIdentity _ident = new ENetIdentity(1);
        private static readonly string RandName = "Player" + new Random().Next(MAX_PLAYERS);
        private Host _host;

        public ENetClient(Connection connection) : base(connection)
        {
            
        }

        public override void AttemptConnect(string ip, ushort port, string password)
        {
            _host = new Host();
            _host.InitializeClient(MAX_PLAYERS);
            var serverPeer = _host.Connect(ip, port, 0);
            _peers.Add(new ENetIdentity(0), serverPeer);
            new Thread(Listen).Start();
        }

        public override bool Read(out Identity user, byte[] data, out ulong length, int channel)
        {
            return ENetCommon.Read(out user, data, out length, channel, _queue);
        }

        public override bool Write(Identity target, byte[] data, ulong length, SendMethod method, int channel)
        {
            return ENetCommon.Write(target, data, length, method, channel, _peers);
        }

        public override void CloseConnection(Identity user)
        {
            ENetCommon.CloseConnection(user, _peers);
        }

        private void Listen()
        {
            ENetCommon.Listen(_host, Connection, _queue, _peers);
        }

        public override string GetClientName()
        {
            return RandName;
        }

        public override Identity GetUserID()
        {
            return _ident;
        }

        public override uint GetServerRealTime()
        {
            return Convert.ToUInt32(DateTime.Now.Millisecond);
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

        public override bool IsFavoritedServer(string ip, ushort port)
        {
            return false;
        }

        public override byte[] OpenTicket()
        {
            return new byte[] {0};
        }

        public override void CloseTicket()
        {
            //do nothing
        }

        public override void FavoriteServer(string ip, ushort port)
        {
            //do nothing
        }

        public override void SetIdentity(ulong serializedIdent)
        {
            _ident = new ENetIdentity(serializedIdent);
        }
    }
}