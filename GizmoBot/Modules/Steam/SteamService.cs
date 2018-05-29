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
using SteamKit2;

namespace GizmoBot.Modules.Steam
{
    public class SteamService
    {
        private Config config;
        private DiscordSocketClient client;
        
        private SteamClient steamClient;
        private CallbackManager manager;
        private SteamUser steamUser;

        bool isRunning = false;
        
        public async Task Install(IServiceProvider _services)
        {
            client = _services.GetService<DiscordSocketClient>();
            config = _services.GetService<Config>();

            steamClient = new SteamClient();
            manager = new CallbackManager(steamClient);

            steamUser = steamClient.GetHandler<SteamUser>();

            manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

            manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

            isRunning = true;

            Console.WriteLine("Connecting to steam...");

            steamClient.Connect();

            while(isRunning)
            {
                manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Console.WriteLine($"Connected to Steam! Logging in...");

            steamUser.LogOnAnonymous();
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Console.WriteLine("Disconnected from Steam");

            isRunning = false;
        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                if (callback.Result == EResult.AccountLogonDenied)
                {
                    Console.WriteLine($"Unable to login: Account is steamguard protected");

                    isRunning = false;
                    return;
                }

                Console.WriteLine($"Unable to login: {callback.Result} / {callback.ExtendedResult}");

                isRunning = false;
                return;
            }

            Console.WriteLine("Logged in!");

            // Do stuff here later

            steamUser.LogOff();
        }

        private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine($"Logged off: {callback.Result}");
        }
    }
}
