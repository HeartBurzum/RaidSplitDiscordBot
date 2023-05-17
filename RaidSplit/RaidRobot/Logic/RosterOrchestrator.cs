using Discord;
using RaidRobot.Data.Entities;
using RaidRobot.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;
using RaidRobot.Infrastructure;
using System.Diagnostics.Metrics;
using RaidRobot.Messaging;
using System.Reflection.Metadata;

namespace RaidRobot.Logic
{
    public class RosterOrchestrator : IRosterOrchestrator
    {
        private readonly DiscordSocketClient client;
        private readonly ISplitDataStore splitDataStore;
        private readonly IRaidSplitConfiguration config;
        private readonly ITextCommunicator textCommunicator;

        public RosterOrchestrator(
            DiscordSocketClient client,
            ISplitDataStore splitDataStore,
            IRaidSplitConfiguration config,
            ITextCommunicator textCommunicator)
        {
            this.client = client;
            this.splitDataStore = splitDataStore;
            this.config = config;
            this.textCommunicator = textCommunicator;
        }

        public GuildMember FindMember(ulong userID, CharacterType characterType)
        {
            var member = splitDataStore.Roster.Values.FirstOrDefault(x => x.UserId == userID && string.Equals(x.CharacterType, characterType.Name, StringComparison.OrdinalIgnoreCase));
            return member;
        }

        public async Task<GuildMember> MapUser(ulong guildID, ulong userID, string characterName, string characterTypeName)
        {
            var characterType = config.RaidTypes.SelectMany(x => x.CharacterTypes)
                .FirstOrDefault(x => string.Equals(x.Name, characterTypeName, StringComparison.OrdinalIgnoreCase));

            if (characterType == null)
            {
                await textCommunicator.SendMessageByChannelName(guildID, config.Settings.SpamChannel,
                    $"Character Type {characterTypeName} is not valid.  Cannot map character.");
                return null;
            }

            return await MapUser(guildID, userID, characterName, characterType);
        }

        public async Task<GuildMember> MapUser(UnknownMessage message, string characterName)
        {
            if(characterName.Split().Length != 1)
            {
                await textCommunicator.SendMessageByChannelName(message.GuildID, config.Settings.SpamChannel,
                    $"Character Names can only be a single word. Reply to the original message with just the character name.  Bad Input: {characterName}");
                return null;
            }
            characterName = NormalizeName(characterName);
            var characterType = config.RaidTypes.SelectMany(x => x.CharacterTypes)
                .FirstOrDefault(x => string.Equals(x.Name, message.CharacterType, StringComparison.OrdinalIgnoreCase));

            if(characterType == null)
            {
                await textCommunicator.SendMessageByChannelName(message.GuildID, config.Settings.SpamChannel,
                    $"Character Type {message.CharacterType} is not valid.  Cannot map character.");
                return null;
            }

            return await MapUser(message.GuildID, message.UserID, characterName, characterType);
        }

        public async Task<GuildMember> MapUser(ulong guildID, ulong userID, string characterName, CharacterType characterType)
        {
            var anotherCharacter = FindMember(userID, characterType);
            var matchingCharacter = splitDataStore.Roster.Values.FirstOrDefault(x => string.Equals(x.CharacterName, characterName, StringComparison.OrdinalIgnoreCase));

            if (matchingCharacter == null)
            {
                matchingCharacter = new GuildMember()
                {
                    CharacterName = characterName,
                };
                splitDataStore.Roster.TryAdd(characterName, matchingCharacter);
            }

            matchingCharacter.UserId = userID;
            matchingCharacter.CharacterType = characterType.Name;

            var mention = MentionUtils.MentionUser(userID);

            string deregister = string.Empty;
            if (anotherCharacter != null && anotherCharacter.CharacterName != matchingCharacter.CharacterName)
            {
                anotherCharacter.UserId = null;
                anotherCharacter.CharacterType = null;
                deregister = $" {anotherCharacter.CharacterName} has been removed as {mention} 's {characterType.Name}.";
            }

            splitDataStore.SaveChanges();
            string content = $"{characterName} has been Registered to {mention} as a {characterType.Name}.{deregister} Thank You!";

            await textCommunicator.SendMessageByChannelName(guildID, config.Settings.SpamChannel, content);
            return matchingCharacter;
        }

