using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class WallResourceButtonInfo
    {
        public bool Found { get; set; }
        public string Resource { get; set; } = string.Empty;
        public Rect ButtonRect { get; set; }
        public Point TapPoint { get; set; }
        public Rect CostRoi { get; set; }
        public bool CostRoiVerified { get; set; }
        public double CostRoiScore { get; set; }
        public double Score { get; set; }
        public string Method { get; set; } = string.Empty;
        public string SkipReason { get; set; } = string.Empty;
    }

    internal sealed class WallConfirmButtonInfo
    {
        public bool Found { get; set; }
        public Rect ButtonRect { get; set; }
        public Point TapPoint { get; set; }
        public double Score { get; set; }
        public string Method { get; set; } = string.Empty;
        public string SkipReason { get; set; } = string.Empty;
    }

    internal sealed class WallPanelLocalizationResult
    {
        public Rect SearchRoi { get; set; }
        public WallUpgradeResourceMode ResourceMode { get; set; } = WallUpgradeResourceMode.Unknown;
        public WallResourceButtonInfo GoldInfo { get; set; } = new();
        public WallResourceButtonInfo ElixirInfo { get; set; } = new();
        public List<Rect> DetectedButtons { get; set; } = new();
    }

    internal static class WallDynamicLocalizer
    {
        private const double MinimumValidPairScore = 0.60;

        /// <summary>Calculates normalized search ROI for bottom action panel based on image dimensions.</summary>
        public static Rect GetNormalizedActionPanelRoi(int width, int height)
        {
            if (width <= 0 || height <= 0) return default;
            // Calibrated panel region: (277.3, 624.2) -> (1315.1, 779.2) on 1600x900.
            // Padded by ~30px horizontally and ~40px vertically to tolerate screenshot offsets.
            int x = (int)(width * (247.3 / 1600.0));
            int y = (int)(height * (584.2 / 900.0));
            int w = (int)(width * (1097.8 / 1600.0));
            int h = (int)(height * (235.0 / 900.0));
            return ImageUtils.ClampRect(new Rect(x, y, w, h), width, height);
        }

        /// <summary>Calculates normalized confirm dialog ROI based on image dimensions.</summary>
        public static Rect GetNormalizedConfirmDialogRoi(int width, int height)
        {
            if (width <= 0 || height <= 0) return default;
            int x = (int)(width * (800.0 / 1600.0));
            int y = (int)(height * (500.0 / 900.0));
            int w = (int)(width * (480.0 / 1600.0));
            int h = (int)(height * (350.0 / 900.0));
            return ImageUtils.ClampRect(new Rect(x, y, w, h), width, height);
        }

        /// <summary>
        /// Robust, price-independent dynamic detector that evaluates and scores ALL candidate button pairs
        /// in the action panel using contour geometry, IoU NMS, and HSV resource marker verification.
        /// </summary>
        public static WallPanelLocalizationResult LocalizePanelAndButtons(IVisionEngine vision, Mat screenshot)
        {
            var result = new WallPanelLocalizationResult();
            if (screenshot == null || screenshot.Empty())
            {
                result.GoldInfo = new WallResourceButtonInfo { Found = false, Resource = "gold", SkipReason = "screenshot_invalid" };
                result.ElixirInfo = new WallResourceButtonInfo { Found = false, Resource = "elixir", SkipReason = "screenshot_invalid" };
                return result;
            }

            int wScreen = screenshot.Width;
            int hScreen = screenshot.Height;
            Rect searchRoi = GetNormalizedActionPanelRoi(wScreen, hScreen);
            result.SearchRoi = searchRoi;

            if (searchRoi.Width <= 0 || searchRoi.Height <= 0)
            {
                result.GoldInfo = new WallResourceButtonInfo { Found = false, Resource = "gold", SkipReason = "resource_button_pair_not_validated" };
                result.ElixirInfo = new WallResourceButtonInfo { Found = false, Resource = "elixir", SkipReason = "resource_button_pair_not_validated" };
                return result;
            }

            using Mat panelMat = new Mat(screenshot, searchRoi);
            using Mat grayMat = new Mat();
            using Mat blurMat = new Mat();
            using Mat edgesMat = new Mat();

            Cv2.CvtColor(panelMat, grayMat, ColorConversionCodes.BGR2GRAY);
            Cv2.GaussianBlur(grayMat, blurMat, new Size(3, 3), 0);
            Cv2.Canny(blurMat, edgesMat, 30, 100);

            // RETR_LIST keeps the real 154x153 button contour even when a tooltip or
            // highlight creates a larger external contour around it (Level 16 panel).
            Cv2.FindContours(edgesMat, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            int minW = (int)(wScreen * (140.0 / 1600.0));
            int maxW = (int)(wScreen * (165.0 / 1600.0));
            int minH = (int)(hScreen * (140.0 / 900.0));
            int maxH = (int)(hScreen * (170.0 / 900.0));

            int minCenterY = (int)(hScreen * (600.0 / 900.0));
            int maxCenterY = (int)(hScreen * (780.0 / 900.0));
            int minimumActionButtonX = (int)(wScreen * (277.3 / 1600.0));

            var rawCandidates = new List<Rect>();
            foreach (var c in contours)
            {
                Rect br = Cv2.BoundingRect(c);
                int absX = br.X + searchRoi.X;
                int absY = br.Y + searchRoi.Y;
                int absCenterY = absY + br.Height / 2;

                if (absX >= minimumActionButtonX &&
                    absCenterY >= minCenterY && absCenterY <= maxCenterY &&
                    br.Width >= minW && br.Width <= maxW &&
                    br.Height >= minH && br.Height <= maxH)
                {
                    rawCandidates.Add(new Rect(absX, absY, br.Width, br.Height));
                }
            }

            // NMS using IoU / intersection ratio to eliminate nested / overlapping contours.
            // Prefer the calibrated button size so an inner 154x153 contour wins over
            // a nested decoration or a slightly inflated outer edge.
            var nmsCandidates = new List<Rect>();
            foreach (var rc in rawCandidates
                .OrderBy(r => Math.Abs(r.Width - (wScreen * 155.0 / 1600.0)) +
                              Math.Abs(r.Height - (hScreen * 153.0 / 900.0))))
            {
                bool overlap = false;
                foreach (var nc in nmsCandidates)
                {
                    int ix = Math.Max(rc.X, nc.X);
                    int iy = Math.Max(rc.Y, nc.Y);
                    int iw = Math.Min(rc.X + rc.Width, nc.X + nc.Width) - ix;
                    int ih = Math.Min(rc.Y + rc.Height, nc.Y + nc.Height) - iy;
                    if (iw > 0 && ih > 0)
                    {
                        double interArea = iw * ih;
                        double minArea = Math.Min(rc.Width * rc.Height, nc.Width * nc.Height);
                        if (interArea / minArea > 0.20)
                        {
                            overlap = true;
                            break;
                        }
                    }
                }
                if (!overlap)
                {
                    nmsCandidates.Add(rc);
                }
            }

            nmsCandidates.Sort((a, b) => a.X.CompareTo(b.X));
            result.DetectedButtons = nmsCandidates.ToList();

            // Evaluate ALL candidate pairs
            var scoredPairs = new List<(double Score, Rect GoldRect, Rect ElixirRect)>();
            int n = nmsCandidates.Count;

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    Rect c1 = nmsCandidates[i]; // Gold candidate (left)
                    Rect c2 = nmsCandidates[j]; // Elixir candidate (right)

                    // 1. Non-overlap check: IoU between c1 and c2 must be 0
                    if (c1.IntersectsWith(c2)) continue;

                    // 2. Center Y alignment check: dy <= 25px
                    double c1Cy = c1.Y + c1.Height / 2.0;
                    double c2Cy = c2.Y + c2.Height / 2.0;
                    double dCy = Math.Abs(c1Cy - c2Cy);
                    if (dCy > 25) continue;

                    // 3. Dimension similarity check: dw <= 35px, dh <= 35px
                    double dw = Math.Abs(c1.Width - c2.Width);
                    double dh = Math.Abs(c1.Height - c2.Height);
                    if (dw > 35 || dh > 35) continue;

                    // 4. Center-to-center spacing check
                    double c1Cx = c1.X + c1.Width / 2.0;
                    double c2Cx = c2.X + c2.Width / 2.0;
                    double centerDist = c2Cx - c1Cx;
                    double minDist = wScreen * 0.09; // ~144px
                    double maxDist = wScreen * 0.18; // ~288px
                    if (centerDist < minDist || centerDist > maxDist) continue;

                    // 5. Strict Resource Icon Verification inside ButtonRect
                    if (!VerifyButtonResourceIcons(screenshot, c1, c2, out _))
                    {
                        continue;
                    }

                    double geomScore = 1.0 - (dCy / 25.0 * 0.3) - (dw / 35.0 * 0.2) - (dh / 35.0 * 0.2);
                    double totalScore = geomScore + 0.70;

                    scoredPairs.Add((totalScore, c1, c2));
                }
            }

            if (scoredPairs.Count > 0)
            {
                var evaluatedPairs = new List<(double FinalScore, double BaseScore, Rect GoldRect, Rect ElixirRect, WallResourceButtonInfo GoldInfo, WallResourceButtonInfo ElixirInfo)>();

                foreach (var pair in scoredPairs)
                {
                    if (pair.Score < MinimumValidPairScore) continue;

                    var gInfo = BuildResourceButtonInfo("gold", pair.GoldRect, screenshot, "appearance_contour_scored", pair.Score);
                    var eInfo = BuildResourceButtonInfo("elixir", pair.ElixirRect, screenshot, "appearance_contour_scored", pair.Score);

                    bool gRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, gInfo.CostRoi, out int gVal, out double gConf);
                    bool eRead = WallUpdater.TryReadWallUpgradeCost(vision, screenshot, eInfo.CostRoi, out int eVal, out double eConf);

                    double ocrBoost = 0.0;
                    if (gRead && eRead && gVal > 0 && eVal > 0 && WallCostPolicy.ValidateWallCosts(gVal, eVal, 1.15).IsValid)
                    {
                        ocrBoost = 0.50;
                    }
                    else if ((gRead && gVal > 0 && gConf >= 0.70) || (eRead && eVal > 0 && eConf >= 0.70))
                    {
                        ocrBoost = 0.25;
                    }

                    evaluatedPairs.Add((pair.Score + ocrBoost, pair.Score, pair.GoldRect, pair.ElixirRect, gInfo, eInfo));
                }

                if (evaluatedPairs.Count > 0)
                {
                    evaluatedPairs.Sort((a, b) =>
                    {
                        int cmp = b.FinalScore.CompareTo(a.FinalScore);
                        if (cmp != 0) return cmp;
                        return b.GoldRect.X.CompareTo(a.GoldRect.X);
                    });

                    var best = evaluatedPairs[0];
                    WallCostRoiPairLocalization costPair = WallCostRoiLocator.LocalizePair(
                        vision,
                        screenshot,
                        best.GoldInfo.ButtonRect,
                        best.ElixirInfo.ButtonRect);
                    if (costPair.Found)
                    {
                        best.GoldInfo.CostRoi = costPair.GoldRoi;
                        best.ElixirInfo.CostRoi = costPair.ElixirRoi;
                        best.GoldInfo.CostRoiVerified = costPair.OcrVerified;
                        best.ElixirInfo.CostRoiVerified = costPair.OcrVerified;
                        best.GoldInfo.CostRoiScore = costPair.Score;
                        best.ElixirInfo.CostRoiScore = costPair.Score;
                        best.GoldInfo.Method += $"+{costPair.Method}";
                        best.ElixirInfo.Method += $"+{costPair.Method}";
                    }
                    result.ResourceMode = WallUpgradeResourceMode.GoldAndElixir;
                    result.GoldInfo = best.GoldInfo;
                    result.ElixirInfo = best.ElixirInfo;
                    return result;
                }
            }

            // Levels 1 and 2 expose a real Gold upgrade button but no Elixir button.
            // Only enter this mode when exactly one strong Gold marker exists and no
            // candidate can form a verified Gold/Elixir icon pair. This keeps the
            // fallback fail-closed when an Elixir button is present but geometry/OCR failed.
            var goldOnlyCandidates = nmsCandidates
                .Select(r => (Rect: r, MarkerScore: ScoreGoldResourceIcon(screenshot, r)))
                .Where(c => c.MarkerScore > 0)
                .OrderByDescending(c => c.MarkerScore)
                .ThenByDescending(c => c.Rect.X)
                .ToList();

            foreach (var candidate in goldOnlyCandidates)
            {
                bool elixirMarkerPresent = nmsCandidates.Any(other =>
                    other != candidate.Rect &&
                    VerifyButtonResourceIcons(screenshot, candidate.Rect, other, out _));
                if (elixirMarkerPresent)
                {
                    continue;
                }

                WallResourceButtonInfo goldInfo = BuildResourceButtonInfo(
                    "gold",
                    candidate.Rect,
                    screenshot,
                    "appearance_contour_gold_only",
                    candidate.MarkerScore);
                if (!goldInfo.Found)
                {
                    continue;
                }

                WallCostRoiSingleLocalization single = WallCostRoiLocator.LocalizeSingle(
                    vision,
                    screenshot,
                    goldInfo.ButtonRect);
                if (!single.Found || !single.OcrVerified)
                {
                    continue;
                }

                goldInfo.CostRoi = single.CostRoi;
                goldInfo.CostRoiVerified = true;
                goldInfo.CostRoiScore = single.Score;
                goldInfo.Method += $"+{single.Method}";
                result.ResourceMode = WallUpgradeResourceMode.GoldOnly;
                result.GoldInfo = goldInfo;
                result.ElixirInfo = new WallResourceButtonInfo
                {
                    Found = false,
                    Resource = "elixir",
                    SkipReason = "elixir_upgrade_not_available"
                };
                return result;
            }

            if (IsFullyUpgradedTwoButtonPanel(nmsCandidates, wScreen, hScreen))
            {
                result.ResourceMode = WallUpgradeResourceMode.FullyUpgraded;
                result.GoldInfo = new WallResourceButtonInfo { Found = false, Resource = "gold", SkipReason = "wall_fully_upgraded" };
                result.ElixirInfo = new WallResourceButtonInfo { Found = false, Resource = "elixir", SkipReason = "wall_fully_upgraded" };
                return result;
            }

            result.GoldInfo = new WallResourceButtonInfo { Found = false, Resource = "gold", SkipReason = "resource_button_pair_not_validated" };
            result.ElixirInfo = new WallResourceButtonInfo { Found = false, Resource = "elixir", SkipReason = "resource_button_pair_not_validated" };
            return result;
        }

        private static bool IsFullyUpgradedTwoButtonPanel(List<Rect> buttons, int screenWidth, int screenHeight)
        {
            if (buttons.Count != 2) return false;
            Rect left = buttons[0];
            Rect right = buttons[1];
            double centerSpacing = (right.X + right.Width / 2.0) - (left.X + left.Width / 2.0);
            double pairCenter = ((left.X + left.Width / 2.0) + (right.X + right.Width / 2.0)) / 2.0;
            return Math.Abs((left.Y + left.Height / 2.0) - (right.Y + right.Height / 2.0)) <= screenHeight * (12.0 / 900.0) &&
                   centerSpacing >= screenWidth * (150.0 / 1600.0) && centerSpacing <= screenWidth * (205.0 / 1600.0) &&
                   Math.Abs(pairCenter - screenWidth / 2.0) <= screenWidth * (110.0 / 1600.0);
        }

        public static WallResourceButtonInfo LocalizeResourceButton(IVisionEngine vision, Mat screenshot, string resource)
        {
            var res = LocalizePanelAndButtons(vision, screenshot);
            return resource.Equals("gold", StringComparison.OrdinalIgnoreCase) ? res.GoldInfo : res.ElixirInfo;
        }

        private static WallResourceButtonInfo BuildResourceButtonInfo(string resource, Rect buttonRect, Mat screenshot, string method, double score)
        {
            int width = screenshot.Width;
            int height = screenshot.Height;
            double cx = buttonRect.X + buttonRect.Width / 2.0;

            Point tapPoint = new Point((int)(cx - 2), buttonRect.Y + (int)(buttonRect.Height * 0.54));

            Rect costRoi = DetermineDynamicCostRoi(screenshot, buttonRect);
            costRoi = ImageUtils.ClampRect(costRoi, width, height);
            Rect clampedBtnRect = ImageUtils.ClampRect(buttonRect, width, height);

            if (!clampedBtnRect.Contains(tapPoint))
            {
                return new WallResourceButtonInfo { Found = false, Resource = resource, SkipReason = "tap_point_outside_button" };
            }
            if (costRoi.Width < 50 || costRoi.Height < 15)
            {
                return new WallResourceButtonInfo { Found = false, Resource = resource, SkipReason = "cost_roi_not_localized" };
            }

            return new WallResourceButtonInfo
            {
                Found = true,
                Resource = resource,
                ButtonRect = clampedBtnRect,
                TapPoint = tapPoint,
                CostRoi = costRoi,
                Score = score,
                Method = method
            };
        }

        private static bool VerifyButtonResourceIcons(Mat screenshot, Rect goldRect, Rect elixirRect, out string reason)
        {
            reason = "ok";
            int w = screenshot.Width;
            int h = screenshot.Height;

            Rect clampedGold = ImageUtils.ClampRect(goldRect, w, h);
            Rect clampedElixir = ImageUtils.ClampRect(elixirRect, w, h);

            if (clampedGold.Width <= 0 || clampedGold.Height <= 0 || clampedElixir.Width <= 0 || clampedElixir.Height <= 0)
            {
                reason = "resource_icons_out_of_bounds";
                return false;
            }

            // The resource badge is in the button's upper-right corner. Keeping this
            // crop narrow prevents ordinary yellow UI decorations and the Select text
            // from satisfying the resource-marker check.
            Rect gIconRoi = new Rect(
                clampedGold.X + (int)(clampedGold.Width * 0.78),
                clampedGold.Y + (int)(clampedGold.Height * 0.02),
                Math.Max(1, (int)(clampedGold.Width * 0.22)),
                Math.Max(1, (int)(clampedGold.Height * 0.28))
            );
            Rect eIconRoi = new Rect(
                clampedElixir.X + (int)(clampedElixir.Width * 0.78),
                clampedElixir.Y + (int)(clampedElixir.Height * 0.02),
                Math.Max(1, (int)(clampedElixir.Width * 0.22)),
                Math.Max(1, (int)(clampedElixir.Height * 0.28))
            );

            gIconRoi = ImageUtils.ClampRect(gIconRoi, w, h);
            eIconRoi = ImageUtils.ClampRect(eIconRoi, w, h);

            using Mat goldCrop = new Mat(screenshot, gIconRoi);
            using Mat elixirCrop = new Mat(screenshot, eIconRoi);

            using Mat goldHsv = new Mat();
            using Mat elixirHsv = new Mat();
            Cv2.CvtColor(goldCrop, goldHsv, ColorConversionCodes.BGR2HSV);
            Cv2.CvtColor(elixirCrop, elixirHsv, ColorConversionCodes.BGR2HSV);

            // Gold coin marker check on Gold button icon crop
            using Mat yellowMask = new Mat();
            Cv2.InRange(goldHsv, new Scalar(12, 60, 80), new Scalar(38, 255, 255), yellowMask);
            int goldYellowPixels = Cv2.CountNonZero(yellowMask);
            Cv2.FindContours(yellowMask, out Point[][] goldContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            double maxGoldContourArea = 0;
            foreach (var cnt in goldContours)
            {
                double area = Cv2.ContourArea(cnt);
                if (area > maxGoldContourArea) maxGoldContourArea = area;
            }

            // Elixir droplet marker check on Elixir button icon crop
            using Mat elixirMagHsv = new Mat();
            Cv2.InRange(elixirHsv, new Scalar(130, 40, 60), new Scalar(175, 255, 255), elixirMagHsv);

            Mat[] splitBgr = Cv2.Split(elixirCrop);
            using Mat b = splitBgr[0];
            using Mat g = splitBgr[1];
            using Mat r = splitBgr[2];

            using Mat rgbMask = new Mat();
            using Mat diffRG = new Mat();
            using Mat diffBG = new Mat();
            Cv2.Subtract(r, g, diffRG);
            Cv2.Subtract(b, g, diffBG);

            using Mat condR = new Mat();
            using Mat condB = new Mat();
            using Mat condG = new Mat();
            using Mat condDiffRG = new Mat();
            using Mat condDiffBG = new Mat();
            using Mat condStep1 = new Mat();
            using Mat condStep2 = new Mat();
            using Mat condStep3 = new Mat();

            Cv2.Compare(r, 120, condR, CmpType.GT);
            Cv2.Compare(b, 110, condB, CmpType.GT);
            Cv2.Compare(g, 140, condG, CmpType.LT);
            Cv2.Compare(diffRG, 20, condDiffRG, CmpType.GT);
            Cv2.Compare(diffBG, 15, condDiffBG, CmpType.GT);

            Cv2.BitwiseAnd(condR, condB, condStep1);
            Cv2.BitwiseAnd(condStep1, condG, condStep2);
            Cv2.BitwiseAnd(condStep2, condDiffRG, condStep3);
            Cv2.BitwiseAnd(condStep3, condDiffBG, rgbMask);

            foreach (var m in splitBgr) m.Dispose();
            condR.Dispose(); condB.Dispose(); condG.Dispose(); condDiffRG.Dispose(); condDiffBG.Dispose();
            condStep1.Dispose(); condStep2.Dispose(); condStep3.Dispose(); diffRG.Dispose(); diffBG.Dispose();

            using Mat elixirCombined = new Mat();
            Cv2.BitwiseOr(elixirMagHsv, rgbMask, elixirCombined);
            int elixirPixels = Cv2.CountNonZero(elixirCombined);

            Cv2.FindContours(elixirCombined, out Point[][] elixirContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            double maxElixirContourArea = 0;
            foreach (var cnt in elixirContours)
            {
                double area = Cv2.ContourArea(cnt);
                if (area > maxElixirContourArea) maxElixirContourArea = area;
            }

            bool goldValid = goldYellowPixels >= 250 && maxGoldContourArea >= 150;
            bool elixirValid = elixirPixels >= 150 && maxElixirContourArea >= 120 && maxElixirContourArea <= 650;

            if (!goldValid)
            {
                reason = "gold_resource_icon_missing";
                return false;
            }
            if (!elixirValid)
            {
                reason = "elixir_resource_icon_missing";
                return false;
            }

            return true;
        }

        private static double ScoreGoldResourceIcon(Mat screenshot, Rect buttonRect)
        {
            Rect button = ImageUtils.ClampRect(buttonRect, screenshot.Width, screenshot.Height);
            if (button.Width <= 0 || button.Height <= 0) return 0;
            Rect iconRoi = ImageUtils.ClampRect(new Rect(
                button.X + (int)(button.Width * 0.78),
                button.Y + (int)(button.Height * 0.02),
                Math.Max(1, (int)(button.Width * 0.22)),
                Math.Max(1, (int)(button.Height * 0.28))), screenshot.Width, screenshot.Height);
            if (iconRoi.Width <= 0 || iconRoi.Height <= 0) return 0;

            using Mat crop = new(screenshot, iconRoi);
            using Mat hsv = new();
            using Mat mask = new();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, new Scalar(12, 60, 80), new Scalar(38, 255, 255), mask);
            int pixels = Cv2.CountNonZero(mask);
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            double area = contours.Length == 0 ? 0 : contours.Max(c => Cv2.ContourArea(c));
            if (pixels < 250 || area < 150 || area > 650) return 0;
            return pixels + area;
        }

        private static Rect DetermineDynamicCostRoi(Mat screenshot, Rect buttonRect)
        {
            int w = screenshot.Width;
            int h = screenshot.Height;

            int costW = (int)(w * (135.0 / 1600.0));
            int costH = (int)(h * (35.0 / 900.0));

            // Candidate A: Standard offset (+10)
            int topX1 = buttonRect.X + 10;
            int topY1 = buttonRect.Height <= (int)(h * (165.0 / 900.0))
                ? buttonRect.Y + 6
                : buttonRect.Y + (int)(h * (28.0 / 900.0));
            Rect topRoi1 = ImageUtils.ClampRect(new Rect(topX1, topY1, costW, costH), w, h);

            // Candidate A2: Wide offset (+30) for wide buttons e.g. Level 16 panel
            int topX2 = buttonRect.X + (int)(w * (30.0 / 1600.0));
            int topY2 = buttonRect.Y + (int)(h * (12.0 / 900.0));
            Rect topRoi2 = ImageUtils.ClampRect(new Rect(topX2, topY2, costW, costH), w, h);

            // Candidate B: Bottom offset (layout variant e.g. Screenshot_2026.08.02_01.13.37.761.png)
            int bottomX = buttonRect.X + 10;
            int bottomY = buttonRect.Y + (int)(h * (90.0 / 900.0));
            Rect bottomRoi = ImageUtils.ClampRect(new Rect(bottomX, bottomY, costW, costH), w, h);

            // Evaluate digit contour count in candidates
            int d1 = CountDigitContoursInRoi(screenshot, topRoi1);
            int d2 = CountDigitContoursInRoi(screenshot, topRoi2);
            int dBottom = CountDigitContoursInRoi(screenshot, bottomRoi);

            double r1 = GetHighThresholdPixelRatio(screenshot, topRoi1);
            double r2 = GetHighThresholdPixelRatio(screenshot, topRoi2);
            double rBottom = GetHighThresholdPixelRatio(screenshot, bottomRoi);

            if (dBottom > d1 && dBottom > d2 && dBottom >= 3) return bottomRoi;
            if (d2 > d1 && d2 >= 4) return topRoi2;
            if (d1 >= 2) return topRoi1;

            if (rBottom > r1 && rBottom > r2 && rBottom > 0.25) return bottomRoi;
            if (r2 > r1 && r2 > 0.15) return topRoi2;

            return topRoi1;
        }

        private static int CountDigitContoursInRoi(Mat screenshot, Rect roi)
        {
            if (roi.Width <= 0 || roi.Height <= 0) return 0;
            using Mat crop = new Mat(screenshot, roi);
            using Mat gray = new Mat();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);

            int maxCount = 0;
            foreach (bool inv in new[] { false, true })
            {
                using Mat binary = new Mat();
                Cv2.Threshold(gray, binary, 220, 255, inv ? ThresholdTypes.BinaryInv : ThresholdTypes.Binary);
                Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
                int count = 0;
                foreach (var c in contours)
                {
                    Rect r = Cv2.BoundingRect(c);
                    if (r.Height >= 8 && r.Height <= (int)(roi.Height * 0.85) && r.Width > 1 && r.Width < 35 && r.X > 1 && r.X + r.Width < roi.Width - 1)
                    {
                        count++;
                    }
                }
                if (count > maxCount) maxCount = count;
            }
            return maxCount;
        }

        internal static double GetHighThresholdPixelRatio(Mat screenshot, Rect roi)
        {
            if (roi.Width <= 0 || roi.Height <= 0) return 0;
            using Mat crop = new Mat(screenshot, roi);
            using Mat gray = new Mat();
            using Mat binary = new Mat();
            Cv2.CvtColor(crop, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, binary, WallUiLayout.WallCostOcrThreshold, 255, ThresholdTypes.Binary);
            int nonZero = Cv2.CountNonZero(binary);
            return (double)nonZero / (roi.Width * roi.Height);
        }

        /// <summary>
        /// Dynamically localizes and verifies the confirm button inside the confirm dialog.
        /// </summary>
        public static WallConfirmButtonInfo LocalizeConfirmButton(IVisionEngine vision, Mat screenshot, bool isMulti, WallResourceButtonInfo? resourceBtnInfo = null)
        {
            if (screenshot == null || screenshot.Empty())
            {
                return new WallConfirmButtonInfo { Found = false, SkipReason = "screenshot_invalid" };
            }

            WallConfirmDialogInfo structural = WallConfirmDialogInspector.Inspect(screenshot);
            WallConfirmationKind expectedKind = isMulti
                ? WallConfirmationKind.MultiCancelOkay
                : WallConfirmationKind.SingleConfirm;
            if (structural.Found)
            {
                if (structural.Kind != expectedKind)
                {
                    return new WallConfirmButtonInfo { Found = false, SkipReason = "confirmation_kind_mismatch" };
                }
                return new WallConfirmButtonInfo
                {
                    Found = true,
                    ButtonRect = structural.ConfirmButton,
                    TapPoint = structural.ConfirmPoint,
                    Score = structural.Score,
                    Method = $"structural_{structural.Kind}"
                };
            }

            Rect dialogRoi = GetNormalizedConfirmDialogRoi(screenshot.Width, screenshot.Height);
            if (dialogRoi.Width <= 0 || dialogRoi.Height <= 0)
            {
                return new WallConfirmButtonInfo { Found = false, SkipReason = "confirm_button_not_localized" };
            }

            string[] confirmTemplates = isMulti
                ? new[] { "ui/okay_n.png", "ui/okay.png", "ui/start_button.png" }
                : new[] { "ui/okay.png", "ui/okay_n.png", "ui/okay_df.png", "resources/gold_confirm_n.png", "resources/elixir_confirm_n.png" };

            foreach (var template in confirmTemplates)
            {
                Point? match = vision.FindElement(screenshot, template, 0.75, dialogRoi, out double score);
                if (match.HasValue)
                {
                    Point pt = match.Value;
                    Rect btnRect = new Rect(pt.X - 50, pt.Y - 25, 100, 50);
                    btnRect = ImageUtils.ClampRect(btnRect, screenshot.Width, screenshot.Height);

                    if (btnRect.Contains(pt))
                    {
                        return new WallConfirmButtonInfo
                        {
                            Found = true,
                            ButtonRect = btnRect,
                            TapPoint = pt,
                            Score = score,
                            Method = $"template_{template}"
                        };
                    }
                }
            }

            // Fail closed: never derive a confirmation tap from the resource button or brightness.
            _ = resourceBtnInfo;

            return new WallConfirmButtonInfo
            {
                Found = false,
                SkipReason = "confirm_button_not_localized"
            };
        }
    }
}
