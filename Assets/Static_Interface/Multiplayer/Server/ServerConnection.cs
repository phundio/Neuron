﻿using System;
using System.Collections.Generic;
using System.Linq;
using Static_Interface.Level;
using Static_Interface.Multiplayer.Protocol;
using Static_Interface.Multiplayer.Service.ConnectionProviderService;
using Static_Interface.Objects;
using Static_Interface.PlayerFramework;
using Steamworks;
using UnityEngine;
using Types = Static_Interface.Objects.Types;

namespace Static_Interface.Multiplayer.Server
{
    public class ServerConnection : Connection
    {
        private const string _map = "DefaultMap";

        public string Map
        {
            get { return _map; }
        }

        private const uint _bindIP = 0;

        public uint BindIP
        {
            get { return _bindIP;}
        }

        public int MaxPlayers = 8;

        private const float Timeout = 0.75f;
        private ushort _port;

        public ushort Port
        {
            get { return _port;}
        }

        public uint PublicIP
        {
            get { return SteamGameServer.GetPublicIP(); }
        }

        private readonly List<PendingUser> _pendingPlayers = new List<PendingUser>();

        public ICollection<PendingUser> PendingPlayers
        {
            get { return _pendingPlayers.AsReadOnly(); }
        } 

        private ServerMultiplayerProvider _provider;
        public override MultiplayerProvider Provider
        {
            get { return _provider; }
        }

        private bool _isSecure;

        public bool IsSecure
        {
            get { return _isSecure;}
        }

        protected override void Listen()
        {
            if ((Time.realtimeSinceStartup - LastCheck) > CHECKRATE)
            {
                LastCheck = Time.realtimeSinceStartup;
                foreach (var c in Clients)
                {
                    if (((Time.realtimeSinceStartup - c.LastPing) > 1f) || (c.LastPing < 0f))
                    {
                        c.LastPing = Time.realtimeSinceStartup;
                        Send(c.Identity.ID, EPacket.TICK, new byte[] { }, 0, 0);
                    }
                }
            }

            foreach (User c in Clients.Where(
                c => ((Time.realtimeSinceStartup - c.LastNet) > SERVER_TIMEOUT) ||
                (((Time.realtimeSinceStartup - c.Joined) > SERVER_TIMEOUT) && (c.LastPing > Timeout))))
            {
                DisconnectClient(c.Identity.ID);
            }

            foreach (PendingUser c in _pendingPlayers.Where(c => 
                (Time.realtimeSinceStartup - c.Joined) > PENDING_TIMEOUT))
            {
                Reject(c.Identity.ID, ERejectionReason.TIMEOUT);
            }
        }

        public override void Disconnect(string reason = null)
        {
            CloseGameServer();
        }

