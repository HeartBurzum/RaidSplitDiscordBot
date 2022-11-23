using Newtonsoft.Json;
using RaidRobot.Data.Entities;
using RaidRobot.Infrastructure;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace RaidRobot.Data
{
    public class SplitDataStore : ISplitDataStore
    {
        private object lockObj = new object();
        private SplitData splitData;
        string fileName;
        private readonly IRaidSplitConfiguration config;

        public ConcurrentDictionary<string, GuildMember> Roster => splitData.Roster;
        public ConcurrentDictionary<Guid, RaidEvent> Events => splitData.Events;
        public ConcurrentDictionary<ulong, UnknownMessage> UnknownMessages => splitData.UnknownMessages;
        public ConcurrentDictionary<string, PreSplit> PreSplits => splitData.PreSplits;

        public SplitDataStore(IRaidSplitConfiguration config)
        {
            this.config = config;
            fileName = getFileName();
            splitData = loadFromDataStore();
        }

        private string getFileName()
        {
            string path;

            if (config.Settings.DataFileDirectoryPath == "" || config.Settings.DataFileDirectoryPath.StartsWith("__")) {
                path = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                path = Path.Combine(path, "DataFileDirectory");
            } else {
                path = config.Settings.DataFileDirectoryPath;
            }

            if(!Directory.Exists(path))
                Directory.CreateDirectory(path);

            string fileName = Path.Combine(path, Constants.DATA_FILE_NAME);
            return fileName;
        }

        private SplitData loadFromDataStore()
        {
            var data = new SplitData();
            if (File.Exists(fileName))
            {
                string jsonBlob = File.ReadAllText(fileName);
                data = JsonConvert.DeserializeObject<SplitData>(jsonBlob);
            }

            return data;
        }

        public string UpdateRoster(List<GuildMember> roster)
        {
            int addedCount = 0;
            int updatedCount = 0;

            StringBuilder added = new StringBuilder();
            StringBuilder updated = new StringBuilder();

            foreach (var member in roster)
            {
                lock (lockObj)
                {
                    splitData.Roster.TryGetValue(member.CharacterName, out var foundMember);
                    if (foundMember == null)
                    {
                        splitData.Roster[member.CharacterName] = member;
                        addedCount++;
                        added.Append($", {member.CharacterName}");
                    }
                    else
                    {
                        if (foundMember.Rank != member.Rank || foundMember.Level != member.Level || foundMember.Comment != member.Comment)
                        {
                            foundMember.Rank = member.Rank;
                            foundMember.Level = member.Level;
                            foundMember.Comment = member.Comment;
                            updatedCount++;
                            updated.Append($", {member.CharacterName}");
                        }
                    }
                }
            }

            SaveChanges();

            return $"**Added ({addedCount})**{Environment.NewLine}**Updated ({updatedCount})**{Environment.NewLine}";
        }

        public void SaveChanges()
        {
            var jsonBlob = JsonConvert.SerializeObject(splitData);
            lock (lockObj)
            {
                File.WriteAllText(fileName, jsonBlob);
            }
        }

    }
}
