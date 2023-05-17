using RaidRobot.Data;
using RaidRobot.Data.Entities;
using System.Threading.Tasks;

namespace RaidRobot.Logic
{
    public interface IRosterOrchestrator
    {
        Task<GuildMember> AutoMapCharacter(RaidEvent raidEvent, ulong userID, CharacterType characterType);
        GuildMember FindMember(ulong userID, CharacterType characterType);
        Task<GuildMember> ValidateUser(RaidEvent raidEvent, ulong userID, CharacterType characterType);
        Task<GuildMember> MapUser(UnknownMessage message, string characterName);
        Task<GuildMember> MapUser(ulong guildID, ulong userID, string characterName, string characterTypeName);
        Task<GuildMember> MapUser(ulong guildID, ulong userID, string characterName, CharacterType characterType);
        Task UpdateClass(UnknownMessage message, string className);
        Task UpdateClass(ulong guildID, string characterName, string className);
        string UpdateLeaders(string characterNames, bool value);
        string UpdateLooters(string characterNames, bool value);
        string UpdateInviters(string characterNames, bool value);
        string UpdateAnchors(string characterNames, bool value);
        string GetAnchors();
        void SetBuddies(string characterNames);
        void RemoveBuddies(string characterNames);
        string GetBuddies();
        string Unmap(string characterName);
    }
}