using System;
using CvAut.Handlers;

namespace CvAut
{
    internal record ScoutedResources(int Gold, int Elixir, int DarkElixir);

    internal enum TargetSelectionLogic
    {
        Total,
        Individual,
        DarkOnly
    }

    internal sealed record FarmingTargetConfig(
        int GoldThreshold,
        int ElixirThreshold,
        int DarkElixirThreshold,
        int TotalResourceThreshold,
        TargetSelectionLogic Logic);

    /// <summary>
    /// Phân hệ Đánh giá Tìm Trận (MatchmakingEngine):
    /// Thực hiện quét tài nguyên nhà đối thủ (OCR) và so sánh với chỉ số cấu hình yêu cầu.
    /// </summary>
    internal class MatchmakingEngine
    {
        public bool ShouldAcceptTarget(ScoutedResources resources, FarmingTargetConfig config, out string reason)
        {
            reason = string.Empty;
            int totalGoldElixir = resources.Gold + resources.Elixir;

            switch (config.Logic)
            {
                case TargetSelectionLogic.DarkOnly:
                    if (resources.DarkElixir >= config.DarkElixirThreshold)
                    {
                        reason = $"dark_elixir_satisfied ({resources.DarkElixir}>={config.DarkElixirThreshold})";
                        return true;
                    }
                    reason = $"dark_elixir_insufficient ({resources.DarkElixir}<{config.DarkElixirThreshold})";
                    return false;

                case TargetSelectionLogic.Individual:
                    bool goldOk = resources.Gold >= config.GoldThreshold;
                    bool elixirOk = resources.Elixir >= config.ElixirThreshold;
                    bool darkOk = resources.DarkElixir >= config.DarkElixirThreshold;

                    if (goldOk && elixirOk && darkOk)
                    {
                        reason = $"individual_all_satisfied (G:{resources.Gold} E:{resources.Elixir} DE:{resources.DarkElixir})";
                        return true;
                    }
                    reason = $"individual_not_met (G:{goldOk} E:{elixirOk} DE:{darkOk})";
                    return false;

                case TargetSelectionLogic.Total:
                default:
                    if (totalGoldElixir >= config.TotalResourceThreshold)
                    {
                        reason = $"total_resource_satisfied ({totalGoldElixir}>={config.TotalResourceThreshold})";
                        return true;
                    }
                    if (config.DarkElixirThreshold > 0 && resources.DarkElixir >= config.DarkElixirThreshold)
                    {
                        reason = $"dark_elixir_override ({resources.DarkElixir}>={config.DarkElixirThreshold})";
                        return true;
                    }
                    reason = $"total_insufficient ({totalGoldElixir}<{config.TotalResourceThreshold})";
                    return false;
            }
        }
    }
}
