using Discord;
using Discord.Commands;
using Discord.WebSocket;
using RaidRobot.Data;
using RaidRobot.Data.Entities;
using RaidRobot.Infrastructure;
using RaidRobot.Logic.Interfaces;
using RaidRobot.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace RaidRobot.Logic
{
    public class EventOrchestrator : IEventOrchestrator
    {
        private readonly DiscordSocketClient client;
        private readonly ITextCommunicator communicator;
        private readonly IRaidSplitter raidSplitter;
        private readonly IMessageBuilder messageBuilder;
        private readonly IRegistrantLoader registrantLoader;
        private readonly IRaidSplitConfiguration config;
        private readonly ISplitDataStore splitDataStore;
        private readonly ISplitAuditor auditor;
        private readonly IGuildMemberConverter converter;
        private readonly ISplitOrchestrator splitOrchestrator;

        public EventOrchestrator(
            DiscordSocketClient client,
            ITextCommunicator communicator,
            IRaidSplitter raidSplitter,
            IMessageBuilder messageBuilder,
            IRegistrantLoader registrantLoader,
            IRaidSplitConfiguration config,
            ISplitDataStore splitDataStore,
            ISplitAuditor auditor,
            IGuildMemberConverter converter,
            ISplitOrchestrator splitOrchestrator)
        {
            this.client = client;
            this.communicator = communicator;
            this.raidSplitter = raidSplitter;
            this.messageBuilder = messageBuilder;
            this.registrantLoader = registrantLoader;
            this.config = config;
            this.splitDataStore = splitDataStore;
            this.auditor = auditor;
            this.converter = converter;
            this.splitOrchestrator = splitOrchestrator;
        }

        public async Task RemoveRegistrant(RaidEvent raidEvent, CharacterType characterType, ulong userID)
        {
            var guild = client.GetGuild(raidEvent.GuildID);
            var user = guild?.GetUser(userID);
            if (user == null || user.IsBot)
                return;

            if (raidEvent.Splits.Any())
            {
                //This means the splits are final or the admin is working on building the splits for the event so we need to update the data.
                await RemoveUser(raidEvent, userID, characterType);
            }
            await UpdateAteendeeMessage(raidEvent);
        }

        public async Task RemoveCharacter(ulong guildID, string eventName, string characterName)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            var split = raidEvent.Splits.Values
                .FirstOrDefault(x => x.Attendees.Any(y => string.Equals(y.Value.CharacterName, characterName, StringComparison.OrdinalIgnoreCase)));
            if (split == null)
            {
                await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel, $"{characterName} is not in a split.");
                return;
            }

            splitOrchestrator.RemoveFromSplit(split, characterName);
            splitDataStore.SaveChanges();

            await UpdateSplitAnnouncement(raidEvent, split);
        }

        public async Task RemoveUser(RaidEvent raidEvent, ulong userID, CharacterType characterType)
        {
            var result = raidSplitter.RemoveFromSplit(raidEvent.Splits, userID, characterType);

            if (!result.HasError)
                await UpdateSplitAnnouncement(raidEvent, result.Split);
            else
                await communicator.SendTell(userID, result.Message);
        }

        public async Task AddRegistrant(RaidEvent raidEvent, CharacterType characterType, GuildMember member, ulong userID)
        {
            var mention = MentionUtils.MentionUser(member.UserId.Value);
            var content = $"{mention} Thank you for registering to attend {raidEvent.EventName} on {member.CharacterName} as a {characterType.Name}.";
            await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SpamChannel, content);

            if (!raidEvent.Splits.Any())
                return; //Haven't started split making yet, don't need to do anything else.

            var attendee = converter.ConvertToAttendee(member, characterType, userID);
            var addResult = raidSplitter.AddToSplit(raidEvent, raidEvent.Splits, attendee);

            if (!addResult.HasError)
                await UpdateSplitAnnouncement(raidEvent, addResult.Split);
            else
                await communicator.SendTell(userID, addResult.Message);

            if (raidEvent.FinalizedDT.HasValue)
            {
                await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SpamChannel, $"{mention} {addResult.Message}");
            }
        }

        public async Task IsNowLate(RaidEvent raidEvent, ulong userID)
        {
            if (!raidEvent.Splits.Any())
                return; //Haven't started splits we don't care if they are late yet.

            List<SplitAttendee> lateCharacters = new List<SplitAttendee>();
            foreach (var split in raidEvent.Splits)
            {
                lateCharacters.AddRange(split.Value.Attendees.Values.Where(x => x.UserID == userID));
            }

            foreach (var lateCharacter in lateCharacters)
            {
                var result = raidSplitter.RemoveFromSplit(raidEvent.Splits, lateCharacter);
                if (!result.HasError)
                {
                    await UpdateSplitAnnouncement(raidEvent, result.Split);
                    result.Split.Actions.Add((DateTime.Now, $"Removed {result.Attendee.CharacterName}."));

                }
                raidEvent.LateMembers[lateCharacter.CharacterName] = lateCharacter;
            }
            splitDataStore.SaveChanges();

            if (lateCharacters.Any())
                await updateLateAnnouncement(raidEvent);
        }

        public async Task NoLongerLate(RaidEvent raidEvent, ulong userID)
        {
            if (!raidEvent.Splits.Any())
                return; //Splits haven't started, we don't care if they aren't going to be late...

            var lateCharacters = raidEvent.LateMembers.Values.Where(x => x.UserID == userID).ToList();
            foreach (var lateCharacter in lateCharacters)
            {
                var addResult = raidSplitter.AddToSplit(raidEvent, raidEvent.Splits, lateCharacter);
                addResult.Split.Actions.Add((DateTime.Now, $"No Longer Late - Added {addResult.Attendee.CharacterName}"));
                raidEvent.LateMembers.Remove(lateCharacter.CharacterName);
                await UpdateSplitAnnouncement(raidEvent, addResult.Split);
            }
            splitDataStore.SaveChanges();


            if (lateCharacters.Any())
                await updateLateAnnouncement(raidEvent);
        }

        public async Task WhereAmI(RaidEvent raidEvent, ulong userID)
        {
            var mention = MentionUtils.MentionUser(userID);
            if (!raidEvent.FinalizedDT.HasValue)
            {
                await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SpamChannel, $"{mention} The Splits aren't final, please wait.");
                return;
            }

            foreach (var split in raidEvent.Splits.Values)
            {
                var characters = split.Attendees.Values.Where(x => x.UserID == userID).Select(x => x.CharacterName);
                if (characters.Any())
                {
                    var characterNames = string.Join(" , ", characters);
                    var message = $"{mention} : {characterNames} are in Split{split.SplitNumber} send an x to {split.Inviter}";
                    await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SpamChannel, message);
                    return;
                }
            }

            await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SpamChannel, $"{mention} You are not in a split.");
        }

        public async Task UpdateAteendeeMessage(RaidEvent raidEvent)
        {
            if (raidEvent.Messages.ContainsKey(MessageContexts.AttendeeMessage))
            {
                var message = raidEvent.Messages[MessageContexts.AttendeeMessage];
                var registrants = await registrantLoader.GetRegistrants(raidEvent);
                var content = messageBuilder.BuildAttendeeMessage(raidEvent, registrants);
                await communicator.UpdateMessage(raidEvent.GuildID, message.ChannelID, message.MessageID, content);
            }
        }

        public async Task<string> SplitASplit(ulong guildID, ulong userID, string username, string eventName, int splitNumber, int numberOfSplits)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return $"Couldn't Find Active Raid Named {eventName}";

            if (!raidEvent.Splits.ContainsKey(splitNumber))
                return $"{eventName} doesn't have a Split {splitNumber}.";

            var split = raidEvent.Splits[splitNumber];

            raidEvent.SubSplitCount++;
            var members = split.Attendees.Values.Select(x => x.Clone())
                .ToDictionary(x => x.CharacterName, x => x);

            string newEventName = $"SubSplit_{raidEvent.SubSplitCount}_{raidEvent.EventName}";
            var newEvent = createEvent(newEventName, DateTime.Now.AddHours(1),
                raidEvent.RaidType, userID,
                guildID,
                username);

            newEvent.IsASubSplit = true;
            newEvent.SourceEventName = raidEvent.EventName;
            newEvent.SourceEventSplitNumber = split.SplitNumber;

            splitDataStore.Events.TryAdd(newEvent.EventID, newEvent);
            splitDataStore.SaveChanges();
            await PrepareSplits(newEvent, numberOfSplits, members);

            return $"Created Sub Split from {eventName}.";
        }


        public async Task UpdateSplitAnnouncement(RaidEvent raidEvent, Split split)
        {
            MessageDetail message = null;
            var content = messageBuilder.BuildSplitAnnouncement(raidEvent, split);

            if (split.Messages.ContainsKey(MessageContexts.Announcement))
            {
                message = split.Messages[MessageContexts.Announcement];
                await communicator.UpdateMessage(raidEvent.GuildID, message.ChannelID, message.MessageID, content);

            }

            if (split.Messages.ContainsKey(MessageContexts.FinalAnnouncement))
            {
                message = split.Messages[MessageContexts.FinalAnnouncement];
                await communicator.UpdateMessage(raidEvent.GuildID, message.ChannelID, message.MessageID, content);
            }
        }

        public async Task PrepareSplits(ulong guildID, string eventName, int numberOfSplits)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            await PrepareSplits(raidEvent, numberOfSplits);
        }

        public async Task PrepareSplits(RaidEvent raidEvent, int numberOfSplits, Dictionary<string, SplitAttendee> members = null, bool ignoreBuddies = false)
        {
            if (raidEvent.FinalizedDT.HasValue)
            {
                await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel,
                    "The raid is finalized, use the help commands to re-open the event if you want to make changes.");
                return;
            }

            if (members == null && raidEvent.IsASubSplit)
            {
                var sourceEvent = splitDataStore.Events.Values.FirstOrDefault(x => x.ExpirationDT >= DateTime.Now &&
                    string.Equals(x.EventName, raidEvent.SourceEventName, StringComparison.OrdinalIgnoreCase));
                if (sourceEvent == null)
                {
                    await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel,
                        $"Can't find Source Event {raidEvent.SourceEventName} to recreate splits.");
                    return;
                }

                members = sourceEvent.Splits[raidEvent.SourceEventSplitNumber].Attendees.Values.Select(x => x.Clone())
                    .ToDictionary(x => x.CharacterName, x => x);
            }

            var newExp = DateTime.Now.AddHours(Constants.DEFAULT_EXTENSION_HOURS);
            if (newExp >= raidEvent.ExpirationDT)
                raidEvent.ExpirationDT = newExp;

            foreach (var split in raidEvent.Splits)
            {
                await cleanUpSplit(raidEvent, split.Value);
                if (raidEvent.Messages.ContainsKey(MessageContexts.LatePreview))
                {
                    try
                    {
                        var guild = client.GetGuild(raidEvent.GuildID);
                        var channel = guild.GetTextChannel(raidEvent.Messages[MessageContexts.LatePreview].ChannelID);
                        await channel.DeleteMessageAsync(raidEvent.Messages[MessageContexts.LatePreview].MessageID);
                    }
                    catch
                    {

                    }

                    raidEvent.Messages.Remove(MessageContexts.LatePreview);
                }
            }

            raidEvent.Splits = await raidSplitter.Split(raidEvent, numberOfSplits, members, ignoreBuddies);

            foreach (var split in raidEvent.Splits.Values)
            {
                var content = messageBuilder.BuildSplitAnnouncement(raidEvent, split);
                var message = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel, content);
                split.Messages[MessageContexts.Announcement] = message.ConvertToMessageDetail();

                var responsibilitiesContent = messageBuilder.BuildResponsiblityAnnouncement(split);
                var responsibilitiesMessage = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel, responsibilitiesContent);
                split.Messages[MessageContexts.ResponsibilityMessage] = responsibilitiesMessage.ConvertToMessageDetail();

                try
                {
                    await auditor.GenerateAuditFile(raidEvent, split);
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Error creating audit file, {ex.Message}");
                }

            }

            var lateMessageContent = messageBuilder.BuildLateMessage(raidEvent);
            var lateMessage = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel, lateMessageContent);
            raidEvent.Messages[MessageContexts.LatePreview] = lateMessage.ConvertToMessageDetail();

            splitDataStore.SaveChanges();
        }

        public async Task FinalizeSplits(ulong guildID, string eventName)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            if (raidEvent.FinalizedDT.HasValue)
            {
                await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel,
                    "The event is already finalized.");
                return;
            }


            if (!raidEvent.Splits.Any())
            {
                await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel,
                    $"You must preview the splits before creating them.  Type {config.Settings.MessagePrefix} PreviewSplit EventName NumberOfSplits");
                return;
            }

            raidEvent.ExpirationDT = DateTime.Now.AddHours(Constants.DEFAULT_EVENT_HOURS);
            raidEvent.FinalizedDT = DateTime.Now;

            foreach (var split in raidEvent.Splits.Values)
            {
                var content = messageBuilder.BuildSplitAnnouncement(raidEvent, split);
                var message = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SplitChannel, content);
                split.Messages[MessageContexts.FinalAnnouncement] = message.ConvertToMessageDetail();
            }

            var lateContent = messageBuilder.BuildLateMessage(raidEvent);
            var lateMessage = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SplitChannel, lateContent);
            raidEvent.Messages[MessageContexts.LateAnnouncement] = lateMessage.ConvertToMessageDetail();

            splitDataStore.SaveChanges();
        }

        public async Task AddASplit(ulong guildID, string eventName)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            if (!raidEvent.Splits.Any())
            {
                await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel,
                    $"You must preview the splits before Adding One them.  Type {config.Settings.MessagePrefix} PreviewSplit EventName NumberOfSplits");
                return;
            }

            raidEvent.SplitHistory.Add(DateTime.Now, raidEvent.Splits.Clone());

            var newSplit = raidSplitter.AddASplit(raidEvent);
            var previewContent = messageBuilder.BuildSplitAnnouncement(raidEvent, newSplit);
            var previewMessage = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel, previewContent);
            newSplit.Messages[MessageContexts.Announcement] = previewMessage.ConvertToMessageDetail();

            var responsibilitiesContent = messageBuilder.BuildResponsiblityAnnouncement(newSplit);
            var responsibilitiesMessage = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel, responsibilitiesContent);
            newSplit.Messages[MessageContexts.ResponsibilityMessage] = responsibilitiesMessage.ConvertToMessageDetail();


            if (raidEvent.FinalizedDT.HasValue)
            {
                var content = messageBuilder.BuildSplitAnnouncement(raidEvent, newSplit);
                var message = await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.SplitChannel, content);
                newSplit.Messages[MessageContexts.FinalAnnouncement] = message.ConvertToMessageDetail();
            }

            foreach(var split in raidEvent.Splits.Values)
            {
                await UpdateSplitAnnouncement(raidEvent, split);
            }

            splitDataStore.SaveChanges();
        }

        public async Task Undo(ulong guildID, string eventName)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            if (!raidEvent.Splits.Any())
            {
                await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel,
                    $"You must preview the splits before Udoing anything.  Type {config.Settings.MessagePrefix} PreviewSplit EventName NumberOfSplits");
                return;
            }

            if(!raidEvent.SplitHistory.Any())
            {
                await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel,
                    $"No split history to undo.");
                return;
            }

            var lastChangeDT = raidEvent.SplitHistory.Max(x => x.Key);
            raidEvent.SplitHistory.Remove(lastChangeDT, out var splits);

            var removedSplits = raidEvent.Splits.Where(x => !splits.Keys.Contains(x.Key)).Select(x => x.Value);
            foreach(var removedSplit in removedSplits)
            {
                await cleanUpSplit(raidEvent, removedSplit);
            }

            raidEvent.Splits = splits;
            foreach(var split in raidEvent.Splits.Values)
            {
                try
                {
                    await UpdateSplitAnnouncement(raidEvent, split);
                }
                catch { }
            }
            splitDataStore.SaveChanges();
        }

        public async Task MoveTo(ulong guildID, string eventName, string characterName, int splitNumber)
        {
            var activeEvent = await findActiveEvent(guildID, eventName);
            if (activeEvent == null)
                return;

            var result = splitOrchestrator.MoveTo(activeEvent, activeEvent.Splits, characterName, splitNumber);
            splitDataStore.SaveChanges();

            var message = $"```asciidoc{Environment.NewLine}{result}{Environment.NewLine}```";
            await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel, message);
            foreach (var split in activeEvent.Splits.Values)
            {
                await UpdateSplitAnnouncement(activeEvent, split);
            }
        }

        public async Task Swap(ulong guildID, string eventName, string characterName1, string characterName2)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            var result = splitOrchestrator.Swap(raidEvent, raidEvent.Splits, characterName1, characterName2);
            splitDataStore.SaveChanges();

            var message = $"```yaml{Environment.NewLine}{result}{Environment.NewLine}```";
            await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel, message);

            foreach (var split in raidEvent.Splits.Values)
            {
                await UpdateSplitAnnouncement(raidEvent, split);
            }
        }

        public async Task OpenEvent(ulong guildID, string eventName)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            if (!raidEvent.FinalizedDT.HasValue)
            {
                await communicator.SendMessageByChannelName(raidEvent.GuildID, config.Settings.AdminChannel, $"The event hasn't been finalized.");
                return;
            }

            raidEvent.FinalizedDT = null;

            var guild = client.GetGuild(raidEvent.GuildID);
            foreach (var split in raidEvent.Splits.Values)
            {
                if (split.Messages.ContainsKey(MessageContexts.FinalAnnouncement))
                {
                    try
                    {
                        var channel = guild.GetTextChannel(split.Messages[MessageContexts.FinalAnnouncement].ChannelID);
                        await channel.DeleteMessageAsync(split.Messages[MessageContexts.FinalAnnouncement].MessageID);
                    }
                    catch { }
                    split.Messages.Remove(MessageContexts.FinalAnnouncement);
                }
            }

            if (raidEvent.Messages.ContainsKey(MessageContexts.LateAnnouncement))
            {
                try
                {
                    var channel = guild.GetTextChannel(raidEvent.Messages[MessageContexts.LateAnnouncement].ChannelID);
                    await channel.DeleteMessageAsync(raidEvent.Messages[MessageContexts.LateAnnouncement].MessageID);
                }
                catch { }
                raidEvent.Messages.Remove(MessageContexts.LateAnnouncement);
            }

            splitDataStore.SaveChanges();
        }

        public async Task NeedsItem(ulong guildID, string eventName, string itemName, string characterNames)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            var characters = characterNames.Split(" ");
            foreach (var character in characters)
            {
                if (raidEvent.ItemNeeds.Any(x => x.CharacterName.ToLower() == character.ToLower() && x.Item.ToLower() == itemName.ToLower()))
                    continue;

                raidEvent.ItemNeeds.Add(new ItemNeed()
                {
                    CharacterName = character,
                    Item = itemName,
                });
            }

            splitDataStore.SaveChanges();

            StringBuilder sb = new StringBuilder();
            var itemGroups = raidEvent.ItemNeeds.GroupBy(x => x.Item);
            sb.AppendLine("```yaml");
            sb.AppendLine("Item Needs Updated");
            foreach (var item in itemGroups)
            {
                sb.AppendLine($"**{item.Key}**");
                var charactersOrdered = item.Select(x => x.CharacterName).OrderBy(x => x).ToList();
                var charactersJoined = string.Join(", ", charactersOrdered);
                sb.AppendLine(charactersJoined);
            }
            sb.AppendLine("```");

            await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel, sb.ToString());
        }

        public async Task CancelEvent(ulong guildID, string eventName)
        {
            var raidEvent = await findActiveEvent(guildID, eventName);
            if (raidEvent == null)
                return;

            await cleanUpEvent(raidEvent);

            splitDataStore.Events.TryRemove(raidEvent.EventID, out var removed);
            splitDataStore.SaveChanges();

            await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel,
                $"```yaml{Environment.NewLine}Event, {eventName} has been removed.{Environment.NewLine}```");
        }

        private async Task updateLateAnnouncement(RaidEvent raidEvent)
        {
            MessageDetail lateMessage = null;
            var lateContent = messageBuilder.BuildLateMessage(raidEvent);
            if (raidEvent.Messages.ContainsKey(MessageContexts.LatePreview))
            {
                lateMessage = raidEvent.Messages[MessageContexts.LatePreview];
                await communicator.UpdateMessage(raidEvent.GuildID, lateMessage.ChannelID, lateMessage.MessageID, lateContent);
            }

            if (raidEvent.Messages.ContainsKey(MessageContexts.LateAnnouncement))
            {
                lateMessage = raidEvent.Messages[MessageContexts.LateAnnouncement];
                await communicator.UpdateMessage(raidEvent.GuildID, lateMessage.ChannelID, lateMessage.MessageID, lateContent);
            }
        }

        private async Task<RaidEvent> findActiveEvent(ulong guildID, string eventName)
        {
            var raidEvent = splitDataStore.Events.Values
                .FirstOrDefault(x => x.ExpirationDT >= DateTime.Now && string.Equals(x.EventName, eventName, StringComparison.OrdinalIgnoreCase));

            if (raidEvent == null)
            {
                await communicator.SendMessageByChannelName(guildID, config.Settings.AdminChannel, $"No Active Event named: {eventName}.");
            }

            return raidEvent;
        }

        private async Task cleanUpEvent(RaidEvent raidEvent)
        {
            try
            {
                var guild = client.GetGuild(raidEvent.GuildID);

                foreach (var message in raidEvent.Messages.ToList())
                {
                    var channel = guild.GetTextChannel(message.Value.ChannelID);
                    await channel.DeleteMessageAsync(message.Value.MessageID);
                    raidEvent.Messages.Remove(message.Key);
                }
            }
            catch
            {
                //Do Nothing...
            }

            foreach (var split in raidEvent.Splits.Values)
            {
                await cleanUpSplit(raidEvent, split);
            }
        }

        private async Task cleanUpSplit(RaidEvent raidEvent, Split split)
        {
            var guild = client.GetGuild(raidEvent.GuildID);
            foreach (var message in split.Messages)
            {
                try
                {
                    var channel = guild.GetTextChannel(message.Value.ChannelID);
                    await channel.DeleteMessageAsync(message.Value.MessageID);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not delete Message {message.Key}, {ex.Message}");
                }
            }
        }

        private RaidEvent createEvent(string eventName, DateTime eventTime, RaidType raidType, ulong userID, ulong guildID, string userName)
        {
            var newEvent = new RaidEvent()
            {
                ExpirationDT = eventTime.AddHours(6),
                EventName = eventName,
                UserID = userID,
                GuildID = guildID,
                EventDT = eventTime,
                UserName = userName,
                RaidType = raidType,
            };
            return newEvent;
        }


    }
}
