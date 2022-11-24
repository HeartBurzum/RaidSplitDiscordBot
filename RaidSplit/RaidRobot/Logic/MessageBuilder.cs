using RaidRobot.Data.Entities;
using RaidRobot.Infrastructure;
using RaidRobot.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaidRobot.Logic
{
    public class MessageBuilder : IMessageBuilder
    {
        private readonly IRaidSplitConfiguration config;

        public MessageBuilder(IRaidSplitConfiguration config)
        {
            this.config = config;
        }

        public string BuildSplitAnnouncement(RaidEvent raidEvent, Split split)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"**{raidEvent.RaidType.Name} Raid {raidEvent.EventName} on {raidEvent.EventDT.ToShortDateString()} at {raidEvent.EventDT.ToString("hh:mm tt")}**");
            sb.AppendLine("```md");
            sb.AppendLine($"Split {split.SplitNumber} - Leader {split.Leader.CharacterName} - ({split.Attendees.Count})");
            sb.AppendLine("-------------------------------");
            sb.AppendLine($"* Send Your x's for invites to <{split.Inviter.CharacterName}>");
            sb.AppendLine($"* Your Master Looter is <{split.MasterLooter.CharacterName}>");

            foreach (var guildClass in config.Classes.OrderBy(x => x.Name))
            {
                var members = split.Attendees.Values.Where(x => string.Equals(x.ClassName, guildClass.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.CharacterName).OrderBy(x => x).ToList();

                var names = string.Join(", ", members);
                if (members.Count > 0)
                    sb.AppendLine($"[{guildClass.ShortName}]({members.Count()}) {names}");
                else
                    sb.AppendLine($"#{guildClass.ShortName} ({members.Count()}) {names}");
            }

            sb.AppendLine("```");
            if (split.Actions.Count != 0) {
                sb.AppendLine("```yaml");
                foreach (var action in split.Actions.OrderByDescending(x => x.actionTime).Take(5))
                {
                    sb.AppendLine($"{action.actionTime.ToString("HH:mm")} {action.action}");
                }
                sb.AppendLine("```");
            }

            return sb.ToString();
        }

        public string BuildRegistrationMessage(RaidEvent raidEvent)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine($"**New {raidEvent.RaidType.Name} Raid {raidEvent.EventName} on {raidEvent.EventDT.ToShortDateString()} at {raidEvent.EventDT.ToString("hh:mm tt")} Sign Up Below**");
            sb.AppendLine("```md");
            sb.AppendLine("1. To register for the raid react to this message using the appropriate emoji for your character type.");
            sb.AppendLine($"--- {string.Join(", ", raidEvent.RaidType.CharacterTypes.Select(x => $"{x.Name} {x.EmojiCode}"))}");
            sb.AppendLine("2. If you are bringing multiple characters, please react to all the emojis that apply.");
            sb.AppendLine("3. Use the clock emoji if you are going to be late. Uncheck it when you arrive to be put into a split.");
            sb.AppendLine("4. Use the no entry emoji if you cannot make the raid.");
            sb.AppendLine("5. Use question mark emoji once the splits are announced to easily find your split.");
            sb.AppendLine("```");

            return sb.ToString();
        }

        public string BuildAttendeeMessage(RaidEvent raidEvent, RegistrantResponse registrants)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"**{raidEvent.EventName}  on {raidEvent.EventDT.ToShortDateString()} at {raidEvent.EventDT.ToString("hh:mm tt")}**");

            if (registrants != null)
            {
                sb.AppendLine($"__Total ({registrants.Members.Count})  " +
                    $"Mains ({registrants.Members.Where(x => !x.Value.IsBox).Count()}) " +
                    $"Boxes ({registrants.Members.Where(x => x.Value.IsBox).Count()}) " +
                    $"Unknown ({registrants.IncompleteUsers.Count + registrants.UnknownUsers.Count})__");

                StringBuilder longClassNames = new StringBuilder();
                StringBuilder shortClassNames = new StringBuilder();

                foreach (var gameClass in config.Classes.OrderBy(x => x.Name))
                {
                    var longClassIntro = $"{gameClass.EmojiCode} **{gameClass.ShortName}**";
                    var shortClassIntro = $"**{gameClass.ShortName}**";

                    var people = registrants.Members.Values.Where(x => string.Equals(x.ClassName, gameClass.Name, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => x.CharacterName).ToList();

                    var classLine = new StringBuilder();
                    var shortClassLine = new StringBuilder();

                    classLine.Append(longClassIntro);
                    classLine.Append($" ({people.Count()}) ");

                    shortClassLine.Append(shortClassIntro);
                    shortClassLine.Append($" ({people.Count()}) ");

                    foreach (var person in people)
                    {
                        if (person.IsBox)
                        {
                            classLine.Append($"{person.CharacterName} (B)");
                        }
                        else
                        {
                            classLine.Append($"{person.CharacterName}");
                        }

                        if (person.IsLate)
                        {
                            classLine.Append(" (L)");
                        }

                        if (person.CharacterName.Length > 7)
                        {
                            shortClassLine.Append(person.CharacterName.Substring(0, 6) + " ");
                        }
                        else
                        {
                            shortClassLine.Append(person.CharacterName + " ");
                        }

                        classLine.Append(", ");
                    }

                    longClassNames.AppendLine(classLine.ToString());
                    shortClassNames.AppendLine(shortClassLine.ToString());
                }

                if (longClassNames.Length <= 1900)
                {
                    sb.AppendLine(longClassNames.ToString());
                }
                else
                {
                    sb.AppendLine(shortClassNames.ToString());
                }
            }
            return sb.ToString();
        }

        public string BuildLateMessage(RaidEvent raidEvent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("```md");
            sb.AppendLine("Late for the raid");
            sb.AppendLine("---------------------------");
            if (raidEvent.LateMembers.Any())
            {
                sb.AppendLine(string.Join(", ", raidEvent.LateMembers.Values.Select(x => x.CharacterName)));
            }
            else
            {
                sb.AppendLine("No One...");
            }
            sb.AppendLine("```");

            return sb.ToString();
        }

        public string BuildResponsiblityAnnouncement(Split split)
        {
            var sb = new StringBuilder();
            sb.AppendLine("```md");
            sb.AppendLine($"Split {split.SplitNumber} - Leader: {split.Leader.CharacterName} - Looter: {split.MasterLooter.CharacterName} - Inviter: {split.Inviter.CharacterName}");
            sb.AppendLine($"------------------------");
            sb.AppendLine("* Reply Leader followed by a character name to change it.");
            sb.AppendLine("* Reply Looter followed by a character name to change it.");
            sb.AppendLine("* Reply Inviter followed by a character name to change it.");
            sb.AppendLine("* Reply Move followed by a character name to move them into this split.");
            sb.AppendLine("* Reply Swap followed by two character names to swap their splits.");
            sb.AppendLine("* Reply Redo followed by a split count to rebuild the splits using a differnt random seed.");
            sb.AppendLine("* Reply IgnoreBuddies to redo the splits without buddies.");
            sb.AppendLine("```");
            return sb.ToString();
        }

    }
}
