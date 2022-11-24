using RaidRobot.Data.Entities;
using RaidRobot.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RaidRobot.Models;
using Discord.WebSocket;
using RaidRobot.Infrastructure;
using Discord;

namespace RaidRobot.Logic
{
    public class RaidSplitter : IRaidSplitter
    {
        private readonly IRaidSplitConfiguration config;
        private readonly ISplitDataStore splitDataStore;
        private readonly IGuildMemberConverter converter;
        private readonly ISplitOrchestrator splitOrchestrator;
        private readonly IRegistrantLoader registrantLoader;
        private readonly IRandomizer randomizer;

        public RaidSplitter(
            IRaidSplitConfiguration config,
            ISplitDataStore splitDataStore,
            IGuildMemberConverter converter,
            ISplitOrchestrator splitOrchestrator,
            IRegistrantLoader registrantLoader,
            IRandomizer randomizer)
        {
            this.config = config;
            this.splitDataStore = splitDataStore;
            this.converter = converter;
            this.splitOrchestrator = splitOrchestrator;
            this.registrantLoader = registrantLoader;
            this.randomizer = randomizer;
        }

        public AttendeeResponse RemoveFromSplit(Dictionary<int, Split> splits, ulong userId, CharacterType characterType)
        {
            var member = splitDataStore.Roster.Values
                    .FirstOrDefault(x => x.UserId == userId
                        && string.Equals(x.CharacterType, characterType.Name, StringComparison.OrdinalIgnoreCase));

            if (member == null)
            {
                return new AttendeeResponse()
                {
                    HasError = true,
                    Message = $"Sorry I don't know which character is your {characterType}.  I cannot remove you from a split.",
                };
            }

            if (string.IsNullOrEmpty(member.ClassName))
            {
                return new AttendeeResponse()
                {
                    HasError = true,
                    Message = $"Sorry, I don't know what class {member.CharacterName} is. I cannot remove you from a split.",
                };
            }

            var attendee = converter.ConvertToAttendee(member, characterType, userId);
            return RemoveFromSplit(splits, attendee);
        }

        public AttendeeResponse RemoveFromSplit(Dictionary<int, Split> splits, SplitAttendee splitAttendee)
        {
            var split = splits.Values.FirstOrDefault(x => x.Attendees.Values.Any(y => y.CharacterName == splitAttendee.CharacterName));
            if (split == null)
            {
                return new AttendeeResponse()
                {
                    HasError = true,
                    Message = $"You are not in a Split.",
                };
            }

            splitOrchestrator.RemoveFromSplit(split, splitAttendee.CharacterName);
            split.Actions.Add((DateTime.Now, $"Removed {splitAttendee.CharacterName}."));
            splitDataStore.SaveChanges();

            return new AttendeeResponse()
            {
                Message = $"{splitAttendee.CharacterName} removed from Split {split.SplitNumber}.",
                Split = split,
                Attendee = splitAttendee
            };
        }

        public AttendeeResponse AddToSplit(RaidEvent raidEvent, Dictionary<int, Split> splits, ulong userId, CharacterType characterType, bool ignoreBuddies = false)
        {
            var member = splitDataStore.Roster.Values
                    .FirstOrDefault(x => x.UserId == userId
                        && string.Equals(x.CharacterType, characterType.Name, StringComparison.OrdinalIgnoreCase));

            if (member == null)
            {
                return new AttendeeResponse()
                {
                    HasError = true,
                    Message = $"Sorry I don't know which character is your {characterType}.  I cannot add you to a split.",
                };
            }

            if (string.IsNullOrEmpty(member.ClassName))
            {
                return new AttendeeResponse()
                {
                    HasError = true,
                    Message = $"Sorry, I don't know what class {member.CharacterName} is. I cannot add you to a split.",
                };
            }

            var attendee = converter.ConvertToAttendee(member, characterType, userId);
            return AddToSplit(raidEvent, splits, attendee, ignoreBuddies);
        }