        protected override void Receive(CSteamID source, byte[] packet, int offset, int size, int channel)
        {
            var net = ((OffsetNet + Time.realtimeSinceStartup) - LastNet);

            EPacket parsedPacket = (EPacket)packet[offset];
            if (parsedPacket.IsUpdate())
            {
                if (source == ServerID)
                {
                    foreach(Channel ch in Receivers)
                    {
                       ch.Receive(source, packet, offset, size);
                    }
                }
                else
                {
                    if (Clients.All(client => client.Identity.ID != source)) return;
                    foreach (Channel ch in Receivers)
                    {
                        ch.Receive(source, packet, offset, size);
                    }
                }
                return;
            }

            PendingUser currentPending;
            switch (parsedPacket)
            {
                case EPacket.WORKSHOP:
                {
                    //workshop list {none for now}
                    List<ulong> list = new List<ulong>();

                    byte[] args = new byte[1 + (list.Count * 8)];
                    args[0] = (byte)list.Count;
                    for (byte i = 0; i < list.Count; i = (byte)(i + 1))
                    {
                        BitConverter.GetBytes(list[i]).CopyTo(args, (1 + (i * 8)));
                    }
                    Send(source, EPacket.WORKSHOP, args, args.Length, 0);
                    return;
                }

                case EPacket.TICK:
                {
                    int length;
                    object[] objects = { net };
                    byte[] buffer2 = ObjectSerializer.GetBytes(0, out length, objects);
                    Send(source, EPacket.TIME, buffer2, length, 0);
                    return;
                }
                case EPacket.TIME:
                    foreach (User c in Clients.Where(c => c.Identity.ID == source))
                    {
                        if (!(c.LastPing > 0f)) return;
                        c.LastNet = Time.realtimeSinceStartup;
                        c.Lag(Time.realtimeSinceStartup - c.LastPing);
                        c.LastPing = -1f;
                        return;
                    }
                    return;

                case EPacket.CONNECT:
                {
                    if (_pendingPlayers.Any(p => p.Identity.ID == source))
                    {
                        Reject(source, ERejectionReason.ALREADY_PENDING);
                        return;
                    }

                    if (Clients.Any(c => c.Identity.ID == source))
                    {
                        Reject(source, ERejectionReason.ALREADY_CONNECTED);
                        return;
                    }

                    Type[] argTypes = {
                        //[0] package id, [1] name, [2] hashedPassword, [3] group, [4] version, [5] point, [6], angle, [7] channel
                        Types.STRING_TYPE, Types.STEAM_ID_TYPE, Types.STRING_TYPE, Types.VECTOR3_TYPE, Types.BYTE_TYPE, Types.INT32_TYPE
                    };

                    var args = ObjectSerializer.GetObjects(source, offset, 0, packet, argTypes);
                    UserIdentity playerIdent = new UserIdentity(source, (string) args[1], (CSteamID) args[3]);

                    if (((string)args[4]) != Game.VERSION)
                    {
                        Reject(source, ERejectionReason.WRONG_VERSION);
                        return;
                    }

                    if ((Clients.Count + 1) > MultiplayerProvider.MAX_PLAYERS)
                    {
                        Reject(source, ERejectionReason.SERVER_FULL);
                        return;
                    }

                    _pendingPlayers.Add(new SteamPendingUser(playerIdent));
                    Send(source, EPacket.VERIFY, new byte[] { }, 0, 0);
                    return;
                }

                default:
                    if (parsedPacket != EPacket.AUTHENTICATE)
                    {
                        Debug.LogError("Failed to handle message: " + parsedPacket);
                        return;
                    }

                    currentPending = _pendingPlayers.FirstOrDefault(p => p.Identity.ID == source);
                    break;
            }

            if (currentPending == null)
            {
                Reject(source, ERejectionReason.NOT_PENDING);
            }
            else if ((Clients.Count + 1) > MultiplayerProvider.MAX_PLAYERS)
            {
                Reject(source, ERejectionReason.SERVER_FULL);
            }
            else
            {
                var srcOffset = BitConverter.ToUInt16(packet, 1);
                var dst = new byte[srcOffset];
                System.Buffer.BlockCopy(packet, 3, dst, 0, srcOffset);
                var count2 = BitConverter.ToUInt16(packet, 3 + srcOffset);
                byte[] count = new byte[count2];
                System.Buffer.BlockCopy(packet, 5 + count2, count, 0, count2);
                if (!VerifyTicket(source, dst))
                {
                    Reject(source, ERejectionReason.AUTH_VERIFICATION);
                }
            }
        }

        public void Reject(CSteamID user, ERejectionReason reason)
        {
            foreach (var player in _pendingPlayers.Where(player => player.Identity.ID == user))
            {
                PendingPlayers.Remove(player);
            }

            SteamGameServer.EndAuthSession(user);
            byte[] data = {(byte)reason};
            Send(user, EPacket.REJECTED, data, data.Length, 0);
        }

        public void DisconnectClient(CSteamID user)
        {
            byte index = GetUserIndex(user);
            RemovePlayer(index);
            byte[] packet = { index };
            AnnounceToAll(EPacket.DISCONNECTED, packet, packet.Length, 0);
        }

        public byte GetUserIndex(CSteamID user)
        {
            byte index = 0;
            foreach (User client in Clients)
            {
                if (client.Identity.ID == user)
                {
                    return index;
                }
                index++;
            }

            throw new Exception("User not found: " + user);
        }

        public void AnnounceToAll(EPacket packet, byte[] data, int size, int channel)
        {
            foreach (var c in Clients)
            {
                Send(c.Identity.ID, packet, data, size, channel);
            }
        }

        public override void Send(CSteamID receiver, EPacket type, byte[] data, int length, int id)
        {
            var tmp = data.ToList();
            tmp.Insert(0, type.GetID());
            data = tmp.ToArray();
            length += 1;

            if (receiver == ServerID)
            {
                Receive(ServerID, data, 0, length, id);
                return;
            }
            base.Send(receiver, type, data, length, id);
        }

        protected override void OnAwake()
        {
            _provider = new ServerMultiplayerProvider();
            Callback<GSPolicyResponse_t>.CreateGameServer(OnGsPolicyResponse);
            Callback<P2PSessionConnectFail_t>.CreateGameServer(OnP2PSessionConnectFail);
            Callback<ValidateAuthTicketResponse_t>.CreateGameServer(OnValidateAuthTicketResponse);
            _port = 27015;
        }

