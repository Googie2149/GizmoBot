using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Rest;
using Discord.Commands;
using System.IO;
using Microsoft.Extensions.DependencyInjection;

namespace GizmoBot
{
    class Program
    {
        static void Main(string[] args) =>
            new Program().RunAsync().GetAwaiter().GetResult();

        private DiscordSocketClient socketClient;
        private DiscordRestClient restClient;
        private Config config;
        private CommandHandler handler;
        private ServiceProvider map;
        private ulong updateChannel = 0;

        private async Task RunAsync()
        {
            socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Verbose,
                AlwaysDownloadUsers = true
            });
            socketClient.Log += Log;

            restClient = new DiscordRestClient(new DiscordRestConfig
            {
                LogLevel = LogSeverity.Verbose
            });
            restClient.Log += Log;

            if (File.Exists("./update"))
            {
                var temp = File.ReadAllText("./update");
                ulong.TryParse(temp, out updateChannel);
                File.Delete("./update");
                Console.WriteLine($"Found an update file! It contained [{temp}] and we got [{updateChannel}] from it!");
            }

            config = Config.Load();

            map = new ServiceCollection().AddSingleton(socketClient).AddSingleton(config).BuildServiceProvider();
            

            await socketClient.LoginAsync(TokenType.Bot, config.Token);
            await socketClient.StartAsync();

            await restClient.LoginAsync(TokenType.Bot, config.Token);

            if (File.Exists("./deadlock"))
            {
                Console.WriteLine("We're recovering from a deadlock.");
                File.Delete("./deadlock");
                (await restClient.GetUserAsync(config.OwnerId))?.SendMessageAsync($"I recovered from a deadlock.\n`{DateTime.Now.ToShortDateString()}` `{DateTime.Now.ToLongTimeString()}`");
            }

            socketClient.GuildAvailable += Client_GuildAvailable;

            handler = new CommandHandler();
            await handler.Install(map);

            await Task.Delay(-1);
        }
                
        private async Task Client_GuildAvailable(SocketGuild guild)
        {
            if (updateChannel != 0 && guild.GetTextChannel(updateChannel) != null)
            {
                await Task.Delay(3000); // wait 3 seconds just to ensure we can actually send it. this might not do anything.
                await guild.GetTextChannel(updateChannel).SendMessageAsync("Successfully reconnected.");
            }
        }

        private async Task SocketClient_Disconnected(Exception ex)
        {
            // If we disconnect, wait 3 minutes and see if we regained the connection.
            // If we did, great, exit out and continue. If not, check again 3 minutes later
            // just to be safe, and restart to exit a deadlock.
            var task = Task.Run(async () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    await Task.Delay(1000 * 60 * 3);

                    if (socketClient.ConnectionState == ConnectionState.Connected)
                        break;
                    else if (i == 1)
                    {
                        File.Create("./deadlock");
                        Environment.Exit((int)ExitCodes.ExitCode.DeadlockEscape);
                    }
                }
            });
        }

        private Task Log(LogMessage msg)
        {
            //Console.WriteLine(msg.ToString());

            //Color
            ConsoleColor color;
            switch (msg.Severity)
            {
                case LogSeverity.Error: color = ConsoleColor.Red; break;
                case LogSeverity.Warning: color = ConsoleColor.Yellow; break;
                case LogSeverity.Info: color = ConsoleColor.White; break;
                case LogSeverity.Verbose: color = ConsoleColor.Gray; break;
                case LogSeverity.Debug: default: color = ConsoleColor.DarkGray; break;
            }

            //Exception
            string exMessage;
            Exception ex = msg.Exception;
            if (ex != null)
            {
                while (ex is AggregateException && ex.InnerException != null)
                    ex = ex.InnerException;
                exMessage = $"{ex.Message}";
                if (exMessage != "Reconnect failed: HTTP/1.1 503 Service Unavailable")
                    exMessage += $"\n{ex.StackTrace}";
            }
            else
                exMessage = null;

            //Source
            string sourceName = msg.Source?.ToString();

            //Text
            string text;
            if (msg.Message == null)
            {
                text = exMessage ?? "";
                exMessage = null;
            }
            else
                text = msg.Message;

            if (sourceName == "Command")
                color = ConsoleColor.Cyan;
            else if (sourceName == "<<Message")
                color = ConsoleColor.Green;
            else if (sourceName == ">>Message")
                return Task.CompletedTask;

            //Build message
            StringBuilder builder = new StringBuilder(text.Length + (sourceName?.Length ?? 0) + (exMessage?.Length ?? 0) + 5);
            if (sourceName != null)
            {
                builder.Append('[');
                builder.Append(sourceName);
                builder.Append("] ");
            }
            builder.Append($"[{DateTime.Now.ToString("d")} {DateTime.Now.ToString("T")}] ");
            for (int i = 0; i < text.Length; i++)
            {
                //Strip control chars
                char c = text[i];
                if (c == '\n' || !char.IsControl(c) || c != (char)8226)
                    builder.Append(c);
            }
            if (exMessage != null)
            {
                builder.Append(": ");
                builder.Append(exMessage);
            }

            text = builder.ToString();
            //if (msg.Severity <= LogSeverity.Info)
            //{
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            //}
#if DEBUG
            System.Diagnostics.Debug.WriteLine(text);
#endif



            return Task.CompletedTask;
        }
    }
}
