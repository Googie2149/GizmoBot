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
        private SteamService steam;
        private IServiceProvider services;

        public Steam(Config _config, SteamService _steam, IServiceProvider _services)
        {
            config = _config;
            steam = _steam;
            services = _services;
        }

        [Command("add", RunMode = RunMode.Async)]
        public async Task AddApps(params uint[] appIds)
        {
            var msg = await ReplyAsync("Loading...");
            
            var added = await steam.AddSteamGames(appIds, Context.Channel.Id);

            if (added.Count() == 0)
            {
                await msg.ModifyAsync(x => x.Content = "Those games are already being watched.");
                return;
            }

            StringBuilder output = new StringBuilder();
            string buffer = new string('0', added.OrderByDescending(x => x.AppId.ToString().Length).FirstOrDefault().AppId.ToString().Length);
            var errors = added.Where(x => x.GameName == null).Select(x => x.AppId).ToList();

            output.AppendLine("Added the following:");
            output.AppendLine(added.Where(x => !errors.Contains(x.AppId)).Select(y => $"`[{y.AppId.ToString(buffer)}]` {y.GameName}").Join("\n"));


            if (errors.Count() > 0)
            {
                errors.ForEach(x => config.RemoveGame(x, Context.Channel.Id));

                output.AppendLine("The following could not be added at this time:");
                output.AppendLine(errors.Select(x => $"`[{x.ToString(buffer)}]`").Join(" "));
                output.Append("Check that the App Ids are correct and try again later.");
            }
            
            if (errors.Count() != added.Count())
                await config.Save();

            await msg.ModifyAsync(x => x.Content = output.ToString());
        }

        [Command("remove", RunMode = RunMode.Async)]
        public async Task RemoveApps(params uint[] appIds)
        {
            var msg = await ReplyAsync("Loading...");

            var removed = await steam.RemoveSteamGames(appIds, Context.Channel.Id);
            
            if (removed.Count() == 0)
            {
                await msg.ModifyAsync(x => x.Content = "None of those games were not being watched in this channel.");
                return;
            }

            string buffer = new string('0', removed.OrderByDescending(x => x.AppId.ToString().Length).FirstOrDefault().AppId.ToString().Length);
            await msg.ModifyAsync(x => x.Content = $"Removed the following:\n{(removed.Select(y => $"`[{y.AppId.ToString(buffer)}]` {y.GameName}").Join("\n"))}");
        }

        [Command("list", RunMode = RunMode.Async)]
        public async Task ListApps()
        {
            var msg = await ReplyAsync("Loading...");

            var list = await steam.ListSteamGames(Context.Channel.Id);

            if (list.Count() == 0)
                await msg.ModifyAsync(x => x.Content = "This channel isn't watching any games at this time.");

            string buffer = new string('0', list.OrderByDescending(x => x.AppId.ToString().Length).FirstOrDefault().AppId.ToString().Length);

            await msg.ModifyAsync(x => x.Content = $"This channel is watching the following:\n{(list.Select(y => $"`[{y.AppId.ToString(buffer)}]` {y.GameName}").Join("\n"))}");
        }

        [Command("info", RunMode = RunMode.Async)]
        public async Task GetInfo(uint appId)
        {
            var result = await steam.GetInfo(appId);

            StringBuilder output = new StringBuilder();

            output.AppendLine(result.ID.ToString());

            foreach (var a in result.KeyValues.Children)
            {
                output.AppendLine($" {a.Name}\n -{a.Value}");
                foreach (var b in a.Children)
                {
                    output.AppendLine($"  {b.Name}\n  -{b.Value}");
                    foreach (var c in b.Children)
                    {
                        output.AppendLine($"   {c.Name}\n   -{c.Value}");
                        foreach (var d in c.Children)
                        {
                            output.AppendLine($"    {d.Name}\n    -{d.Value}");
                            foreach (var e in d.Children)
                            {
                                output.AppendLine($"     {e.Name}\n     -{e.Value}");
                            }
                        }
                    }
                }
            }

            await RespondAsync(output.ToString());
        }
    }
}
