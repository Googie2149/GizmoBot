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
using System.Security.Cryptography;

namespace GizmoBot.Modules.Steam
{
    // Note: Basically this entire service is based off of samples on the SteamKit repo
    public class SteamService
    {
        private Config config;
        private DiscordSocketClient client;

        private SteamClient steamClient;
        private CallbackManager manager;
        private SteamUser steamUser;
        private SteamApps steamApps;

        private string authCode, twoFactorAuth;

        //private JobID badgeRequest = JobID.Invalid;

        bool isRunning = false;

        public async Task Install(IServiceProvider _services)
        {
            client = _services.GetService<DiscordSocketClient>();
            config = _services.GetService<Config>();

            steamClient = new SteamClient();
            manager = new CallbackManager(steamClient);

            steamUser = steamClient.GetHandler<SteamUser>();
            steamApps = steamClient.GetHandler<SteamApps>();

            manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);

            manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

            manager.Subscribe<SteamUser.UpdateMachineAuthCallback>(OnMachineAuth);

            isRunning = true;

            Console.WriteLine("Connecting to steam...");

            steamClient.Connect();

            Task.Run(() =>
            {
                while (isRunning)
                {
                    manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                }
            }).ConfigureAwait(false);
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Console.WriteLine($"Connected to Steam! Logging in...");

            if (config.SteamUsername == "")
            {
                steamUser.LogOnAnonymous();
            }
            else
            {
                byte[] sentryHash = null;
                if (File.Exists("sentry.bin"))
                {
                    // if we have a saved sentry file, read and sha-1 hash it
                    byte[] sentryFile = File.ReadAllBytes("sentry.bin");
                    sentryHash = CryptoHelper.SHAHash(sentryFile);
                }

                steamUser.LogOn(new SteamUser.LogOnDetails
                {
                    Username = config.SteamUsername,
                    Password = config.SteamPassword,
                    AuthCode = authCode,
                    TwoFactorCode = twoFactorAuth,
                    SentryFileHash = sentryHash
                });
            }
        }

        private async void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Console.WriteLine("Disconnected from Steam");

