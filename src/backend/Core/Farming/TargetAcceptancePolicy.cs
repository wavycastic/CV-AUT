namespace CvAut
{
    /// <summary>
    /// Chịu trách nhiệm duy nhất: quyết định chấp nhận hay bỏ qua một mục tiêu
    /// dựa trên lượng tài nguyên do dò thám và cấu hình ngưỡng của người dùng.
    /// </summary>
    internal static class TargetAcceptancePolicy
    {
        public static bool ShouldAcceptTarget((int Gold, int Elixir, int DarkElixir) resources, FarmingTargetConfig config, out string reason)
        {
            int total = resources.Gold + resources.Elixir;
            bool goldOk = resources.Gold >= config.GoldThreshold;
            bool elixirOk = resources.Elixir >= config.ElixirThreshold;
            bool darkOk = config.DarkElixirThreshold <= 0 || resources.DarkElixir >= config.DarkElixirThreshold;
            bool totalOk = total >= config.TotalResourceThreshold;

            bool accepted = config.Logic switch
            {
                TargetSelectionLogic.And => goldOk && elixirOk && darkOk,
                TargetSelectionLogic.Or => goldOk || elixirOk || darkOk,
                _ => totalOk && darkOk
            };

            reason = config.Logic switch
            {
                TargetSelectionLogic.And => $"and gold_ok={goldOk} elixir_ok={elixirOk} dark_ok={darkOk}",
                TargetSelectionLogic.Or => $"or gold_ok={goldOk} elixir_ok={elixirOk} dark_ok={darkOk}",
                _ => $"total total_ok={totalOk} dark_ok={darkOk}"
            };
            return accepted;
        }
    }
}
