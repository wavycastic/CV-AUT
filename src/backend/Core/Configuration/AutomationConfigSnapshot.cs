using System;
using System.Text.Json;

namespace CvAut.Configuration;

public enum VillagePlayMode
{
    MainVillage,
    NightVillage,
    ClanGames,
    ClanCapital
}

public sealed record RunSessionConfig(
    VillagePlayMode PlayMode,
    bool StopAfterBattlesEnabled,
    int StopAfterBattles,
    bool StopAfterMinutesEnabled,
    int StopAfterMinutes);

public sealed record AutomationConfigSnapshot(
    DeviceConnectionConfig DeviceConnection,
    RunSessionConfig RunSession);

internal static class AutomationConfigSnapshotReader
{
    public static AutomationConfigSnapshot Read(JsonElement root)
        => new(
            DeviceConnection: DeviceConnectionConfigReader.Read(root),
            RunSession: ReadRunSession(root));

    private static RunSessionConfig ReadRunSession(JsonElement root)
    {
        JsonElement session = ConfigManager.GetObjectOrDefault(root, "run_session");
        string playMode = ConfigManager.GetStringOrDefault(
            session,
            "play_mode",
            ConfigManager.GetStringOrDefault(root, "play_mode", "main_village"));

        return new RunSessionConfig(
            PlayMode: ParsePlayMode(playMode),
            StopAfterBattlesEnabled: ConfigManager.GetBoolOrDefault(
                session,
                "stop_after_battles_enabled",
                false),
            StopAfterBattles: Math.Max(
                0,
                ConfigManager.GetIntOrDefault(session, "stop_after_battles", 0)),
            StopAfterMinutesEnabled: ConfigManager.GetBoolOrDefault(
                session,
                "stop_after_minutes_enabled",
                false),
            StopAfterMinutes: Math.Max(
                0,
                ConfigManager.GetIntOrDefault(session, "stop_after_minutes", 0)));
    }

    private static VillagePlayMode ParsePlayMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "night_village" => VillagePlayMode.NightVillage,
            "clan_games" => VillagePlayMode.ClanGames,
            "clan_capital" => VillagePlayMode.ClanCapital,
            _ => VillagePlayMode.MainVillage
        };
}