        public AttendeeResponse AddToSplit(RaidEvent raidEvent, Dictionary<int, Split> splits, SplitAttendee attendee, bool ignoreBuddies = false)
        {
            var alreadyInASplit = raidEvent.Splits.Values.FirstOrDefault(x => x.Attendees.Values.Any(y => string.Equals(y.CharacterName, attendee.CharacterName, StringComparison.OrdinalIgnoreCase)));
            if (alreadyInASplit != null)
            {
                return new AttendeeResponse()
                {
                    HasError = true,
                    Message = $"You are already in Split {alreadyInASplit.SplitNumber} Send an X to {alreadyInASplit.Inviter.CharacterName}.",
                    Split = alreadyInASplit,
                    Attendee = attendee
                };
            }

            var splitResult = findBestSplitForAttendee(raidEvent, splits, attendee, ignoreBuddies);
            var bestSplit = splitResult.Split;

            //Check to see if your are already in a split with another character.
            var existingSplit = splits.Values.FirstOrDefault(x => x.Attendees.Values.Any(y => y.UserID == attendee.UserID));
            if (existingSplit != null)
            {
                attendee.IsBoxing = true;
                bestSplit = existingSplit;
            }

            addSplitAttendee(splits, bestSplit, attendee, Enumerable.Empty<SplitAttendee>(), splitResult.Audit);
            bestSplit.Actions.Add((DateTime.Now, $"Added {attendee.CharacterName}."));
            splitDataStore.SaveChanges();

            return new AttendeeResponse()
            {
                Message = $"{attendee.CharacterName} added to Split {bestSplit.SplitNumber}. Send /gu !inv split {bestSplit.SplitNumber}",
                Split = bestSplit,
                Attendee = attendee
            };
        }

        public async Task<Dictionary<int, Split>> Split(RaidEvent raidEvent, int numberOfSplits, Dictionary<string, SplitAttendee> members = null, bool ignoreBuddies = false)
        {
            var splits = new Dictionary<int, Split>();
            if (members == null)
            {
                var registrants = await registrantLoader.GetRegistrants(raidEvent);
                members = registrants.Members;
            }

            performSplit(raidEvent, splits, members, numberOfSplits, ignoreBuddies);
            return splits;
        }

        public Split AddASplit(RaidEvent raidEvent)
        {
            var newSplitCount = raidEvent.Splits.Count() + 1;
            var newSplit = new Split()
            {
                SplitNumber = newSplitCount
            };

            //Remove Boxers
            var boxers = raidEvent.Splits.Values.SelectMany(x => x.Attendees.Values).Where(x => x.IsBoxing).ToList();
            splitInto(newSplit, raidEvent.Splits, boxers, 0m, newSplitCount);

            //Remove Anchors
            var anchors = raidEvent.Splits.Values.SelectMany(x => x.Attendees.Values).Where(x => x.IsAnchor).ToList();
            var anchorWeight = newSplit.Attendees.Values.Where(x => x.IsAnchor).Sum(x => x.Weight);
            splitInto(newSplit, raidEvent.Splits, anchors, anchorWeight, newSplitCount);

            splitIntoForClasses(raidEvent, newSplit, newSplitCount);

            var leader = newSplit.Attendees.Values.FirstOrDefault(x => x.CanBeLeader);
            var looter = newSplit.Attendees.Values.FirstOrDefault(x => x.CanBeMasterLooter);
            var inviter = newSplit.Attendees.Values.FirstOrDefault(x => x.CanBeInviter);

            if (leader == null)
                leader = newSplit.Attendees.Values.FirstOrDefault();

            newSplit.Leader = leader;
            newSplit.MasterLooter = looter ?? leader;
            newSplit.Inviter = inviter ?? leader;

            raidEvent.Splits.TryAdd(newSplit.SplitNumber, newSplit);

            return newSplit;
        }