        public  async Task UpdateClass(ulong guildID, string characterName, string className)
        {
            if (className.Split().Length != 1 && className != "Shadow Knight")
            {
                await textCommunicator.SendMessageByChannelName(guildID, config.Settings.SpamChannel,
                    $"Class Names can only be a single word. Reply to the original message with just the class name.  Bad Input: {className}");
                return;
            }

            var gameClass = config.Classes.FirstOrDefault(x => string.Equals(x.Name, className, StringComparison.OrdinalIgnoreCase));
            if (gameClass == null)
            {
                await textCommunicator.SendMessageByChannelName(guildID, config.Settings.SpamChannel,
                    $"Class {className} is not valid.  Cannot map character. Acceptable Values: {string.Join(", ", config.Classes.Select(x => x.Name))}");
                return;
            }

            var member = splitDataStore.Roster.Values.FirstOrDefault(x => string.Equals(x.CharacterName, characterName, StringComparison.OrdinalIgnoreCase));
            if (member == null)
            {
                await textCommunicator.SendMessageByChannelName(guildID, config.Settings.SpamChannel,
                    $"I can't find a character named {characterName} in the roster, unable to set class.");
                return;
            }

            await textCommunicator.SendMessageByChannelName(guildID, config.Settings.SpamChannel,
                $"Updated {characterName}'s class to {gameClass.Name} {gameClass.EmojiCode}");

            member.ClassName = gameClass.Name;
            splitDataStore.SaveChanges();

        }

        public async Task UpdateClass(UnknownMessage message, string className)
        {
            await UpdateClass(message.GuildID, message.CharacterName, className);
        }


        public async Task<GuildMember> AutoMapCharacter(RaidEvent raidEvent, ulong userID, CharacterType characterType)
        {
            var Guild = client.GetGuild(raidEvent.GuildID);
            var guildUser = Guild?.GetUser(userID);
            var userName = guildUser?.Nickname ?? guildUser?.Username;

            if (string.IsNullOrEmpty(userName))
                return null; //Can't map anything if we don't know the username...

            var match = splitDataStore.Roster.Values
                .FirstOrDefault(x => !x.UserId.HasValue
                    && string.Equals(x.CharacterName, userName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return await MapUser(raidEvent.GuildID, userID, match.CharacterName, characterType);
            }

            //See if their username is the comment of another character in the guild (boxes)...
            match = splitDataStore.Roster.Values
                .FirstOrDefault(x => !x.UserId.HasValue
                    && string.Equals(x.Rank, characterType.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Comment, userName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return await MapUser(raidEvent.GuildID, userID, match.CharacterName, characterType);
            }

            return null;
        }

        public async Task<GuildMember> ValidateUser(RaidEvent raidEvent, ulong userID, CharacterType characterType)
        {
            var guild = client.GetGuild(raidEvent.GuildID);
            var user = guild.GetUser(userID);

            var member = FindMember(userID, characterType);
            if (member == null)
            {
                member = await AutoMapCharacter(raidEvent, userID, characterType);
            }

            var mention = MentionUtils.MentionUser(userID);
            if (member == null)
            {
                var content = $"I don't know {mention} 's {characterType.Name}. Please reply to this message with their character name." +
                    $" You must do this before raid time to be included in a split. You should only have to do this once.";
                var messageResult = await textCommunicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SpamChannel, content);

                var unknownMessage = new UnknownMessage()
                {
                    GuildID = raidEvent.GuildID,
                    ChannelID = messageResult.Channel.Id,
                    MessageID = messageResult.Id,
                    CharacterType = characterType.Name,
                    MessageType = UnknownMessageTypes.Character,
                    UserID = userID,
                    UserName = user?.Nickname ?? user?.Username,
                };
                splitDataStore.UnknownMessages.TryAdd(unknownMessage.MessageID, unknownMessage);
                splitDataStore.SaveChanges();
                return null;
            }

            if (string.IsNullOrEmpty(member.ClassName))
            {
                var classMessage = $"{mention} I don't know {member.CharacterName}'s class. Please reply to this message with {member.CharacterName}'s class." +
                $"Please do this before raid time or the split will ignore you. You should only have to do this once. " +
                $"{Environment.NewLine}**Acceptable Values:** {Environment.NewLine}" +
                $"```yaml{Environment.NewLine}{string.Join(", ", config.Classes.Select(x => x.Name).OrderBy(x => x))}{Environment.NewLine}```";
                var messageResult = await textCommunicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SpamChannel, classMessage);

                var unknownMessage = new UnknownMessage()
                {
                    GuildID = raidEvent.GuildID,
                    ChannelID = messageResult.Channel.Id,
                    MessageID = messageResult.Id,
                    CharacterType = characterType.Name,
                    MessageType = UnknownMessageTypes.Class,
                    CharacterName = member.CharacterName,
                    UserID = userID,
                    UserName = user?.Nickname ?? user?.Username,
                };
                splitDataStore.UnknownMessages.TryAdd(unknownMessage.MessageID, unknownMessage);
                splitDataStore.SaveChanges();

                return null;
            }

            return member;
        }

