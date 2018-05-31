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

namespace GizmoBot.Modules.Steam
{
    public class Steam : MinitoriModule
    {
        private Config config;

        public Steam(Config _config)
        {
            config = _config;
        }
    }
}
