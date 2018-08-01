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
            
            while (true)
            {
                await Task.Delay(1000 * 60 * 2); // wait 2 minutes

                try
                {
                    // Make a copy of the list so we don't get interrupted
                    var tempSettings = new Dictionary<uint, SteamAppDetails>(config.SteamSettings);

                    // Fetch the most recent changelists
                    var changes = await steamApps.PICSGetChangesSince(config.ChangeNumber);
                    
                    // If we got no results, it means our last change number was too old
                    // Set the returned most recent number and try again later
                    if (config.ChangeNumber == 0 || changes.AppChanges.Count() == 0)
                    {
                        Console.WriteLine($"Empty changelist! {config.ChangeNumber}");
                        config.ChangeNumber = changes.CurrentChangeNumber;
                        continue;
                    }

                    // No new changes
                    if (config.ChangeNumber == changes.CurrentChangeNumber)
                    {
                        Console.WriteLine("No new changes");
                        continue;
                    }

                    // We got a new changenumber, save it
                    config.ChangeNumber = changes.CurrentChangeNumber;

                    // If this passes, it means there were no actual changes despite the number incrementing
                    if (changes.AppChanges.Count() == 0)
                    {
                        Console.WriteLine("No new app changes");
                        continue;
                    }

                    // Check if any of the changed apps are being watched, and store their Ids in a list
                    var watchedApps = tempSettings.Where(x => changes.AppChanges.Keys.Contains(x.Key)).Select(x => x.Key).ToList();

                    Dictionary<uint, uint> updatedApps = await GetVersions(watchedApps);
                    
                    // Send out a broadcast to all channels watching those apps
                    foreach (var kv in updatedApps)
                    {
                        // We've already caught this update once before, ignore it
                        if (kv.Value <= tempSettings[kv.Key].GameVersion)
                            continue;

                        string name = await config.GetGameName(kv.Key);
                        foreach (var ch in tempSettings[kv.Key].Channels)
                        {
                            // Implement error handling/deleted channel detection here
                            await (client.GetChannel(ch) as ITextChannel).SendMessageAsync($"New update detected for {name}!\n`{tempSettings[kv.Key].GameVersion}` -> `{kv.Value}`");
                        }

                        // Lastly, update the cached version number
                        config.SteamSettings[kv.Key].GameVersion = kv.Value;
                    }
                    
                    // We made it this far, at the very least the change number updated
                    await config.Save();
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
        
        public async Task<Dictionary<uint, uint>> GetVersions(List<uint> appIds)
        {
            int failureCount = 0;
            Dictionary<uint, uint> temp = new Dictionary<uint, uint>();

            while (failureCount < 3)
            {
                // If we've failed at least once, pause for a second
                if (failureCount > 0)
                    await Task.Delay(1000);

                try
                {
                    // Request information for watched apps that haven't already been checked in this pass
                    var results = await steamApps.PICSGetProductInfo(appIds, packages: new uint[0], onlyPublic: false);

                    var apps = results.Results.FirstOrDefault()?.Apps;

                    foreach (var a in apps)
                    {
                        // Remove it from the list of apps to request, since we successfully got its info
                        appIds.Remove(a.Key);

                        // Check for the Build ID
                        // Note: I have no idea if all games follow this structure, or if it's public for all games
                        string tempVersion = a.Value?.KeyValues?
                        .Children?.FirstOrDefault(x => x.Name == "depots")?
                        .Children?.FirstOrDefault(x => x.Name == "branches")?
                        .Children?.FirstOrDefault(x => x.Name == "public")?
                        .Children?.FirstOrDefault(x => x.Name == "buildid")?.Value;

                        // Make sure what we got is actually a number
                        if (uint.TryParse(tempVersion, out uint version))
                        {
                            temp.Add(a.Key, version);
                        }
                    }

                    if (appIds.Count() == 0)
                    {
                        // We got everything we wanted
                        break;
                    }
                    else
                    {
                        // We missed something, try again a couple of times
                        failureCount++;
                    }
                }
                catch
                {
                    // We timed out before getting anything.
                    failureCount++;
                    continue;
                }
            }

            return temp;
        }
    }

    public class SteamAppDetails
    {
        public SteamAppDetails()
        {
            Channels = new List<ulong>();
        }

        public uint GameVersion;
        public List<ulong> Channels;
    }
}
