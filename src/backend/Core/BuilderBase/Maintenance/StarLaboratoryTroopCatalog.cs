using System;
using System.Collections.Generic;
using System.Linq;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal static class StarLaboratoryTroopCatalog
    {
        public static readonly StarLabTroopInfo[] StarLabTroops =
        {
            new(1, "raged_barbarian", "Raged Barbarian", new Point(114, 341), new[] { "raged_barbarian", "barbarian", "ragedbarbarian" }),
            new(2, "sneaky_archer", "Sneaky Archer", new Point(114, 449), new[] { "sneaky_archer", "sneakyarcher", "archer" }),
            new(3, "boxer_giant", "Boxer Giant", new Point(213, 341), new[] { "boxer_giant", "boxer_giants", "boxergiant", "giant" }),
            new(4, "beta_minion", "Beta Minion", new Point(213, 449), new[] { "beta_minion", "betaminion", "minion" }),
            new(5, "bomber", "Bomber", new Point(314, 341), new[] { "bomber" }),
            new(6, "baby_dragon", "Baby Dragon", new Point(314, 449), new[] { "baby_dragon", "baby_dragon_builder", "babydragon" }),
            new(7, "cannon_cart", "Cannon Cart", new Point(416, 341), new[] { "cannon_cart", "cannoncart" }),
            new(8, "night_witch", "Night Witch", new Point(416, 449), new[] { "night_witch", "nightwitch" }),
            new(9, "drop_ship", "Drop Ship", new Point(516, 341), new[] { "drop_ship", "dropship" }),
            new(10, "super_pekka", "Super Pekka", new Point(516, 449), new[] { "super_pekka", "power_pekka", "superpekka", "pekka" }),
            new(11, "hog_glider", "Hog Glider", new Point(622, 341), new[] { "hog_glider", "hogglider" }),
            new(12, "electrofire_wizard", "Electrofire Wizard", new Point(622, 449), new[] { "electrofire_wizard", "electro_fire_wizard", "efwizard", "wizard" })
        };

        public static IEnumerable<StarLabTroopInfo> SortStarLabTroops(string configuredTroop)
        {
            string normalized = NormalizeStarLabTroopKey(configuredTroop);
            if (string.IsNullOrEmpty(normalized) || normalized == "auto" || normalized == "any") return StarLabTroops;
            return StarLabTroops.OrderBy(t => t.Key == normalized || t.Aliases.Contains(normalized, StringComparer.OrdinalIgnoreCase) ? 0 : 1).ThenBy(t => t.Index);
        }

        public static string[] BuildStarLaboratoryTroopTemplates(StarLabTroopInfo troop)
        {
            return troop.Aliases
                .Append(troop.Key)
                .SelectMany(alias => new[] { $@"troops\builder_base\{alias}_click", $@"builder_base\starlab\{alias}", $@"builder_base\star_laboratory\{alias}" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static string NormalizeStarLabTroopKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "auto";
            return value.Trim().ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        }
    }

    internal sealed record StarLabTroopInfo(int Index, string Key, string DisplayName, Point DefaultCenter, string[] Aliases);
}