        private void OnGsPolicyResponse(GSPolicyResponse_t callback)
        {
            if (callback.m_bSecure != 0)
            {
                _isSecure = true;
            }
            else if (_isSecure)
            {
                _isSecure = false;
            }
        }

        private void OnP2PSessionConnectFail(P2PSessionConnectFail_t callback)
        {
            DisconnectClient(callback.m_steamIDRemote);
        }

        private void OnValidateAuthTicketResponse(ValidateAuthTicketResponse_t callback)
        {
            if (callback.m_eAuthSessionResponse != EAuthSessionResponse.k_EAuthSessionResponseOK)
            {
                if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseUserNotConnectedToSteam)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_NO_STEAM);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseNoLicenseOrExpired)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_LICENSE_EXPIRED);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseVACBanned)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_VAC_BAN);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseLoggedInElseWhere)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_ELSEWHERE);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseVACCheckTimedOut)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_TIMED_OUT);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseAuthTicketCanceled)
                {
                    DisconnectClient(callback.m_SteamID);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseAuthTicketInvalidAlreadyUsed)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_USED);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseAuthTicketInvalid)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_NO_USER);
                }
                else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponsePublisherIssuedBan)
                {
                    Reject(callback.m_SteamID, ERejectionReason.AUTH_PUB_BAN);
                }
                return;
            }

            PendingUser pending = _pendingPlayers.FirstOrDefault(pendingPlayer => pendingPlayer.Identity.ID == callback.m_SteamID);
            if (pending == null)
            {
                Reject(callback.m_SteamID, ERejectionReason.NOT_PENDING);
                return;
            }

            pending.HasAuthentication = true;
            Accept(pending);
        }

        public void Accept(PendingUser user)
        {
            UserIdentity ident = user.Identity;
            if (!user.HasAuthentication) return;
            _pendingPlayers.Remove(user);
            SteamGameServer.BUpdateUserData(ident.ID, ident.PlayerName, 0);
            Vector3 spawn = Vector3.zero;
            byte angle = 0;
            int size;
            //Todo: savefile

            int channels = Channels;
            Transform player = AddPlayer(ident, spawn, angle, channels);
            object[] data;
            byte[] packet;
            foreach (var c in Clients)
            {
                data = new object[] { c.Identity.ID, c.Identity.PlayerName, c.Identity.Group, c.Model.position, c.Model.rotation.eulerAngles.y / 2f };
                packet = ObjectSerializer.GetBytes(0, out size, data);
                Send(user.Identity.ID, EPacket.CONNECTED, packet, data.Length, 0);
            }

            object[] objects = { PublicIP, Port };
            packet = ObjectSerializer.GetBytes(0, out size, objects);
            Send(ident.ID, EPacket.ACCEPTED, packet, size, 0);
            data = new object[] { ident.ID, ident.PlayerName, ident.Group, player.position, player.rotation.eulerAngles.y / 2f };

            packet = ObjectSerializer.GetBytes(0, out size, data);
            AnnounceToAll(EPacket.CONNECTED, packet, size, 0);
            //Todo: OnUserConnectedEvent
        }

        public void OpenGameServer()
        {
            try
            {
                _provider.Open(_bindIP, Port);
            }
            catch (Exception exception)
            {
                Debug.LogError(exception.Message);
                Application.Quit();
                return;
            }
            SteamAPIWarningMessageHook_t apiWarningMessageHook = OnAPIWarningMessage;
            SteamUtils.SetWarningMessageHook(apiWarningMessageHook);
            CurrentTime = SteamGameServerUtils.GetServerRealTime();
            LevelManager.Instance.LoadLevel(_map); //Todo
            SteamGameServer.SetMaxPlayerCount(MaxPlayers);
            SteamGameServer.SetServerName(_provider.Description);
            SteamGameServer.SetPasswordProtected(false); //Todo
            SteamGameServer.SetMapName(_map);

            ServerID = SteamGameServer.GetSteamID();
            ClientID = ServerID;

            ClientName = "Console";
            LastNet = Time.realtimeSinceStartup;
            OffsetNet = 0f;

            //Todo: OnServerStart
        }

        public void CloseGameServer()
        {
            //Todo: OnServerShutdown
            _provider.Close();
        }

        public bool VerifyTicket(CSteamID user, byte[] ticket)
        {
            return (SteamGameServer.BeginAuthSession(ticket, ticket.Length, user) == EBeginAuthSessionResult.k_EBeginAuthSessionResultOK);
        }
    }
}