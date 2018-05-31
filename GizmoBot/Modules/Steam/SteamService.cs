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
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace GizmoBot.Modules.Steam
{
    public class SteamService
    {
        private Config config;
        private DiscordSocketClient client;
        
        bool isRunning = false;
        
        public async Task Install(IServiceProvider _services)
        {
            client = _services.GetService<DiscordSocketClient>();
            config = _services.GetService<Config>();
            
        }
    }
}
