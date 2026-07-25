using System;
using System.Collections.Generic;
using System.Linq;

namespace CvAut
{
    internal static class DropOrderPolicy
    {
        public const string DefaultDropOrderSequence = "BattleMachine|BattleCopter|BoxerGiant|DropShip|HogGlider|Bomber|SuperPekka|PowerPekka|BabyDragon|CannonCart|ElectrofireWizard|NightWitch|RagedBarbarian|BetaMinion|SneakyArcher";

        public static IEnumerable<BuilderBaseTroopSlot> OrderSlots(List<BuilderBaseTroopSlot> slots, BuilderBaseBattleOptions options)
        {
            var ordered = new List<BuilderBaseTroopSlot>();

            // Always drop Hero (Battle Machine / Battle Copter) FIRST
            ordered.AddRange(slots.Where(s => IsHeroSlot(s.Name) && !ordered.Contains(s)));

            string sequence = options.UseCustomDropOrder && !string.IsNullOrWhiteSpace(options.DropOrder)
                ? options.DropOrder
                : DefaultDropOrderSequence;
            foreach (string raw in sequence.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                ordered.AddRange(slots.Where(s => NamesMatch(s.Name, raw) && !ordered.Contains(s)));
            }

            ordered.AddRange(slots.Where(s => !ordered.Contains(s)));
            return ordered;
        }

        public static bool IsHeroSlot(string name)
        {
            return NamesMatch(name, "BattleMachine") || NamesMatch(name, "BattleCopter") || NamesMatch(name, "Hero") || NamesMatch(name, "Machine") || NamesMatch(name, "Copter");
        }

        public static bool NamesMatch(string actual, string requested)
        {
            static string Normalize(string s) => s.Replace("_", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.OrdinalIgnoreCase).Replace("-", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            string act = Normalize(actual);
            string req = Normalize(requested);
            return act == req || act.Contains(req) || req.Contains(act);
        }

        public static BuilderBaseBattleOptions DefaultOptions() => new(
            DropOrder: DefaultDropOrderSequence,
            UseCustomDropOrder: false,
            NextTroopDelayMs: 600,
            SameTroopDelayMs: 180,
            HandleBomber: true);
    }
}
