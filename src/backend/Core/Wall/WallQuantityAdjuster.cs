using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut
{
    /// <summary>
    /// Grows the wall batch one wall at a time by tapping the +1 button, stopping as soon as the
    /// cost turns red or the cost region stops changing (the cap has been reached).
    /// </summary>
    internal sealed class WallQuantityAdjuster
    {
        private readonly IADBHelper _adb;

        public WallQuantityAdjuster(IADBHelper adb)
        {
            _adb = adb;
        }

        public int AddWallsSafely(string resource, int requestedCount, int batchLimit, CancellationToken token)
        {
            int targetCount = Math.Clamp(requestedCount, 1, Math.Clamp(batchLimit, 1, 10));
            int selectedCount = 1;
            int addMoreTaps = targetCount - 1;
            if (addMoreTaps <= 0) return 1;

            Rect costRoi = WallUiLayout.CostRoiFor(resource);

            for (int i = 0; i < addMoreTaps; i++)
            {
                if (token.IsCancellationRequested) break;

                using Mat? beforeScreenshot = _adb.TakeScreenshot();
                if (beforeScreenshot == null || beforeScreenshot.Empty())
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=before_screenshot_failed");
                    break;
                }

                _adb.Tap(WallUiLayout.AddWallPlusOneButton.X, WallUiLayout.AddWallPlusOneButton.Y);
                if (ThreadingUtil.InterruptibleSleep(WallUiLayout.WallUiAnimationDelayMs, token)) break;

                using Mat? afterScreenshot = _adb.TakeScreenshot();
                if (afterScreenshot == null || afterScreenshot.Empty())
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=after_screenshot_failed");
                    break;
                }

                if (WallCostPolicy.IsUpgradeCostRed(afterScreenshot, resource, out _, out _))
                {
                    _adb.Tap(WallUiLayout.RemoveWallMinusOneButton.X, WallUiLayout.RemoveWallMinusOneButton.Y);
                    ThreadingUtil.InterruptibleSleep(WallUiLayout.WallUiAnimationDelayMs, token);
                    break;
                }

                Rect clamped = ImageUtils.ClampRect(costRoi, afterScreenshot.Width, afterScreenshot.Height);
                if (clamped.Width <= 0 || clamped.Height <= 0)
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=invalid_roi");
                    break;
                }

                using Mat beforeCost = new Mat(beforeScreenshot, clamped);
                using Mat afterCost = new Mat(afterScreenshot, clamped);
                using Mat diff = new Mat();
                Cv2.Absdiff(beforeCost, afterCost, diff);
                Scalar meanDiff = Cv2.Mean(diff);
                double diffVal = meanDiff.Val0 + meanDiff.Val1 + meanDiff.Val2;

                if (diffVal < 3.0)
                {
                    Console.WriteLine($"[WALL] phase=add_wall resource={resource} status=stop reason=cost_region_unchanged diff={diffVal:F2}");
                    break;
                }

                selectedCount++;
            }

            return selectedCount;
        }
    }
}
