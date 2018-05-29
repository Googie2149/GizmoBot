using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using Newtonsoft.Json;
using System.Security.Cryptography;
using GizmoBot.Preconditions;
using SteamKit2;

namespace GizmoBot.Modules.Steam
{
    public class Steam : MinitoriModule
    {
        private Config config;

        private SteamClient steamClient;
        private CallbackManager manager;
        private SteamUser steamUser;

        public Steam(Config _config)
        {
            config = _config;
        }
    }
}
