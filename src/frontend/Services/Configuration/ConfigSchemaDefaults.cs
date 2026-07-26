using System;
using System.Text.Json.Nodes;

namespace CvAut.Services.Configuration
{
    /// <summary>
    /// Default value schema for a bot config document, plus migrations from retired key names.
    /// Applying defaults is idempotent: every key is written with ??= so a user value always wins.
    /// </summary>
    internal static class ConfigSchemaDefaults
    {
        private static bool s_loggedLegacyWallConfigMigration;

        internal static void Apply(JsonObject root)
        {
            JsonObject device = ConfigStore.GetOrCreateObject(root, "device_connection");
            device["host"] ??= "127.0.0.1";
            device["port"] ??= 5556;

            JsonObject thresholds = ConfigStore.GetOrCreateObject(root, "farming_thresholds");
            thresholds["gold_threshold"] ??= 650000;
            thresholds["elixir_threshold"] ??= 650000;
            thresholds["dark_elixir_threshold"] ??= 1000;
            thresholds["total_resource_threshold"] ??= 1300000;
            thresholds["target_logic"] ??= "total";

            root["attack"] ??= "Dragon_Attack";
            root["enable_stats"] ??= true;
            root["upgrade_wall"] ??= false;
            MigrateLegacyWallConfig(root);
            root["wall_gold_threshold"] ??= 5000000;
            root["wall_elixir_threshold"] ??= 5000000;
            root["wall_gold_reserve"] ??= 100000;
            root["wall_elixir_reserve"] ??= 0;
            root["wall_batch_limit"] ??= 1;
            root["wall_debug_screenshots"] ??= false;
            root["attack_mode"] ??= "attack";
            root["use_electro_dragon"] ??= false;
            root["request_troops"] ??= false;

            JsonObject smart = ConfigStore.GetOrCreateObject(root, "smart_surrender");
            smart["enabled"] ??= false;
            smart["after_seconds_enabled"] ??= true;
            smart["after_seconds"] ??= 60;
            smart["low_resources_enabled"] ??= false;
            smart["low_resources_threshold"] ??= 100000;

            JsonObject night = ConfigStore.GetOrCreateObject(root, "night_village");
            night["farm_mode"] ??= "auto";
            night["min_cups"] ??= 0;
            night["max_cups"] ??= 5000;
            night["enable_attack"] ??= true;
            night["boost_clock_tower"] ??= false;
            night["upgrade_wall"] ??= false;
            night["army_management"] ??= true;
            night["fill_army"] ??= true;
            night["army_formation"] ??= "auto";
            night["hero_wait_seconds"] ??= 90;
            night["custom_drop_order_enabled"] ??= false;
            night["drop_order"] ??= "BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher";
            night["next_troop_delay_ms"] ??= 600;
            night["same_troop_delay_ms"] ??= 180;
            night["handle_bomber"] ??= true;
            night["suggested_upgrades"] ??= false;
            night["place_new_buildings"] ??= false;
            night["ignore_gold_upgrades"] ??= false;
            night["ignore_elixir_upgrades"] ??= false;
            night["ignore_hall_upgrades"] ??= true;
            night["ignore_wall_upgrades"] ??= true;
            night["star_laboratory"] ??= false;
            night["star_laboratory_troop"] ??= "auto";
            night["upgrade_battle_machine"] ??= false;
            night["upgrade_battle_copter"] ??= false;

            JsonObject clanGames = ConfigStore.GetOrCreateObject(root, "clan_games");
            clanGames["village"] ??= "main_village";
            clanGames["mission_filter"] ??= "resources,walls,stars";
            clanGames["filter_set_name"] ??= "Default";

            JsonObject capital = ConfigStore.GetOrCreateObject(root, "clan_capital");
            capital["enabled"] ??= true;
            capital["attack_mode"] ??= "auto";

            JsonObject advanced = ConfigStore.GetOrCreateObject(root, "advanced");
            advanced["search_delay_ms"] ??= 800;
            advanced["deploy_delay_ms"] ??= 120;
            advanced["return_home_delay_ms"] ??= 1500;
        }

        internal static JsonObject CreateConfig()
        {
            var root = new JsonObject();
            Apply(root);
            return root;
        }

        private static void MigrateLegacyWallConfig(JsonObject root)
        {
            bool migrated = false;
            if (root["wall_upgrade_threshold"] is JsonNode upgradeThreshold)
            {
                root["wall_gold_threshold"] ??= ConfigStore.TryGetInt(upgradeThreshold, 5000000);
                root["wall_elixir_threshold"] ??= ConfigStore.TryGetInt(upgradeThreshold, 5000000);
                root.Remove("wall_upgrade_threshold");
                migrated = true;
            }

            if (root["wall_reserve_threshold"] is JsonNode reserveThreshold)
            {
                root["wall_gold_reserve"] ??= ConfigStore.TryGetInt(reserveThreshold, 100000);
                root["wall_elixir_reserve"] ??= ConfigStore.TryGetInt(reserveThreshold, 0);
                root.Remove("wall_reserve_threshold");
                migrated = true;
            }

            if (migrated && !s_loggedLegacyWallConfigMigration)
            {
                Console.WriteLine("[CONFIG] event=legacy_config_migrated scope=wall");
                s_loggedLegacyWallConfigMigration = true;
            }
        }
    }
}
