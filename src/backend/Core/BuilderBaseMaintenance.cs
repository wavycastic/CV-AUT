using System;
using System.Threading;

namespace CvAut
{
    internal sealed record BuilderBaseMaintenanceOptions(
        bool SuggestedUpgrades,
        bool StarLaboratory,
        bool UpgradeBattleMachine,
        bool UpgradeBattleCopter,
        bool PlaceNewBuildings,
        bool IgnoreGoldUpgrades,
        bool IgnoreElixirUpgrades,
        bool IgnoreHallUpgrades,
        bool IgnoreWallUpgrades,
        string StarLaboratoryTroop,
        int VillageIdx,
        bool StarLaboratoryDebugScreenshots);

    /// <summary>
    /// Port an toàn các maintenance task Builder Base từ MBR.
    /// MBR dùng XML image pack riêng; bản C# chỉ thao tác khi template PNG/DAT tương ứng tồn tại
    /// và match được trên màn hình, tránh click mù khi asset chưa được port.
    /// </summary>
    internal sealed partial class BuilderBaseMaintenance
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;
        private readonly string _templatesPath;

        private readonly BuilderBaseMaintenanceUi _ui;
        private readonly SuggestedUpgradePlanner _suggestedPlanner;
        private readonly StarLaboratoryStateStore _starLabStateStore;
        private readonly StarLaboratoryService _starLabService;
        private readonly HeroUpgrader _heroUpgrader;

        private DateTime? _starLabUpgradeFinishUtc => _starLabService.StarLabUpgradeFinishUtc;

        public BuilderBaseMaintenance(IADBHelper adb, IVisionEngine vision, BuilderBaseNavigator navigator, string templatesPath)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
            _templatesPath = templatesPath;

            _ui = new BuilderBaseMaintenanceUi(_adb, _vision, _templatesPath);
            _suggestedPlanner = new SuggestedUpgradePlanner(_adb, _ui);
            _starLabStateStore = new StarLaboratoryStateStore();
            _starLabService = new StarLaboratoryService(_adb, _vision, _ui, _starLabStateStore);
            _heroUpgrader = new HeroUpgrader(_navigator, _ui);
        }

        public BuilderBaseMaintenanceResult Run(BuilderBaseMaintenanceOptions options, BuilderBaseReportSnapshot report, CancellationToken token)
        {
            if (!_navigator.IsOnBuilderBase())
            {
                Console.WriteLine("[BB-MAINT] phase=maintenance status=skip reason=not_on_builder_base");
                return new BuilderBaseMaintenanceResult(0, 0, 0);
            }

            int upgrades = 0, research = 0, hero = 0;
            if (options.SuggestedUpgrades) upgrades = _suggestedPlanner.SuggestedUpgrades(options, report, token);
            if (options.StarLaboratory) research = _starLabService.TryStartStarLaboratoryResearch(options, report, token) ? 1 : 0;
            if (options.UpgradeBattleMachine) hero += _heroUpgrader.TryUpgradeHero("battle_machine", BuilderBaseMaintenanceLayout.BattleMachineTemplates, report, token) ? 1 : 0;
            if (options.UpgradeBattleCopter) hero += _heroUpgrader.TryUpgradeHero("battle_copter", BuilderBaseMaintenanceLayout.BattleCopterTemplates, report, token) ? 1 : 0;
            return new BuilderBaseMaintenanceResult(upgrades, research, hero);
        }
    }

    internal sealed record BuilderBaseMaintenanceResult(int SuggestedUpgrades, int ResearchStarted, int HeroUpgrades);

    internal sealed record BuilderBaseUpgradeTarget(string Name, string[] Templates, bool AllowGold, bool AllowElixir, bool IsHall = false, int RequiredLevel = 0, int[]? CostThousandsByLevel = null);
}