        public string UpdateLeaders(string characterNames, bool value)
        {
            var characters = characterNames.Split(' ');
            foreach (var character in characters)
            {
                var member = findMember(character);
                if (member == null)
                    continue;

                member.CanBeLeader = value;
            }
            splitDataStore.SaveChanges();
            var currentMembers = splitDataStore.Roster.Where(x => x.Value.CanBeLeader).Select(x => x.Value.CharacterName);
            return $"Leaders:{string.Join(",", currentMembers)}";
        }

        public string UpdateLooters(string characterNames, bool value)
        {
            var characters = characterNames.Split(' ');
            foreach (var character in characters)
            {
                var member = findMember(character);
                if (member == null)
                    continue;

                member.CanBeMasterLooter = value;
            }
            splitDataStore.SaveChanges();
            var currentMembers = splitDataStore.Roster.Where(x => x.Value.CanBeMasterLooter).Select(x => x.Value.CharacterName);
            return $"Looters:{string.Join(",", currentMembers)}";
        }

        public string UpdateInviters(string characterNames, bool value)
        {
            var characters = characterNames.Split(' ');
            foreach (var character in characters)
            {
                var member = findMember(character);
                if (member == null)
                    continue;

                member.CanBeInviter = value;
            }
            splitDataStore.SaveChanges();
            var currentMembers = splitDataStore.Roster.Where(x => x.Value.CanBeInviter).Select(x => x.Value.CharacterName);
            return $"Inviters:{string.Join(",", currentMembers)}";
        }

        public string UpdateAnchors(string characterNames, bool value)
        {
            var characters = characterNames.Split(' ');
            foreach (var character in characters)
            {
                var member = findMember(character);
                if (member == null)
                    continue;

                member.IsAnchor = value;
            }
            splitDataStore.SaveChanges();
            var currentAnchors = splitDataStore.Roster.Where(x => x.Value.IsAnchor).Select(x => x.Value.CharacterName);
            return $"Anchors:{string.Join(", ", currentAnchors)}";
        }

        public string GetAnchors()
        {
            var currentAnchors = splitDataStore.Roster.Where(x => x.Value.IsAnchor).Select(x => x.Value.CharacterName);
            return $"Anchors:{string.Join(", ", currentAnchors)}";
        }

        public void SetBuddies(string characterNames)
        {
            var characters = characterNames.Split(' ');
            var group = Guid.NewGuid().ToString();
            foreach (var character in characters)
            {
                var member = findMember(character);
                if (member == null)
                    continue;

                member.BuddieGroup = group;
            }
            splitDataStore.SaveChanges();
        }

        public void RemoveBuddies(string characterNames)
        {
            var characters = characterNames.Split(' ');
            foreach (var character in characters)
            {
                var member = findMember(character);
                if (member == null)
                    continue;

                member.BuddieGroup = null;
            }
            splitDataStore.SaveChanges();
        }

        public string GetBuddies()
        {
            var sb = new StringBuilder();
            var buddieGroups = splitDataStore.Roster.Values.Where(x => x.BuddieGroup != null).GroupBy(x => x.BuddieGroup);
            int x = 1;
            foreach (var buddyGroup in buddieGroups)
            {
                sb.AppendLine($"Buddy Group {x}: {string.Join(", ", buddyGroup.Select(x => x.CharacterName))}.");
                x++;
            }
            return sb.ToString();
        }

        public string Unmap(string characterName)
        {
            var member = findMember(characterName);
            if (member != null && member.UserId.HasValue)
            {
                var mention = MentionUtils.MentionUser(member.UserId.Value);
                member.UserId = null;
                member.CharacterType = null;
                splitDataStore.SaveChanges();
                return $"{member.CharacterName} has been unmapped from {mention} .";
            }
            else
            {
                return $"I cannot unmap {characterName}.";
            }

        }


        private GuildMember findMember(string characterName)
        {
            return splitDataStore.Roster.Values.FirstOrDefault(x => string.Equals(x.CharacterName, characterName, StringComparison.OrdinalIgnoreCase));
        }


        private string NormalizeName(string name)
        {
            if (name?.Length <= 0)
                return name;

            return char.ToUpper(name[0]) + name.Substring(1).ToLower();
        }

    }
}
