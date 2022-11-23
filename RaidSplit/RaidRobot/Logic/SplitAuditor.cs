using Discord.WebSocket;
using RaidRobot.Data.Entities;
using RaidRobot.Infrastructure;
using RaidRobot.Logic.Interfaces;
using RaidRobot.Messaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace RaidRobot.Logic
{
    public class SplitAuditor : ISplitAuditor
    {
        private readonly DiscordSocketClient client;
        private readonly IRaidSplitConfiguration config;

        public SplitAuditor(DiscordSocketClient client,
            IRaidSplitConfiguration config)
        {
            this.client = client;
            this.config = config;
        }

        public async Task GenerateAuditFile(RaidEvent raidEvent, Split split)
        {
            var fileName = getFileName();
            var sb = new StringBuilder();
            var audits = split.Attendees.Values.Select(x => x.SplitReason).OrderBy(x => x);
            foreach (var audit in audits)
            {
                sb.AppendLine(audit);
            }
            File.WriteAllText(fileName, sb.ToString());

            var guild = client.GetGuild(raidEvent.GuildID);
            var channel = guild.TextChannels.FirstOrDefault(x => x.Name == config.Settings.AdminChannel);
            if (channel == null)
                throw new ArgumentException($"Couldn't find admin Channel {config.Settings.AdminChannel}");


            var message = await channel.SendFileAsync(fileName, $"Audit for Split {split.SplitNumber}");
            split.Messages[MessageContexts.Audit] = message.ConvertToMessageDetail();
        }


        private string getFileName()
        {
            string path;

            if (config.Settings.TempFileDirectoryPath == "" || config.Settings.TempFileDirectoryPath.StartsWith("__")) {
                path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                path = Path.Combine(path, "TempFileDirectory");
            } else {
                path = config.Settings.TempFileDirectoryPath;
            }

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            string fileName = Path.Combine(path, $"{Guid.NewGuid()}.txt");
            return fileName;
        }
    }
}