        private void splitInto(Split newSplit, Dictionary<int, Split> otherSplits, List<SplitAttendee> attendees, decimal currentWeight, int newSplitCount)
        {
            var avgWeight = (attendees.Sum(x => x.Weight) + currentWeight) / newSplitCount;

            foreach (var oldSplit in otherSplits.Values)
            {
                var oldSplitWeight = attendees.Where(x => x.SplitNumber == oldSplit.SplitNumber).Sum(x => x.Weight);
                var weightToRemove = Math.Floor(oldSplitWeight - avgWeight);
                var weightRemoved = 0m;
                var availableAttendeesToRemove = attendees
                    .Where(x => x.SplitNumber == oldSplit.SplitNumber
                        && !string.Equals(x.CharacterName, oldSplit.Leader.CharacterName, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(x.CharacterName, oldSplit.MasterLooter.CharacterName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.RandomOrder).ToList();
                var removedAttendeeCount = 0;

                foreach (var attendee in availableAttendeesToRemove.ToList()) //Yes I want a new list here...
                {
                    if (removedAttendeeCount + 1 >= availableAttendeesToRemove.Count)
                        break; //Lets not take the last one...

                    if (weightRemoved >= weightToRemove)
                        break; //We took enough lets stop...

                    swapAttendee(oldSplit, newSplit, attendee);
                    weightRemoved += attendee.Weight;
                    removedAttendeeCount++;

                    var boxes = oldSplit.Attendees.Values.Where(x => x.UserID == attendee.UserID && !string.Equals(x.CharacterName, attendee.CharacterName, StringComparison.OrdinalIgnoreCase));
                    foreach (var box in boxes)
                    {
                        swapAttendee(oldSplit, newSplit, box);
                        //Not counting box weight towards removal...
                    }
                }
            }
        }

        private void splitIntoForClasses(RaidEvent raidEvent, Split newSplit, int newSplitCount)
        {
            foreach (var gameClass in config.Classes)
            {
                var newSplitWeight = newSplit.Attendees.Values.Where(x => string.Equals(x.ClassName, gameClass.Name, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.Weight);

                var totalWeight = raidEvent.Splits.Values.SelectMany(x => x.Attendees.Values)
                    .Where(x => string.Equals(x.ClassName, gameClass.Name, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.Weight) + newSplitWeight;

                var avgWeight = totalWeight / newSplitCount;

                foreach (var oldSplit in raidEvent.Splits.Values)
                {
                    var oldSplitWeight = oldSplit.Attendees.Values.Where(x => string.Equals(x.ClassName, gameClass.Name, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Weight);
                    var weightToRemove = Math.Floor(oldSplitWeight - avgWeight);

                    if (weightToRemove <= 0)
                        continue;

                    var availableAttendeesToRemove = oldSplit.Attendees.Values
                        .Where(x => 
                            string.Equals(x.ClassName, gameClass.Name, StringComparison.OrdinalIgnoreCase)
                            && !x.IsBox
                            && !x.IsBoxing //We already did boxers
                            && !string.Equals(x.CharacterName, oldSplit.Leader.CharacterName, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(x.CharacterName, oldSplit.MasterLooter.CharacterName, StringComparison.OrdinalIgnoreCase)).ToList();
                    var weightRemoved = 0m;
                    var removedAttendeeCount = 0;

                    foreach (var attendee in availableAttendeesToRemove.ToList()) //Yes I want a new list here...
                    {
                        if (removedAttendeeCount + 1 >= availableAttendeesToRemove.Count)
                            break; //Lets not take the last one...

                        if (weightRemoved >= weightToRemove)
                            break; //We took enough lets stop...

                        swapAttendee(oldSplit, newSplit, attendee);
                        weightRemoved += attendee.Weight;
                        removedAttendeeCount++;

                        var boxes = oldSplit.Attendees.Values.Where(x => x.UserID == attendee.UserID && !string.Equals(x.CharacterName, attendee.CharacterName, StringComparison.OrdinalIgnoreCase));
                        foreach (var box in boxes)
                        {
                            swapAttendee(oldSplit, newSplit, box);
                            //Not counting box weight towards removal...
                        }
                    }
                }
            }
        }

        private void swapAttendee(Split oldSplit, Split newSplit, SplitAttendee attendee)
        {
            if (!oldSplit.Attendees.ContainsKey(attendee.CharacterName))
                return;

            attendee.SplitNumber = null;
            oldSplit.Attendees.Remove(attendee.CharacterName);
            oldSplit.Actions.Add((DateTime.Now, $"Removed {attendee.CharacterName}."));

            attendee.SplitNumber = newSplit.SplitNumber;
            newSplit.Attendees.TryAdd(attendee.CharacterName, attendee);
            newSplit.Actions.Add((DateTime.Now, $"Added {attendee.CharacterName}."));
        }


        private void performSplit(RaidEvent raidEvent, Dictionary<int, Split> splits, Dictionary<string, SplitAttendee> registrants, int numberOfSplits, bool ignoreBuddies)
        {
            randomizeMembers(registrants);
            var members = registrants.Values.Where(x => !x.IsLate).OrderBy(x => x.RandomOrder);

            initializeSplits(splits, registrants.Values, numberOfSplits);
            setBoxers(raidEvent, splits, members, raidEvent.ItemNeeds, ignoreBuddies);
            setAnchors(raidEvent, splits, members, raidEvent.ItemNeeds, ignoreBuddies);
            setTheRest(raidEvent, splits, members, raidEvent.ItemNeeds, ignoreBuddies);

            raidEvent.LateMembers = registrants.Values.Where(x => x.IsLate).ToDictionary(x => x.CharacterName, x => x);
        }

        private void randomizeMembers(Dictionary<string, SplitAttendee> members)
        {
            foreach (var member in members.Values)
            {
                member.RandomOrder = randomizer.GetRandomNumber(0, 10000);
            }
        }
        private List<PreSplit> randomizePreSplits()
        {
            foreach (var preSplit in splitDataStore.PreSplits)
            {
                preSplit.Value.RandomNumber = randomizer.GetRandomNumber(0, 10000);
            }
            var randomOrderedPreSplits = splitDataStore.PreSplits.Values.OrderBy(x => x.RandomNumber);
            return randomOrderedPreSplits.ToList();
        }

        private void initializeSplits(Dictionary<int, Split> splits, IEnumerable<SplitAttendee> splitMembers, int numberOfSplits)
        {
            var preSplits = randomizePreSplits();
            for (int x = 0; x < numberOfSplits; x++)
            {
                var split = new Split()
                {
                    SplitNumber = x + 1,
                };
                splits[split.SplitNumber] = split;

                PreSplit preSplit = null;
                if (x < preSplits.Count)
                    preSplit = preSplits[x];

                SplitAttendee leader = null;
                SplitAttendee looter = null;
                SplitAttendee inviter = null;

                if (preSplit != null)
                {
                    foreach (var character in preSplit.Characters)
                    {
                        var member = splitMembers.FirstOrDefault(x => string.Equals(x.CharacterName, character, StringComparison.OrdinalIgnoreCase)
                            && x.SplitNumber == null);
                        if (member == null)
                            continue;

                        addSplitAttendee(splits, split, member, splitMembers, $"Pre Selected {member.CharacterName} from Pre Split: {preSplit.Name}.");
                    }

                    leader = split.Attendees.Values.FirstOrDefault(x =>
                        string.Equals(x.CharacterName, preSplit.LeaderName, StringComparison.OrdinalIgnoreCase));
                    looter = split.Attendees.Values.FirstOrDefault(x =>
                        string.Equals(x.CharacterName, preSplit.LooterName, StringComparison.OrdinalIgnoreCase));
                    inviter = split.Attendees.Values.FirstOrDefault(x =>
                        string.Equals(x.CharacterName, preSplit.InviterName, StringComparison.OrdinalIgnoreCase));

                    if (leader == null)
                        leader = split.Attendees.Values.FirstOrDefault();

                    split.Leader = leader;
                    split.MasterLooter = looter ?? leader;
                    split.Inviter = inviter ?? leader;
                }

                if (leader == null)
                {
                    splitMembers.FirstOrDefault(x => x.SplitNumber == null && x.CanBeLeader);

                    if (leader == null)
                        leader = splitMembers.FirstOrDefault(x => x.SplitNumber == null && x.Rank == "Officer");

                    if (leader == null)
                        leader = splitMembers.FirstOrDefault(x => x.SplitNumber == null);

                    split.Leader = leader;
                    split.MasterLooter = leader;
                    split.Inviter = leader;

                    addSplitAttendee(splits, split, leader, splitMembers, $"{generateAuditStart(split, leader)} because they are the leader.");
                }
            }
        }

        private void setBoxers(RaidEvent raidEvent, Dictionary<int, Split> splits, IEnumerable<SplitAttendee> members, List<ItemNeed> itemNeeds, bool ignoreBuddies)
        {
            setMeleeBoxOwners(raidEvent, splits, members, itemNeeds, ignoreBuddies);
            setCasterBoxOwners(raidEvent, splits, members, itemNeeds, ignoreBuddies);
            setBoxOwners(raidEvent, splits, members, itemNeeds, ignoreBuddies);
        }

        private void setMeleeBoxOwners(RaidEvent raidEvent, Dictionary<int, Split> splits, IEnumerable<SplitAttendee> members, List<ItemNeed> itemNeeds, bool ignoreBuddies)
        {
            var boxes = members.Where(x => x.IsBox && x.SplitNumber == null);
            foreach (var box in boxes)
            {
                var owner = members.FirstOrDefault(x => x.UserID == box.UserID && !string.Equals(x.CharacterName, box.CharacterName, StringComparison.OrdinalIgnoreCase));
                if (owner == null)
                    continue;

                if (!config.Classes.Any(x => x.IsMelee && string.Equals(x.Name, owner.ClassName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var splitResult = findBestSplitForAttendee(raidEvent, splits, owner, ignoreBuddies);
                var reason = $"{owner.CharacterName} is a melee and boxing - {splitResult.Audit}";
                addSplitAttendee(splits, splitResult.Split, owner, members, reason);
            }
        }

        private void setCasterBoxOwners(RaidEvent raidEvent, Dictionary<int, Split> splits, IEnumerable<SplitAttendee> members, List<ItemNeed> itemNeeds, bool ignoreBuddies)
        {
            var boxes = members.Where(x => x.IsBox && x.SplitNumber == null);
            foreach (var box in boxes)
            {
                var owner = members.FirstOrDefault(x => x.UserID == box.UserID && !string.Equals(x.CharacterName, box.CharacterName, StringComparison.OrdinalIgnoreCase));
                if (owner == null)
                    continue;

                if (!config.Classes.Any(x => x.IsCaster && string.Equals(x.Name, owner.ClassName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var splitResult = findBestSplitForAttendee(raidEvent, splits, owner, ignoreBuddies);
                var reason = $"{owner.CharacterName} is a caster and boxing - {splitResult.Audit}";
                addSplitAttendee(splits, splitResult.Split, owner, members, reason);
            }
        }

        private void setBoxOwners(RaidEvent raidEvent, Dictionary<int, Split> splits, IEnumerable<SplitAttendee> members, List<ItemNeed> itemNeeds, bool ignoreBuddies)
        {
            var boxes = members.Where(x => x.IsBox && x.SplitNumber == null);
            foreach (var box in boxes)
            {
                var owner = members.FirstOrDefault(x => x.UserID == box.UserID && !string.Equals(x.CharacterName, box.CharacterName, StringComparison.OrdinalIgnoreCase));
                if (owner == null)
                    continue;

                var splitResult = findBestSplitForAttendee(raidEvent, splits, owner, ignoreBuddies);
                var reason = $"{owner.CharacterName} is boxing - {splitResult.Audit}";
                addSplitAttendee(splits, splitResult.Split, owner, members, reason);
            }
        }

        private void setAnchors(RaidEvent raidEvent, Dictionary<int, Split> splits, IEnumerable<SplitAttendee> members, List<ItemNeed> itemNeeds, bool ignoreBuddies)
        {
            var availableAnchors = members.Where(x => x.IsAnchor && x.SplitNumber == null);
            var groupedAnchors = availableAnchors.GroupBy(x => x.ClassName);

            foreach (var anchorGroup in groupedAnchors)
            {
                foreach (var anchor in anchorGroup)
                {
                    var splitResult = findBestSplitForAttendee(raidEvent, splits, anchor, ignoreBuddies);
                    addSplitAttendee(splits, splitResult.Split, anchor, members, splitResult.Audit);
                }
            }
        }

        private void setTheRest(RaidEvent raidEvent, Dictionary<int, Split> splits, IEnumerable<SplitAttendee> members, List<ItemNeed> itemNeeds, bool ignoreBuddies)
        {
            var availableMembers = members.Where(x => x.SplitNumber == null);

            foreach (var member in availableMembers)
            {
                var splitResult = findBestSplitForAttendee(raidEvent, splits, member, ignoreBuddies);
                addSplitAttendee(splits, splitResult.Split, member, members, splitResult.Audit);
            }
        }

        private (Split Split, string Audit) findBestSplitForAttendee(RaidEvent raidEvent, Dictionary<int, Split> splits, SplitAttendee attendee, bool ignoreBuddies)
        {
            StringBuilder audit = new StringBuilder();
            Split bestSplit = null;

            //Default Logic
            if (attendee.IsAnchor)
            {
                bestSplit = findLowestCountSplit(splits, attendee.ClassName);
                audit.AppendLine($"{generateAuditStart(bestSplit, attendee)} they are an anchor and this split had the lowest **{attendee.ClassName}s**.");
            }
            else
            {
                bestSplit = findLowestCountSplit(splits, attendee.ClassName);
                audit.AppendLine($"{generateAuditStart(bestSplit, attendee)} this split had the lowest **{attendee.ClassName}s**.");
            }

            //Check if we should swap due to buddies
            if (!string.IsNullOrEmpty(attendee.BuddieGroup) && !ignoreBuddies)
            {
                var buddieSplit = splits.Values.FirstOrDefault(x => x.Attendees.Values.Any(x => x.BuddieGroup == attendee.BuddieGroup));
                audit.AppendLine("Checking Splits For Existing Buddies..");
                if (buddieSplit != null && buddieSplit.SplitNumber != bestSplit.SplitNumber)
                {
                    audit.AppendLine($"Moved {attendee.CharacterName} from Split {bestSplit.SplitNumber} to {buddieSplit.SplitNumber} because their buddies are in this split.");
                    bestSplit = buddieSplit;
                }
                else
                {
                    audit.AppendLine("No Buddies Found...");
                }
            }

            //Check if we should swap due to item needs
            var neededItems = raidEvent.ItemNeeds.Where(x => string.Equals(x.CharacterName, attendee.CharacterName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Item).Distinct().ToList();

            foreach (var neededItem in neededItems)
            {
                audit.AppendLine($"Checking to see if anyone in Split {bestSplit.SplitNumber} needs {neededItem}");

                var charactersWhoNeedTheItem = raidEvent.ItemNeeds.Where(x => x.Item == neededItem).Select(x => x.CharacterName.ToLower()).Distinct().ToList();
                var charactersInSplit = checkForCharacters(bestSplit, charactersWhoNeedTheItem);

                if (charactersInSplit.Any())
                {
                    audit.AppendLine($"{string.Join(",", charactersInSplit)} need {neededItem}");

                    //Look for a split without someone who needs the items, if we can't find one we will just go with what we had.
                    foreach (var split in splits.Values.Where(x => x.SplitNumber != bestSplit.SplitNumber))
                    {
                        audit.AppendLine($"Checking to see if anyone in Split {split.SplitNumber} needs {neededItem}");
                        var matchingCharactersInSplit = checkForCharacters(split, charactersWhoNeedTheItem);

                        if (!matchingCharactersInSplit.Any())
                        {
                            audit.AppendLine($"Moved {attendee.CharacterName} from Split {bestSplit.SplitNumber} to {split.SplitNumber} because they need {neededItem}.");
                            bestSplit = split;
                            break;
                        }
                        else
                        {
                            audit.AppendLine($"{string.Join(",", matchingCharactersInSplit)} need {neededItem}");
                        }
                    }
                }
                else
                {
                    audit.AppendLine($"No one in Split {bestSplit.SplitNumber} needs {neededItem}");
                }
            }

            if (bestSplit == null)
            {
                audit.AppendLine($"No logic found taking the first split...");
                bestSplit = splits.Values.First();
            }

            return (bestSplit, audit.ToString());
        }

        private Split findLowestCountSplit(Dictionary<int, Split> splits, string className)
        {
            var lowestSplit = splits.Values.OrderBy(x => x.ClassWeight(className)).ThenBy(x => x.Attendees.Count()).FirstOrDefault();

            if (lowestSplit == null)
                lowestSplit = splits.Values.First();

            return lowestSplit;
        }

        private string generateAuditStart(Split split, SplitAttendee attendee)
        {
            return $"Adding **{attendee.CharacterName}** to **Split: {split.SplitNumber}** because";
        }
        private List<string> checkForCharacters(Split split, List<string> characters)
        {
            var members = split.Attendees.Values.Select(x => x.CharacterName.ToLower());
            return members.Intersect(characters).ToList();
        }

        private void addSplitAttendee(Dictionary<int, Split> splits, Split split, SplitAttendee attendee, IEnumerable<SplitAttendee> attendees, string reason, bool skipBoxes = false)
        {
            if (attendee.SplitNumber != null)
                return;

            var splitAudit = string.Join(Environment.NewLine, splits.Select(x => getSplitDescription(x.Value)));
            split.Attendees[attendee.CharacterName] = attendee;
            var count = split.Attendees.Count.ToString().PadLeft(4, '0');
            attendee.SplitNumber = split.SplitNumber;
            attendee.SplitReason = $"{count}{Environment.NewLine}{splitAudit}{Environment.NewLine}{reason}";

            if (skipBoxes)
                return;

            var boxes = attendees.Where(x => x.UserID == attendee.UserID && !string.Equals(x.CharacterName, attendee.CharacterName)).ToList();
            foreach (var box in boxes)
            {
                addSplitAttendee(splits, split, box, attendees, $"{generateAuditStart(split, box)} they are **{attendee.CharacterName}'s** box.", true);
                attendee.IsBoxing = true;
            }
        }

        private string getSplitDescription(Split split)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"Split {split.SplitNumber} - ({split.Attendees.Count}) ");

            foreach (var gameClass in config.Classes)
            {
                sb.Append($"{gameClass.Name} - ({calculateClassWeight(split, gameClass.Name)}) ");
            }

            return sb.ToString();
        }

        private decimal calculateClassWeight(Split split, string className)
        {
            var weight = split.Attendees.Values.Where(x => string.Equals(x.ClassName, className, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Weight);
            return weight;
        }


    }

}
