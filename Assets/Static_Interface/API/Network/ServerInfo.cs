﻿using Static_Interface.API.Player;
using Steamworks;

namespace Static_Interface.API.Network
{
    public class ServerInfo
    {
        public bool HasPassword { get; internal set; }

        public bool IsSecure { get; internal set;  }

        public string Map { get; internal set; }

        public int MaxPlayers { get; internal set; }

        public string Name { get; internal set; }

        public int Ping { get; internal set; }

        public int Players { get; internal set; }

        public Identity ServerID { get; internal set; }
    }
}
