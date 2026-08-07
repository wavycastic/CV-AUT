using System;
using System.Collections.Generic;
using System.Linq;

namespace CvAut
{
    internal sealed record WallQuantityPlanStep(
        bool CanExecute,
        WallQuantityControlRole Role,
        int Delta,
        int ExpectedCount,
        string Reason);

    /// <summary>Chooses the largest verified quantity step that cannot overshoot the target.</summary>
    internal static class WallQuantityPlanner
    {
        internal const int HardSafetyMaximum = 255;

        public static int ClampTarget(int requestedCount, int batchLimit)
        {
            int safeLimit = Math.Clamp(batchLimit, 1, HardSafetyMaximum);
            return Math.Clamp(requestedCount, 1, safeLimit);
        }

        public static WallQuantityPlanStep PlanNext(
            int currentCount,
            int targetCount,
            IReadOnlyList<WallQuantityControlInfo> controls)
        {
            if (currentCount is < 1 or > HardSafetyMaximum)
                return Stop("current_count_out_of_range", currentCount);
            targetCount = Math.Clamp(targetCount, 1, HardSafetyMaximum);
            if (currentCount >= targetCount)
                return Stop("target_reached", currentCount);

            int remaining = targetCount - currentCount;
            WallQuantityControlInfo? addTen = controls.SingleOrDefault(c => c.Role == WallQuantityControlRole.AddTen);
            if (remaining >= 10 && addTen is { Found: true, Available: true })
                return new(true, WallQuantityControlRole.AddTen, 10, checked(currentCount + 10), "add_ten_preferred");

            WallQuantityControlInfo? addOne = controls.SingleOrDefault(c => c.Role == WallQuantityControlRole.AddOne);
            if (addOne is { Found: true, Available: true })
                return new(true, WallQuantityControlRole.AddOne, 1, checked(currentCount + 1),
                    remaining >= 10 ? "add_ten_unavailable_fallback_add_one" : "add_one_remainder");

            return Stop(remaining >= 10 ? "no_available_add_control" : "add_one_unavailable_for_remainder", currentCount);
        }

        private static WallQuantityPlanStep Stop(string reason, int count)
            => new(false, WallQuantityControlRole.AddOne, 0, count, reason);
    }
}
