using Discord;
using Discord.Commands;
using Microsoft.Extensions.Configuration;
using RaidRobot.Data;
using RaidRobot.Data.Entities;
using RaidRobot.Infrastructure;
using RaidRobot.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaidRobot.Logic
{
    public class EventCreator : IEventCreator
    {
        private readonly IRaidSplitConfiguration config;
        private readonly ISplitDataStore splitDataStore;
        private readonly ITextCommunicator textCommunicator;
        private readonly IRegistrantLoader registrantLoader;
        private readonly IMessageBuilder messageBuilder;

        public EventCreator(
            IRaidSplitConfiguration config,
            ISplitDataStore splitDataStore,
            ITextCommunicator textCommunicator, 
            IRegistrantLoader registrantLoader,
            IMessageBuilder messageBuilder)
        {
            this.config = config;
            this.splitDataStore = splitDataStore;
            this.textCommunicator = textCommunicator;
            this.registrantLoader = registrantLoader;
            this.messageBuilder = messageBuilder;
        }

        public async Task CreateEvent(SocketCommandContext context, IGuildUser user, string eventName, DateTime raidTime, string raidTypeName)
        {
            if (user == null || string.IsNullOrEmpty(eventName))
                throw new ArgumentNullException("User nor event name can be null");

            var eventExists = splitDataStore.Events.Values
                .Any(x => string.Equals(x.EventName, eventName, StringComparison.OrdinalIgnoreCase)
                    && x.ExpirationDT >= DateTime.Now);

            if (eventExists)
                throw new ArgumentException($"There is already an active event named {eventName}. Cancel the existing event or choose another name.");

            var channel = context.Guild.Channels.FirstOrDefault(x => x.Name == config.Settings.RegistrationChannel);
            if (channel == null)
                throw new ArgumentException($"Could not find a Registration Channel Named {config.Settings.RegistrationChannel}. Please check your appsettings.json");

            var raidType = config.RaidTypes.FirstOrDefault(x => string.Equals(x.Name, raidTypeName, StringComparison.OrdinalIgnoreCase));
            if (raidType == null)
                throw new ArgumentException($"{raidTypeName} is not a valid Raid Type. Acceptable Values: {string.Join(", ", config.RaidTypes.Select(x => x.Name))}");


            var raidEvent = new RaidEvent()
            {
                ExpirationDT = raidTime.AddHours(Constants.DEFAULT_EVENT_HOURS),
                EventName = eventName,
                UserID = user.Id,
                GuildID = context.Guild.Id,
                EventDT = raidTime,
                UserName = user.Nickname ?? user.DisplayName,
                RaidType = raidType,
            };

            var registrationMessage = messageBuilder.BuildRegistrationMessage(raidEvent);
            var registrationMessageResult = await textCommunicator.SendMessage(context.Guild.Id, channel.Id, registrationMessage);
            raidEvent.Messages[MessageContexts.RegistrationMessage] = registrationMessageResult.ConvertToMessageDetail();

            foreach (var characterType in raidType.CharacterTypes)
            {
                await textCommunicator.React(context.Guild.Id, channel.Id, registrationMessageResult.Id, characterType.EmojiCode);
            }
            await textCommunicator.React(context.Guild.Id, channel.Id, registrationMessageResult.Id, Reactions.LateCode);
            await textCommunicator.React(context.Guild.Id, channel.Id, registrationMessageResult.Id, Reactions.NoShowCode);
            await textCommunicator.React(context.Guild.Id, channel.Id, registrationMessageResult.Id, Reactions.HelpCode);

            var registrants = await registrantLoader.GetRegistrants(raidEvent);
            var attendeeMessage = messageBuilder.BuildAttendeeMessage(raidEvent, registrants);
            var attendeeResult = await textCommunicator.SendMessage(context.Guild.Id, channel.Id, attendeeMessage);
            raidEvent.Messages[MessageContexts.AttendeeMessage] = attendeeResult.ConvertToMessageDetail();

            //var announceMessage = $"{context.Guild.EveryoneRole.Mention} Sign up for {raidEvent.EventName} above.";
            //var announceResult = await textCommunicator.SendMessage(context.Guild.Id, channel.Id, announceMessage);
            //raidEvent.Messages[MessageContexts.EveryoneRegistrationMessage] = announceResult.ConvertToMessageDetail();

            splitDataStore.Events.TryAdd(raidEvent.EventID, raidEvent);
            splitDataStore.SaveChanges();
        }

    }
}