            if (callback.UserInitiated)
                isRunning = false;
            else
            {
                await Task.Delay(5000);
                steamClient.Connect();
            }
        }

        private async void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            bool isSteamGuard = callback.Result == EResult.AccountLogonDenied;
            bool is2FA = callback.Result == EResult.AccountLoginDeniedNeedTwoFactor;

            if (isSteamGuard || is2FA)
            {
                Console.WriteLine("This account is SteamGuard protected!");

                if (is2FA)
                {
                    Console.Write("Please enter your 2 factor auth code from your authenticator app: ");
                    twoFactorAuth = Console.ReadLine();
                }
                else
                {
                    Console.Write("Please enter the auth code sent to the email at {0}: ", callback.EmailDomain);
                    authCode = Console.ReadLine();
                }

                return;
            }


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

            //var depotJob = steamApps.GetDepotDecryptionKey(depotid: 441, appid: 440);

            //SteamApps.DepotKeyCallback depotKey = await depotJob;

            //if (depotKey.Result == EResult.OK)
            //{
            //    Console.WriteLine($"Got our depot key: {BitConverter.ToString(depotKey.DepotKey)}");
            //}
            //else
            //{
            //    Console.WriteLine("Unable to request depot key!");
            //}

            //var moreChanges = await steamApps.PICSGetProductInfo(440, package: null);

            //Console.WriteLine();

            bool update = false;

            while (true)
            {
                await Task.Delay(1000 * 60 * 2); // wait 2 minutes

                try
                {
                    var changes = await steamApps.PICSGetChangesSince(config.ChangeNumber);

                    if (config.ChangeNumber == 0 || changes.AppChanges.Count() == 0)
                    {
                        Console.WriteLine($"Empty changelist! {config.ChangeNumber}");
                        config.ChangeNumber = changes.CurrentChangeNumber;
                        continue;
                    }

                    if (config.ChangeNumber == changes.CurrentChangeNumber)
                    {
                        Console.WriteLine("No new changes");
                        continue;
                    }

                    config.ChangeNumber = changes.CurrentChangeNumber;

                    if (changes.AppChanges.Count() == 0)
                    {
                        Console.WriteLine("No new app changes");
                        continue;
                    }

                    update = true;

                    // This should send a list of AppIds that are followed by at least one channel.
                    await Broadcast(changes.AppChanges.Keys.Where(x => config.SteamSettings.Keys.Contains(x)).ToList());

                    if (update)
                    {
                        await config.Save();
                        update = false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in steam changes\nMessage: {ex.Message}\nSource: {ex.Source}\n{ex.InnerException}");
                }
            }
        }

        private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine($"Logged off: {callback.Result}");
        }

        private void OnMachineAuth(SteamUser.UpdateMachineAuthCallback callback)
        {
            Console.WriteLine("Updating sentryfile...");

            int fileSize;
            byte[] sentryHash;
            using (var fs = File.Open("sentry.bin", FileMode.OpenOrCreate, FileAccess.ReadWrite))
            {
                fs.Seek(callback.Offset, SeekOrigin.Begin);
                fs.Write(callback.Data, 0, callback.BytesToWrite);
                fileSize = (int)fs.Length;

                fs.Seek(0, SeekOrigin.Begin);
                using (var sha = SHA1.Create())
                {
                    sentryHash = sha.ComputeHash(fs);
                }
            }

            // inform the steam servers that we're accepting this sentry file
            steamUser.SendMachineAuthResponse(new SteamUser.MachineAuthDetails
            {
                JobID = callback.JobID,

                FileName = callback.FileName,

                BytesWritten = callback.BytesToWrite,
                FileSize = fileSize,
                Offset = callback.Offset,

                Result = EResult.OK,
                LastError = 0,

                OneTimePassword = callback.OneTimePassword,

                SentryFileHash = sentryHash,
            });

            Console.WriteLine("Done!");
        }

        private async Task Broadcast(List<uint> changedIds)
        {
            if (changedIds.Count() == 0)
                return;

            var changedApps = config.SteamSettings.Where(x => changedIds.Contains(x.Key)).ToList();

            if (changedApps.Count() == 0)
                return;

            Dictionary<ulong, StringBuilder> messages = new Dictionary<ulong, StringBuilder>();

            string buffer = new string('0', changedApps.Select(x => x.Key).OrderByDescending(x => x.ToString().Length).FirstOrDefault().ToString().Length);

            foreach (var app in changedApps)
            {
                foreach (var channel in app.Value)
                {
                    if (!messages.ContainsKey(channel))
                    {
                        messages[channel] = new StringBuilder();
                        messages[channel].AppendLine($"Detected the following updated chagelists:");
                    }

                    messages[channel].AppendLine($"`[{app.Key.ToString(buffer)}]` {await config.GetGameName(app.Key, steamApps)}");
                }
            }

            foreach (var channel in messages)
            {
                channel.Value.AppendLine("Note: These are just changelists and *not* guaranteed to be an actual update.");

                var socketChannel = client.GetChannel(channel.Key) as SocketTextChannel;

                // Todo: if a channel is missing for too long, remove it from the config
                if (socketChannel == null)
                    continue;

                await socketChannel.SendMessageAsync(channel.Value.ToString());
            }
        }

        public async Task<SteamApps.PICSProductInfoCallback.PICSProductInfo> GetInfo(uint appId)
        {
            var result = await steamApps.PICSGetProductInfo(appId, package: null, onlyPublic: false);
            
            return result.Results.First().Apps.Values.FirstOrDefault();
        }

        public async Task<IEnumerable<SteamApp>> AddSteamGames(IEnumerable<uint> appIds, ulong channel)
        {
            List<SteamApp> output = new List<SteamApp>();
            List<uint> added = new List<uint>();

            foreach (var a in appIds)
            {
                if (config.AddGame(a, channel))
                    added.Add(a);
            }
            
            foreach (var app in added)
            {
                output.Add(new SteamApp()
                {
                    AppId = app,
                    GameName = await config.GetGameName(app, steamApps)
                });
            }
            
            return output;
        }

        public async Task<IEnumerable<SteamApp>> RemoveSteamGames(IEnumerable<uint> appIds, ulong channel)
        {
            List<SteamApp> output = new List<SteamApp>();
            List<uint> removed = new List<uint>();

            foreach (var r in appIds)
            {
                if (config.RemoveGame(r, channel))
                    removed.Add(r);
            }
            
            foreach (var app in removed)
            {
                output.Add(new SteamApp()
                {
                    AppId = app,
                    GameName = (await config.GetGameName(app, steamApps)) ?? "[Unknown]"
                });
            }

            return output;
        }

        public async Task<IEnumerable<SteamApp>> ListSteamGames(ulong channel)
        {
            List<SteamApp> output = new List<SteamApp>();

            var temp = config.SteamSettings.Where(x => x.Value.Contains(channel));

            foreach (var app in temp)
            {
                output.Add(new SteamApp()
                {
                    AppId = app.Key,
                    GameName = await config.GetGameName(app.Key, steamApps) ?? "[Unknown]"
                });
            }

            return output;
        }
    }

    public class SteamApp
    {
        public uint AppId;
        public string GameName;
        public uint GameVersion;
    }
}
