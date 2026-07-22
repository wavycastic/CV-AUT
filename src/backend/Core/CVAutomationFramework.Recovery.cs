using System;
using System.Threading;
using OpenCvSharp;

namespace CvAut
{
    internal partial class CVAutomationFramework
    {
        private bool RecoverIfConnectionPopup(string warningMessage)
        {
            return HandleBlockingConnectionPopup(warningMessage);
        }

        private bool HandleBlockingConnectionPopup(string warningMessage)
        {
            if (_handlingConnectionPopup || !ConnectionPopupVisible(out string matchInfo))
            {
                return false;
            }

            _handlingConnectionPopup = true;
            try
            {
                string details = warningMessage.Replace("[WARN] ", "").Replace(" → ", "_").ToLower();
                Console.WriteLine($"[FSM-CS WARNING] phase=connection_check status=fail action=recover reason=\"connection_lost\" details=\"{details} ({matchInfo})\"");
                BootRecovery();
                return true;
            }
            finally
            {
                _handlingConnectionPopup = false;
            }
        }

        private bool ConnectionPopupVisible(out string matchInfo, bool allowDialogShapeFallback = true)
        {
            matchInfo = "none";
            if (_disableDialogShapeFallback)
            {
                allowDialogShapeFallback = false;
            }

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                return false;
            }

            foreach (string templateName in AutomationThresholds.ConnectionPopupTemplates)
            {
                bool isLegacyConnectionTemplate = templateName.EndsWith("Client_error!.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("Connection_lost.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("Another_device.png", StringComparison.OrdinalIgnoreCase)
                    || templateName.EndsWith("rate_coc.png", StringComparison.OrdinalIgnoreCase);
                double threshold = templateName.EndsWith("conn.png", StringComparison.OrdinalIgnoreCase)
                    ? AutomationThresholds.ConnIconPopupThreshold
                    : isLegacyConnectionTemplate ? AutomationThresholds.LegacyConnectionPopupThreshold : AutomationThresholds.ConnectionPopupThreshold;
                Rect? popupRoi = isLegacyConnectionTemplate ? null : AutomationRoiConstants.ConnectionPopupRoi;

                bool matched = isLegacyConnectionTemplate
                    ? TryMatchTemplateMultiScale(screenshot, templateName, popupRoi, threshold, out Point center, out double score)
                    : TryMatchTemplate(screenshot, templateName, popupRoi, threshold, out center, out score);
                if (!matched)
                {
                    continue;
                }

                matchInfo = $"{templateName} score={score:F2} center=({center.X},{center.Y})";
                Console.WriteLine($"[FSM-CS WARNING] phase=connection_check status=fail reason=\"popup_detected\" template=\"{templateName}\"");
                return true;
            }

            if (allowDialogShapeFallback && TryDetectReloadDialogShape(screenshot, out Rect dialogRect))
            {
                matchInfo = $"reload_dialog_shape rect=({dialogRect.X},{dialogRect.Y},{dialogRect.Width},{dialogRect.Height})";
                Console.WriteLine("[FSM-CS WARNING] phase=connection_check status=fail reason=\"popup_detected\" template=\"reload_dialog_shape\"");
                return true;
            }

            return false;
        }

        private bool HandleTreasureHuntIfPresent(bool verboseNotFound = true)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty())
            {
                Console.WriteLine("[FSM-CS WARNING] phase=treasure_hunt status=fail reason=screenshot_failed");
                return false;
            }

            return HandleTreasureHuntIfPresent(screenshot, verboseNotFound);
        }

        private bool HandleTreasureHuntIfPresent(Mat screenshot, bool verboseNotFound = true)
        {
            if (!TryFindTreasureHuntPopup(screenshot, out Point center, out double score))
            {
                if (verboseNotFound)
                {
                    Console.WriteLine("[FSM-CS] phase=treasure_hunt status=skip reason=popup_not_found");
                }

                return false;
            }

            Console.WriteLine("[FSM-CS] phase=treasure_hunt status=pending details=\"popup_detected\"");
            for (int i = 1; i <= 5; i++)
            {
                _adb.Tap(center.X, center.Y);
                Thread.Sleep(350);
            }

            Thread.Sleep(1200);
            return true;
        }

        private bool TryFindTreasureHuntPopup(Mat screenshot, out Point center, out double score)
        {
            if (TryMatchTemplate(screenshot, @"ui\treasure_hunt.png", AutomationRoiConstants.TreasureHuntRoi, AutomationThresholds.TreasureHuntThreshold, out center, out score)
                || TryMatchTemplate(screenshot, @"event\treasure_hunt.png", AutomationRoiConstants.TreasureHuntRoi, AutomationThresholds.TreasureHuntThreshold, out center, out score))
            {
                return true;
            }

            double bestScore = score;
            Point bestCenter = center;

            if (TryMatchTemplateRegionMultiScale(
                    screenshot,
                    @"ui\treasure_hunt.png",
                    AutomationRoiConstants.TreasureHuntRoi,
                    AutomationRoiConstants.TreasureHuntChestTemplateRoi,
                    AutomationThresholds.TreasureHuntMarkerThreshold,
                    out Point chestCenter,
                    out double chestScore))
            {
                center = chestCenter;
                score = chestScore;
                return true;
            }

            if (chestScore > bestScore)
            {
                bestScore = chestScore;
                bestCenter = chestCenter;
            }

            if (TryMatchTemplateRegionMultiScale(
                    screenshot,
                    @"ui\treasure_hunt.png",
                    AutomationRoiConstants.TreasureHuntRoi,
                    AutomationRoiConstants.TreasureHuntTextTemplateRoi,
                    AutomationThresholds.TreasureHuntMarkerThreshold,
                    out Point textCenter,
                    out double textScore))
            {
                center = textCenter;
                score = textScore;
                return true;
            }

            if (textScore > bestScore)
            {
                bestScore = textScore;
                bestCenter = textCenter;
            }

            center = bestCenter;
            score = bestScore;
            return false;
        }
    }
}
