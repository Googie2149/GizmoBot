using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

using SteamKit2;

namespace GizmoBot
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Config
    {
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
        public Dictionary<uint, List<ulong>> SteamSettings { get; set; } = new Dictionary<uint, List<ulong>>();

        [JsonProperty("game_names")]
        private Dictionary<uint, string> GameNames { get; set; } = new Dictionary<uint, string>();

        [JsonProperty("game_versions")]
        public Dictionary<uint, uint> GameVersions { get; set; } = new Dictionary<uint, uint>();

        public bool AddGame(uint app, ulong channel)
        {
            if (!SteamSettings.ContainsKey(app))
                SteamSettings[app] = new List<ulong>();

            if (SteamSettings[app].Contains(channel))
                return false;

            SteamSettings[app].Add(channel);

            return true;
        }

        public bool RemoveGame(uint app, ulong channel)
        {
            if (!SteamSettings.ContainsKey(app))
                return false;

            if (!SteamSettings[app].Contains(channel))
                return false;

            SteamSettings[app].Remove(channel);

            if (SteamSettings[app].Count() == 0)
                SteamSettings.Remove(app);

            return true;
        }

        public async Task<string> GetGameName(uint app, SteamApps steamApps)
        {
            if (!SteamSettings.ContainsKey(app))
                SteamSettings[app] = new List<ulong>();

            if (GameNames.TryGetValue(app, out string output))
            {
                return output;
            }
            else
            {
                await BatchUpdateGameNames(steamApps);

                GameNames.TryGetValue(app, out string output2);

                return output2;
            }
        }

        public async Task<Dictionary<uint, SteamApps.PICSProductInfoCallback.PICSProductInfo>> GetInfo(IEnumerable<uint> appIds, SteamApps steamApps)
        {
            int failureCount = 0;

            List<uint> requests = new List<uint>(appIds);

            while (failureCount < 3)
            {
                if (failureCount > 0)
                    await Task.Delay(1000);

                try
                {
                    var results = await steamApps.PICSGetProductInfo(appIds, packages: new uint[0]);

                    //if (results.Failed)



                    var apps = results.Results.FirstOrDefault()?.Apps;



                    foreach (var a in apps)
                    {
                        string name = a.Value?.KeyValues?
                            .Children?.FirstOrDefault(x => x.Name == "common")?
                            .Children?.FirstOrDefault(x => x.Name == "name")?.Value;

                        if (name != null)
                            GameNames.Add(a.Value.ID, name);
                        else
                            continue;

                        if (!GameVersions.ContainsKey(a.Value.ID))
                        {
                            string temp = a.Value?.KeyValues?
                                .Children?.FirstOrDefault(x => x.Name == "depots")?
                                .Children?.FirstOrDefault(x => x.Name == "branches")?
                                .Children?.FirstOrDefault(x => x.Name == "public")?
                                .Children?.FirstOrDefault(x => x.Name == "buildid")?.Value;

                            if (uint.TryParse(temp, out uint version))
                            {
                                GameVersions[a.Value.ID] = version;
                            }
                        }
                    }

                    if (SteamSettings.Keys.Where(x => GameNames.ContainsKey(x)).Count() == 0)
                    {
                        // We got everything we wanted
                        break;
                    }
                    else
                    {
                        // We missed some stuff, try again a couple of times until we get it
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

            return null;
        }

        public async Task BatchUpdateGameNames(SteamApps steamApps)
        {
            int failureCount = 0;

            while (failureCount < 3)
            {
                if (failureCount > 0)
                    await Task.Delay(1000);

                var requests = SteamSettings.Keys.Where(x => !GameNames.ContainsKey(x)).ToList();

                if (requests.Count() == 0)
                    return;

                try
                {
                    var results = await steamApps.PICSGetProductInfo(requests, packages: new uint[0]);
                    
                    var apps = results.Results.FirstOrDefault()?.Apps;
                    
                    foreach (var a in apps)
                    {
                        string name = a.Value?.KeyValues?
                            .Children?.FirstOrDefault(x => x.Name == "common")?
                            .Children?.FirstOrDefault(x => x.Name == "name")?.Value;

                        if (name != null)
                            GameNames.Add(a.Value.ID, name);
                        else
                            continue;

                        if (!GameVersions.ContainsKey(a.Value.ID))
                        {
                            string temp = a.Value?.KeyValues?
                                .Children?.FirstOrDefault(x => x.Name == "depots")?
                                .Children?.FirstOrDefault(x => x.Name == "branches")?
                                .Children?.FirstOrDefault(x => x.Name == "public")?
                                .Children?.FirstOrDefault(x => x.Name == "buildid")?.Value;

                            if (uint.TryParse(temp, out uint version))
                            {
                                GameVersions[a.Value.ID] = version;
                            }
                        }
                    }

                    if (SteamSettings.Keys.Where(x => GameNames.ContainsKey(x)).Count() == 0)
                    {
                        // We got everything we wanted
                        break;
                    }
                    else
                    {
                        // We missed some stuff, try again a couple of times until we get it
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
        }
        
        public async static Task<Config> Load()
        {
            if (File.Exists("config.json"))
            {
                var json = File.ReadAllText("config.json");
                return JsonConvert.DeserializeObject<Config>(json);
            }
            var config = new Config();
            await config.Save();
            throw new InvalidOperationException("configuration file created; insert token and restart.");
        }

        public async Task Save()
        {
            //var json = JsonConvert.SerializeObject(this);
            //File.WriteAllText("config.json", json);
            JsonStorage.SerializeObjectToFile(this, "config.json").Wait();
        }
    }
}
