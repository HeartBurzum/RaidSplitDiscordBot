using Discord.Commands;
using Discord.WebSocket;
using RaidRobot.Data;
using RaidRobot.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaidRobot.Infrastructure
{
    public partial class Commands
    {
        [Command("UpdateRoster", RunMode = RunMode.Async)]
        public async Task Roster()
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "Update Roster");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = uploadMonitor.MonitorRosterUpload(Context);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("MyCharacterIs", RunMode = RunMode.Async)]
        public async Task MyCharacterIs(string characterName, string characterType)
        {
            try
            {
                await rosterOrchestrator.MapUser(Context.Guild.Id, Context.User.Id, characterName, characterType);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("ClassIs", RunMode = RunMode.Async)]
        public async Task ClassIs(string characterName, [Remainder] string className)
        {
            try
            {
                await rosterOrchestrator.UpdateClass(Context.Guild.Id, characterName, className);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");

            }
        }

        [Command("AddLeaders", RunMode = RunMode.Async)]
        public async Task AddLeaders([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "AddLeaders");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateLeaders(characters, true);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }
        [Command("RemoveLeaders", RunMode = RunMode.Async)]
        public async Task RemoveLeaders([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "RemoveLeaders");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateLeaders(characters, false);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("AddLooters", RunMode = RunMode.Async)]
        public async Task AddLooters([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "AddLooters");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateLooters(characters, true);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("RemoveLooters", RunMode = RunMode.Async)]
        public async Task RemoveLooters([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "RemoveLooters");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateLooters(characters, false);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("AddInviters", RunMode = RunMode.Async)]
        public async Task AddInviters([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "AddInviters");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateInviters(characters, true);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("RemoveInviters", RunMode = RunMode.Async)]
        public async Task RemoveInviters([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "RemoveInviters");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateInviters(characters, false);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("AddAnchors", RunMode = RunMode.Async)]
        public async Task AddAnchors([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "AddAnchors");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateAnchors(characters, true);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }
        [Command("RemoveAnchors", RunMode = RunMode.Async)]
        public async Task RemoveAnchors([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "RemoveAnchors");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.UpdateAnchors(characters, false);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }
        [Command("ViewAnchors", RunMode = RunMode.Async)]
        public async Task ViewAnchors()
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "ViewAnchors");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.GetAnchors();
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }
        [Command("SetBuddies", RunMode = RunMode.Async)]
        public async Task SetBuddies([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "SetBuddies");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                rosterOrchestrator.SetBuddies(characters);
                await ReplyAsync("Buddies Updated");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }
        [Command("RemoveBuddies", RunMode = RunMode.Async)]
        public async Task RemoveBuddies([Remainder] string characters)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "RemoveBuddies");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                rosterOrchestrator.RemoveBuddies(characters);
                await ReplyAsync("Buddies Updated");
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("MapCharacter", RunMode = RunMode.Async)]
        public async Task MapCharacter(string characterName, string characterTypeName, [Remainder] string userName)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "MapCharacter");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                if (!userName.StartsWith("<") && !userName.EndsWith(">")) {
                    await ReplyAsync("Linking to a user must be with a mention");
                    return;
                }

                var user = await findUser(userName);
                if (user == null) {
                    await ReplyAsync($"Error: User {userName} was unable to be found. No changes have been made.");
                    return;
                }

                await rosterOrchestrator.MapUser(Context.Guild.Id, user.Id, characterName, characterTypeName);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("MapUser", RunMode = RunMode.Async)]
        public async Task MapUser(string characterName, string characterTypeName, SocketUser user)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "MapCharacter");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                if (user == null)
                    return;

                await rosterOrchestrator.MapUser(Context.Guild.Id, user.Id, characterName, characterTypeName);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }



        [Command("Unmap", RunMode = RunMode.Async)]
        public async Task Unmap(string characterName)
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "Unmap");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.Unmap(characterName);
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }

        [Command("ViewBuddies", RunMode = RunMode.Async)]
        public async Task ViewBuddies()
        {
            try
            {
                var permissionResult = permissionChecker.CheckManagerPermissions(Context, "ViewBuddies");
                if (!permissionResult.HasPremission)
                {
                    await ReplyAsync(permissionResult.Message);
                    return;
                }

                var result = rosterOrchestrator.GetBuddies();
                await ReplyAsync(result);
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error: {ex.Message}");
            }
        }
    }
}
