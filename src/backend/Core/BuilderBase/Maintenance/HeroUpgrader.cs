using System;
using System.Threading;

namespace CvAut
{
    internal sealed class HeroUpgrader
    {
        private readonly BuilderBaseNavigator _navigator;
        private readonly BuilderBaseMaintenanceUi _ui;

        public HeroUpgrader(BuilderBaseNavigator navigator, BuilderBaseMaintenanceUi ui)
        {
            _navigator = navigator;
            _ui = ui;
        }

        public bool TryUpgradeHero(string name, string[] templates, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            if (report.FreeBuilders == 0 || report.Elixir <= 0) return false;

            bool isBattleCopter = name.Equals("battle_copter", StringComparison.OrdinalIgnoreCase);
            if (isBattleCopter)
            {
                if (!_navigator.SwitchToOttoVillage(token)) return false;
            }

            bool found = _ui.TapFirstExisting(templates, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.HeroMapRoi, token, $"hero_{name}_open");
            bool upgraded = false;

            if (found)
            {
                BuilderBaseMaintenanceUi.Sleep(900, token);
                upgraded = _ui.TapFirstExisting(BuilderBaseMaintenanceLayout.UpgradeActionTemplates, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.ActionButtonRoi, token, $"hero_{name}_upgrade")
                    && _ui.TapFirstExisting(BuilderBaseMaintenanceLayout.UpgradeConfirmElixir, BuilderBaseMaintenanceLayout.ButtonThreshold, BuilderBaseMaintenanceLayout.ActionButtonRoi, token, $"hero_{name}_confirm");
                _ui.SafeDismiss(token);
            }

            if (isBattleCopter)
            {
                _navigator.SwitchToBuilderBaseStage1(token);
            }

            Console.WriteLine($"[BB-MAINT] phase=hero_upgrade hero={name} status={(upgraded ? "success" : "skip")}");
            return upgraded;
        }
    }
}
