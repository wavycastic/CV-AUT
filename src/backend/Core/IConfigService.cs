using CvAut.Configuration;

namespace CvAut;

// TODO(config-migration): Legacy raw JSON config access removed.
public interface IConfigService : IConfigSnapshotProvider
{
    AutomationConfigSnapshot Snapshot => Current;
    DeviceConnectionConfig DeviceConnection => Current.DeviceConnection;
    RunSessionConfig RunSession => Current.RunSession;

    internal MainVillageConfig GetMainVillageConfig(int villageIndex);
    internal CvAut.TrainingConfig GetTrainingConfig(int villageIndex);
    internal string GetAttackStrategy(int villageIndex);
    internal CvAut.WallUpgradeConfig GetWallUpgradeConfig(int villageIndex);
}
