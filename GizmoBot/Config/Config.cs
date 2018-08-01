using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using SteamKit2;
using RestSharp;
using GizmoBot.Modules.Steam;

namespace GizmoBot
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Config
    {
        [JsonIgnore]
        private SteamGameList steamGameList;

        [JsonProperty("token")]
        public string Token { get; set; }
        [JsonProperty("steam_token")]
        public string SteamToken { get; set; }
        [JsonProperty("prefixes")]
        public IEnumerable<string> PrefixList { get; set; } = new[]
        {
            "*"
        };
        [JsonProperty("mention_trigger")]
        public bool TriggerOnMention { get; set; } = true;

        [JsonProperty("success_response")]
        public string SuccessResponse { get; set; } = ":thumbsup:";

        [JsonProperty("owner_id")]
        public ulong OwnerId { get; set; } = 0;

        [JsonProperty("steam_username")]
        public string SteamUsername { get; set; } = "";
        [JsonProperty("steam_password")]
        public string SteamPassword { get; set; } = "";

        [JsonProperty("change_number")]
        public uint ChangeNumber = 0;

        [JsonProperty("steam_settings")]
        public Dictionary<uint, SteamAppDetails> SteamSettings { get; set; } = new Dictionary<uint, SteamAppDetails>();

        //public bool RemoveGame(uint app, ulong channel)
        //{
        //    if (!SteamSettings.ContainsKey(app))
        //        return false;

        //    if (!SteamSettings[app].Contains(channel))
        //        return false;

        //    SteamSettings[app].Remove(channel);

        //    if (SteamSettings[app].Count() == 0)
        //        SteamSettings.Remove(app);

        //    return true;
        //}

        public async Task<string> GetGameName(uint app)
        {
            if (steamGameList.LatestAppId < app)
                await LoadGameNames(forceReload: true);

            return steamGameList.applist.apps.FirstOrDefault(x => x.AppId == app)?.Name ?? null;
        }

        //public async Task<Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo>> GetInfo(IEnumerable<uint> appIds, SteamApps steamApps)
        //{
        //    int failureCount = 0;

        //    List<uint> requests = new List<uint>(appIds);

        //    while (failureCount < 3)
        //    {
        //        if (failureCount > 0)
        //            await Task.Delay(1000);

        //        try
        //        {
        //            var results = await steamApps.PICSGetProductInfo(requests, packages: new uint[0]);

        //            var apps = results.Results.FirstOrDefault()?.Apps;

        //            foreach (var a in apps)
        //            {
        //                requests.Remove(a.Key);

        //                //string name = a.Value?.KeyValues?
        //                //    .Children?.FirstOrDefault(x => x.Name == "common")?
        //                //    .Children?.FirstOrDefault(x => x.Name == "name")?.Value;

        //                if (!GameVersions.ContainsKey(a.Value.ID))
        //                {
        //                    string temp = a.Value?.KeyValues?
        //                        .Children?.FirstOrDefault(x => x.Name == "depots")?
        //                        .Children?.FirstOrDefault(x => x.Name == "branches")?
        //                        .Children?.FirstOrDefault(x => x.Name == "public")?
        //                        .Children?.FirstOrDefault(x => x.Name == "buildid")?.Value;

        //                    if (uint.TryParse(temp, out uint version))
        //                    {
        //                        GameVersions[a.Value.ID] = version;
        //                    }
        //                }
        //            }

        //            if (requests.Count() == 0)
        //            {
        //                // We got everything we wanted
        //                break;
        //            }
        //            else
        //            {
        //                // We missed some stuff, try again a couple of times until we get it
        //                failureCount++;
        //            }
        //        }
        //        catch
        //        {
        //            // We timed out before getting anything.
        //            failureCount++;
        //            continue;
        //        }
        //    }

        //    return null;
        //}

        public async static Task<Config> Load()
        {
            if (File.Exists("config.json"))
            {
                var json = await File.ReadAllTextAsync("config.json");
                var temp =  JsonConvert.DeserializeObject<Config>(json);
                await temp.LoadGameNames();
                return temp;
            }
            var config = new Config();
            await config.Save();
            throw new InvalidOperationException("configuration file created; insert token and restart.");
        }

        public async Task Save()
        {
            //var json = JsonConvert.SerializeObject(this);
            //File.WriteAllText("config.json", json);
            await JsonStorage.SerializeObjectToFile(this, "config.json");
        }

        private async Task LoadGameNames(bool forceReload = false)
        {
            if (!forceReload && File.Exists("games.json"))
            {
                var json = await File.ReadAllTextAsync("games.json");
                steamGameList = JsonConvert.DeserializeObject<SteamGameList>(json);
            }
            else
            {
                RestClient client = new RestClient("https://api.steampowered.com");
                RestRequest request = new RestRequest("ISteamApps/GetAppList/v2", Method.GET);

                client.ExecuteAsync<SteamGameList>(request, response =>
                {
                    steamGameList = response.Data;
                });

                await SaveGameNames();
            }

            steamGameList.LatestAppId = steamGameList.applist.apps.OrderByDescending(x => x.AppId).FirstOrDefault().AppId;
        }

        private async Task SaveGameNames()
        {
            await JsonStorage.SerializeObjectToFile(steamGameList, "games.json");
        }
    }

    public class App
    {
        [JsonProperty("appid")]
        public uint AppId { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
    }

    public class Applist
    {
        public List<App> apps { get; set; }
    }

    public class SteamGameList
    {
        public Applist applist { get; set; }

        [JsonIgnore]
        public uint LatestAppId { get; set; }
    }
}
