using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Các tùy chọn cấu hình cho một trận đánh Builder Base.
    /// </summary>
    internal sealed record BuilderBaseBattleOptions(
        string DropOrder,
        bool UseCustomDropOrder,
        int NextTroopDelayMs,
        int SameTroopDelayMs,
        bool HandleBomber);

    /// <summary>
    /// Kết quả thu được sau khi kết thúc trận đánh Builder Base.
    /// </summary>
    internal sealed record BuilderBaseBattleResult(
        bool ReturnedHome,
        int Damage,
        int Stars,
        bool Stage2Entered,
        bool AttackExecuted = true);

    internal sealed record BuilderBaseDeploymentResult(bool Succeeded, int DeployedCount, string Reason)
    {
        public static BuilderBaseDeploymentResult Failed(string reason) => new(false, 0, reason);
    }

    /// <summary>
    /// Thông tin slot quân trên thanh rải quân Builder Base.
    /// </summary>
    internal sealed record BuilderBaseTroopSlot(string Name, Point Center, int Index, int Count, double Score);
}
