using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal partial class BuilderBaseAttacks
    {
        private bool HasVisibleTroopsOnPrepScreen()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in BuilderTroopTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, TroopThreshold, AttackPrepTroopRoi, out double score)
                    ?? _vision.FindElement(screenshot, template, TroopThreshold, null, out score);
                if (center == null) continue;

                Console.WriteLine($"[BB-ATTACK] phase=army_ready status=found template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                return true;
            }

            return false;
        }

        private void CloseAttackPrep(CancellationToken token)
        {
            if (TapFirstVisible(CloseTemplates, 0.55, CloseButtonRoi, token, out string matched))
            {
                Console.WriteLine($"[BB-ATTACK] phase=close_prep status=success template=\"{matched}\"");
                Sleep(800, token);
                return;
            }

            _adb.Tap(1450, 90);
            Sleep(800, token);
        }

        private bool WaitForBattleScreen(CancellationToken token)
        {
            for (int i = 1; i <= 20 && !token.IsCancellationRequested; i++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty())
                {
                    foreach (string template in BuilderTroopTemplates)
                    {
                        Point? center = _vision.FindElement(screenshot, template, TroopThreshold, DeployBarRoi, out double score);
                        if (center != null)
                        {
                            Console.WriteLine($"[BB-ATTACK] phase=wait_battle status=success template=\"{template}\" score={score:F2}");
                            return true;
                        }
                    }
                }

                Console.WriteLine($"[BB-ATTACK] phase=wait_battle status=pending attempt={i}");
                if (Sleep(1500, token)) return false;
            }

            Console.WriteLine("[BB-ATTACK] phase=wait_battle status=fail reason=troop_bar_not_detected");
            return false;
        }

        private bool ClickFindNowIfRequired(CancellationToken token)
        {
            if (WaitForBattleScreenQuick(token))
            {
                Console.WriteLine("[BB-ATTACK] phase=find_now status=skip reason=already_in_battle");
                return true;
            }

            for (int attempt = 1; attempt <= 5 && !token.IsCancellationRequested; attempt++)
            {
                if (TapFirstVisible(FindNowTemplates, 0.56, FindNowButtonRoi, token, out string template))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=find_now status=success template=\"{template}\" attempt={attempt}");
                    Sleep(1800, token);
                    return true;
                }

                Console.WriteLine($"[BB-ATTACK] phase=find_now status=retry attempt={attempt} reason=button_not_found");
                if (Sleep(700, token)) return false;
            }

            bool inBattle = WaitForBattleScreenQuick(token);
            Console.WriteLine($"[BB-ATTACK] phase=find_now status={(inBattle ? "skip" : "fail")} reason={(inBattle ? "battle_detected_after_wait" : "button_not_found_after_retry")}");
            return inBattle;
        }

        private bool WaitCloudsAndEnemyVillage(CancellationToken token)
        {
            int cloudTicks = 0;
            for (int attempt = 1; attempt <= 30 && !token.IsCancellationRequested; attempt++)
            {
                if (WaitForBattleScreenQuick(token) && IsEnemyVillageLoaded())
                {
                    Console.WriteLine($"[BB-ATTACK] phase=cloud status=success attempt={attempt} cloud_ticks={cloudTicks}");
                    return true;
                }

                cloudTicks++;
                if (attempt == 21)
                {
                    Console.WriteLine("[BB-ATTACK] phase=cloud status=retry attempt=21 action=android_back_and_find_now");
                    _adb.ExecuteShell("input keyevent 4");
                    Sleep(1000, token);
                    ClickFindNowIfRequired(token);
                }
                else if (attempt == 26)
                {
                    Console.WriteLine("[BB-ATTACK] phase=cloud status=retry attempt=26 action=android_back_and_find_now");
                    _adb.ExecuteShell("input keyevent 4");
                    Sleep(1000, token);
                    ClickFindNowIfRequired(token);
                }
                else
                {
                    Console.WriteLine($"[BB-ATTACK] phase=cloud status=pending attempt={attempt}");
                }

                if (Sleep(1000, token)) return false;
            }

            return false;
        }

        private bool WaitForEnemyVillageLoaded(CancellationToken token, string phase)
        {
            for (int attempt = 1; attempt <= 10 && !token.IsCancellationRequested; attempt++)
            {
                if (IsEnemyVillageLoaded())
                {
                    Console.WriteLine($"[BB-ATTACK] phase={phase} status=success attempt={attempt}");
                    return true;
                }

                Console.WriteLine($"[BB-ATTACK] phase={phase} status=pending attempt={attempt}");
                if (Sleep(1000, token)) return false;
            }

            return false;
        }

        private bool WaitForStage2BattleReady(CancellationToken token)
        {
            for (int attempt = 1; attempt <= 12 && !token.IsCancellationRequested; attempt++)
            {
                if (BBGoldEnd("Stage2Precheck"))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=stage2 status=skip attempt={attempt} reason=result_screen_detected");
                    return false;
                }

                if (TryDismissBattlePopup(token))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=stage2 status=pending attempt={attempt} reason=popup_dismissed");
                    continue;
                }

                if (WaitForBattleScreenQuick(token) && IsEnemyVillageLoaded())
                {
                    List<BuilderBaseTroopSlot> slots = ReadAttackBarSlots(remaining: false, secondAttack: true);
                    if (slots.Count > 0)
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=stage2 status=ready attempt={attempt} slots={slots.Count}");
                        return true;
                    }

                    Console.WriteLine($"[BB-ATTACK] phase=stage2 status=pending attempt={attempt} reason=attack_bar_not_ready");
                }
                else
                {
                    Console.WriteLine($"[BB-ATTACK] phase=stage2 status=pending attempt={attempt} reason=battle_screen_not_ready");
                }

                if (Sleep(1500, token)) return false;
            }

            return false;
        }

        private void ZoomOutBattleView(CancellationToken token, string phase)
        {
            if (token.IsCancellationRequested) return;
            Console.WriteLine($"[BB-ATTACK] phase=zoom_out status=start context={phase}");
            _adb.PinchInZoomOut(count: 2, durationMs: 450, intervalMs: 350);
            Sleep(900, token);
        }

        private bool WaitForBattleScreenQuick(CancellationToken token)
        {
            for (int i = 1; i <= 3 && !token.IsCancellationRequested; i++)
            {
                using Mat? screenshot = _adb.TakeScreenshot();
                if (screenshot != null && !screenshot.Empty())
                {
                    foreach (string template in BuilderTroopTemplates)
                    {
                        if (_vision.FindElement(screenshot, template, TroopThreshold, DeployBarRoi, out _) != null) return true;
                    }
                }

                if (Sleep(350, token)) return false;
            }

            return false;
        }

        private bool IsEnemyVillageLoaded()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in BuilderTroopTemplates)
            {
                if (_vision.FindElement(screenshot, template, TroopThreshold, DeployBarRoi, out _) != null)
                {
                    return true;
                }
            }

            bool hasMapSignal = _vision.FindElement(screenshot, @"ui\surrender_button", 0.48, null, out _) != null
                || _vision.FindElement(screenshot, @"ui\surrender", 0.48, null, out _) != null;
            if (hasMapSignal) return true;

            Rect safe = ImageUtils.ClampRect(EnemyVillageRoi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return false;
            using Mat roi = new(screenshot, safe);
            Scalar mean = Cv2.Mean(roi);
            return mean.Val0 + mean.Val1 + mean.Val2 > 45;
        }

        private bool DetectObstructedLayout()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            foreach (string template in ObstructedTemplates)
            {
                if (_vision.FindElement(screenshot, template, 0.55, null, out double score) == null) continue;
                Console.WriteLine($"[BB-ATTACK] phase=obstructed status=detected template=\"{template}\" score={score:F2}");
                return true;
            }

            return false;
        }

        private bool CheckMachineAbilityLoop()
        {
            Point? machine = GetMachinePos(out string machineName);
            if (IsMachineDeadByMbrPixel())
            {
                Console.WriteLine($"[BB-ATTACK] phase=machine_loop status=dead name=\"{(string.IsNullOrWhiteSpace(machineName) ? "machine" : machineName)}\" reason=mbr_dead_pixel");
                return false;
            }

            if (machine == null)
            {
                Console.WriteLine("[BB-ATTACK] phase=machine_loop status=skip reason=machine_not_on_bar_or_dead");
                return false;
            }

            if (IsMachineAbilityWaiting(machine.Value))
            {
                _machineLoopWaitCount++;
                Console.WriteLine($"[BB-ATTACK] phase=machine_loop status=wait name=\"{machineName}\" reason=ability_wait_state count={_machineLoopWaitCount}");
                return true;
            }

            bool activated = TryActivateHeroAbilityOnce();
            if (activated) _machineLoopAbilityCount++;
            _machineLoopWaitCount = 0;
            Console.WriteLine($"[BB-ATTACK] phase=machine_loop status={(activated ? "ability" : "alive")} name=\"{machineName}\" ability_count={_machineLoopAbilityCount}");
            return true;
        }

        private bool CheckBomberAbilityLoop()
        {
            if (_activeBomberSlots.Count == 0) return false;
            int aliveOrUnknown = 0;
            foreach (BuilderBaseTroopSlot bomber in _activeBomberSlots.ToArray())
            {
                if (IsSlotBannerGrey(bomber))
                {
                    Console.WriteLine($"[BB-ATTACK] phase=bomber_loop status=dead slot={bomber.Index}");
                    _activeBomberSlots.RemoveAll(s => s.Index == bomber.Index);
                    continue;
                }

                aliveOrUnknown++;
                TryActivateBomberAbility(bomber);
            }

            return aliveOrUnknown > 0;
        }

        private bool IsBBAttackPage()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            if (TryGetPixel(screenshot, 30, 550, out Vec3b pixel) && IsColorNear(pixel, 0xCF0D0E, 20))
            {
                Console.WriteLine("[BB-ATTACK] phase=attack_page status=success reason=surrender_red_pixel");
                return true;
            }

            int scaledX = (int)Math.Round(30 * (screenshot.Width / 860.0));
            int scaledY = (int)Math.Round(550 * (screenshot.Height / 732.0));
            bool detected = TryGetPixel(screenshot, scaledX, scaledY, out pixel) && IsColorNear(pixel, 0xCF0D0E, 20);
            if (detected)
            {
                Console.WriteLine("[BB-ATTACK] phase=attack_page status=success reason=scaled_surrender_red_pixel");
            }

            return detected;
        }

        private bool BBGoldEnd(string logText = "BBGoldEnd")
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            Point goldEndPoint = ScaleMbrPoint(632, 406, screenshot.Width, screenshot.Height);
            int scaledX = goldEndPoint.X;
            int scaledY = goldEndPoint.Y;
            if (TryGetPixel(screenshot, scaledX, scaledY, out Vec3b scaledPixel)
                && IsColorNear(scaledPixel, 0xFFE649, 20))
            {
                Console.WriteLine($"[BB-ATTACK] phase=end_battle status=detected reason=bb_gold_end_scaled_pixel log=\"{logText}\" point=({scaledX},{scaledY})");
                return true;
            }

            foreach (string template in new[] { @"ui\okay_battle_rank", @"ui\okay_star", @"ui\okay", @"ui\okay_n", @"ui\okay_n2" })
            {
                if (_vision.FindElement(screenshot, template, 0.50, ResultRoi, out double score) != null)
                {
                    Console.WriteLine($"[BB-ATTACK] phase=end_battle status=detected template=\"{template}\" score={score:F2}");
                    return true;
                }
            }

            return false;
        }

        private bool TryHandleProblemAffect(CancellationToken token, string logText)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in ProblemAffectTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, 0.55, null, out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-ATTACK WARNING] phase=problem_affect status=detected log=\"{logText}\" template=\"{template}\" score={score:F2} action=acknowledge_or_abort");
                _adb.Tap(center.Value.X, center.Value.Y);
                Sleep(1500, token);
                return true;
            }

            foreach (string resultTemplate in ReturnHomeTemplates)
            {
                if (_vision.FindElement(screenshot, resultTemplate, 0.48, ResultRoi, out _) != null)
                {
                    return false;
                }
            }

            if (TryDetectBlockingDialogShape(screenshot, out Rect dialogRect))
            {
                Console.WriteLine($"[BB-ATTACK WARNING] phase=problem_affect status=detected log=\"{logText}\" template=\"dialog_shape\" rect=({dialogRect.X},{dialogRect.Y},{dialogRect.Width},{dialogRect.Height}) action=acknowledge_or_abort");
                _adb.Tap(dialogRect.X + dialogRect.Width / 2, dialogRect.Y + dialogRect.Height * 3 / 4);
                Sleep(1500, token);
                return true;
            }

            return false;
        }

        private static bool TryDetectBlockingDialogShape(Mat screenshot, out Rect dialogRect)
        {
            dialogRect = default;
            if (screenshot.Empty()) return false;
            Rect roi = ImageUtils.ClampRect(Rect.FromLTRB(320, 160, 1280, 760), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;
            using Mat crop = new(screenshot, roi);
            using Mat hsv = new();
            Cv2.CvtColor(crop, hsv, ColorConversionCodes.BGR2HSV);
            using Mat mask = new();
            Cv2.InRange(hsv, new Scalar(0, 0, 45), new Scalar(179, 55, 115), mask);
            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (Point[] contour in contours)
            {
                Rect localRect = Cv2.BoundingRect(contour);
                double area = Cv2.ContourArea(contour);
                double fillRatio = area / Math.Max(1, localRect.Width * localRect.Height);
                if (localRect.Width < screenshot.Width * 0.28 || localRect.Height < screenshot.Height * 0.12 || fillRatio < 0.50) continue;
                dialogRect = new Rect(roi.X + localRect.X, roi.Y + localRect.Y, localRect.Width, localRect.Height);
                return true;
            }
            return false;
        }

        private bool IsSlotBannerGrey(BuilderBaseTroopSlot slot)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            int bannerX = slot.Center.X + 37;
            int bannerY = 583 + (ScreenHeight - 900);
            if (TryGetPixel(screenshot, bannerX, bannerY, out Vec3b mbrPixel)
                && IsColorNear(mbrPixel, 0x707070, 10))
            {
                return true;
            }

            Rect roi = ImageUtils.ClampRect(Rect.FromLTRB(slot.Center.X + 25, slot.Center.Y - 5, slot.Center.X + 55, slot.Center.Y + 25), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;
            using Mat sample = new(screenshot, roi);
            Scalar mean = Cv2.Mean(sample);
            double spread = Math.Abs(mean.Val0 - mean.Val1) + Math.Abs(mean.Val1 - mean.Val2) + Math.Abs(mean.Val0 - mean.Val2);
            return spread < 24 && mean.Val0 is > 60 and < 145;
        }

        private static Point? FindMbrReadyAbilityPixel(Mat screenshot, Rect roi)
        {
            Rect safe = ImageUtils.ClampRect(roi, screenshot.Width, screenshot.Height);
            if (safe.Width <= 0 || safe.Height <= 0) return null;
            int step = Math.Max(2, safe.Width / 55);
            for (int y = safe.Top; y < safe.Bottom; y += step)
            {
                for (int x = safe.Left; x < safe.Right; x += step)
                {
                    Vec3b pixel = screenshot.At<Vec3b>(y, x);
                    int b = pixel.Item0, g = pixel.Item1, r = pixel.Item2;
                    bool violetReady = r >= 165 && b >= 165 && g <= 120 && Math.Abs(r - b) <= 90;
                    bool electricBlue = b >= 170 && g >= 120 && r <= 130;
                    if (violetReady || electricBlue) return new Point(x, y);
                }
            }
            return null;
        }

        private bool IsMachineDeadByMbrPixel()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;
            return IsMachineDeadByMbrPixel(screenshot);
        }

        private bool IsMachineDeadByMbrPixel(Mat screenshot)
        {
            int x = 71;
            int y = 663 + (ScreenHeight - 900);
            if (TryGetPixel(screenshot, x, y, out Vec3b pixel)
                && IsColorNear(pixel, 0x4E4E4E, 20))
            {
                return true;
            }

            int scaledX = (int)Math.Round(71 * (screenshot.Width / 860.0));
            int scaledY = (int)Math.Round(663 * (screenshot.Height / 732.0));
            return TryGetPixel(screenshot, scaledX, scaledY, out pixel) && IsColorNear(pixel, 0x4E4E4E, 20);
        }

        private bool IsMachineAbilityWaiting(Point machine)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            Rect roi = ImageUtils.ClampRect(Rect.FromLTRB(machine.X - 35, machine.Y - 40, machine.X + 35, machine.Y + 40), screenshot.Width, screenshot.Height);
            if (roi.Width <= 0 || roi.Height <= 0) return false;
            using Mat sample = new(screenshot, roi);
            Scalar mean = Cv2.Mean(sample);
            double spread = Math.Abs(mean.Val0 - mean.Val1) + Math.Abs(mean.Val1 - mean.Val2) + Math.Abs(mean.Val0 - mean.Val2);
            return spread < 18 && mean.Val0 is > 45 and < 120;
        }

        private bool WaitForAttackReady(CancellationToken token, string phase, int retries)
        {
            bool requirePrepScreen = !string.Equals(phase, "attack_entry", StringComparison.Ordinal);
            for (int attempt = 1; attempt <= Math.Max(1, retries) && !token.IsCancellationRequested; attempt++)
            {
                bool ready = requirePrepScreen ? HasVisibleTroopsOnPrepScreen() : _navigator.IsOnBuilderBase();
                if (ready)
                {
                    Console.WriteLine($"[BB-ATTACK] phase={phase} status=ready attempt={attempt} reason={(requirePrepScreen ? "prep_screen_detected" : "builder_base_detected")}");
                    return true;
                }
                Console.WriteLine($"[BB-ATTACK] phase={phase} status=retry attempt={attempt} action=wait_ready");
                if (Sleep(1200, token)) return false;
            }

            return requirePrepScreen ? HasVisibleTroopsOnPrepScreen() : _navigator.IsOnBuilderBase();
        }

        private static bool IsBonusOrChallengeTemplate(string template)
        {
            return template.IndexOf("bonus", StringComparison.OrdinalIgnoreCase) >= 0
                || template.IndexOf("challenge", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryDismissBattlePopup(CancellationToken token)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            if (_vision.FindElement(screenshot, @"ui\bonus", 0.50, ResultRoi, out double bonusScore) != null)
            {
                Console.WriteLine($"[BB-ATTACK] phase=bonus status=found template=\"ui\\bonus\" score={bonusScore:F2} action=tap_confirm");
                _adb.Tap(960, 560);
                Sleep(900, token);
                return true;
            }

            if (_vision.FindElement(screenshot, @"ui\challenge_complete", 0.50, ResultRoi, out double challengeScore) != null)
            {
                Console.WriteLine($"[BB-ATTACK] phase=bonus status=found template=\"ui\\challenge_complete\" score={challengeScore:F2} action=tap_confirm");
                _adb.Tap(960, 560);
                Sleep(900, token);
                return true;
            }

            if (CheckClanGamesCompletedLikeMbr(screenshot, out int completeBarHits, out int noBarChecks))
            {
                Console.WriteLine($"[BB-ATTACK] phase=challenge_complete status=success complete_bar_hits={completeBarHits} no_bar_checks={noBarChecks} action=tap_confirm");
                _adb.Tap(960, 560);
                Sleep(900, token);
                return true;
            }

            return false;
        }

        private bool CheckClanGamesCompletedLikeMbr(Mat screenshot, out int completeBarHits, out int noBarChecks)
        {
            completeBarHits = 0;
            noBarChecks = _clanGamesNoCompleteBarChecks;
            Rect completeRoi = ScaleMbrRect(770, 474, 830, 534, screenshot.Width, screenshot.Height);
            if (completeRoi.Width > 0 && completeRoi.Height > 0)
            {
                if (_vision.FindElement(screenshot, @"clan_games\game_complete", 0.50, completeRoi, out _) != null
                    || _vision.FindElement(screenshot, @"ui\game_complete", 0.50, completeRoi, out _) != null
                    || _vision.FindElement(screenshot, @"ui\challenge_complete", 0.50, completeRoi, out _) != null)
                {
                    _clanGamesNoCompleteBarChecks = 0;
                    noBarChecks = 0;
                    completeBarHits = 12;
                    return true;
                }
            }

            Point barPoint = ScaleMbrPoint(830, 500, screenshot.Width, screenshot.Height);
            Rect barRoi = ImageUtils.ClampRect(
                Rect.FromLTRB(barPoint.X - 12, barPoint.Y - 8, barPoint.X + 13, barPoint.Y + 9),
                screenshot.Width,
                screenshot.Height);
            if (barRoi.Width <= 0 || barRoi.Height <= 0) return false;

            using Mat bar = new(screenshot, barRoi);
            for (int y = 0; y < bar.Rows; y++)
            {
                for (int x = 0; x < bar.Cols; x++)
                {
                    Vec3b pixel = bar.At<Vec3b>(y, x);
                    if (IsYellowCompletePixel(pixel)) completeBarHits++;
                }
            }

            if (completeBarHits > 0)
            {
                _clanGamesNoCompleteBarChecks = 0;
                noBarChecks = 0;
                Console.WriteLine($"[BB-ATTACK] phase=challenge_progress status=bar_visible complete_bar_hits={completeBarHits}");
                return false;
            }

            _clanGamesNoCompleteBarChecks++;
            noBarChecks = _clanGamesNoCompleteBarChecks;
            Console.WriteLine($"[BB-ATTACK] phase=challenge_progress status=no_complete_bar check={_clanGamesNoCompleteBarChecks}/12");
            return _clanGamesNoCompleteBarChecks >= 12;
        }

        private static bool TryGetPixel(Mat image, int x, int y, out Vec3b pixel)
        {
            pixel = default;
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return false;
            pixel = image.At<Vec3b>(y, x);
            return true;
        }

        internal static Point ScaleMbrPoint(int x, int y, int imageWidth, int imageHeight)
        {
            return new Point(
                (int)Math.Round(x * (imageWidth / 860.0)),
                (int)Math.Round(y * (imageHeight / 732.0)));
        }

        private static Rect ScaleMbrRect(int left, int top, int right, int bottom, int imageWidth, int imageHeight)
        {
            Point tl = ScaleMbrPoint(left, top, imageWidth, imageHeight);
            Point br = ScaleMbrPoint(right, bottom, imageWidth, imageHeight);
            return ImageUtils.ClampRect(Rect.FromLTRB(tl.X, tl.Y, br.X, br.Y), imageWidth, imageHeight);
        }

        private static bool IsColorNear(Vec3b pixel, int rgb, int tolerance)
        {
            int r = (rgb >> 16) & 0xFF;
            int g = (rgb >> 8) & 0xFF;
            int b = rgb & 0xFF;
            return Math.Abs(pixel.Item2 - r) <= tolerance
                && Math.Abs(pixel.Item1 - g) <= tolerance
                && Math.Abs(pixel.Item0 - b) <= tolerance;
        }

        private static bool IsYellowCompletePixel(Vec3b pixel)
        {
            int b = pixel.Item0;
            int g = pixel.Item1;
            int r = pixel.Item2;
            return r >= 220 && g >= 180 && b <= 90;
        }

        private int ReadDamage() => ReadNumberFromRoi(DamageRoi);
        private int ReadResultDamage() => ReadNumberFromRoi(ResultDamageRoi);

        private int ReadNumberFromRoi(Rect roi)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out _, useRgbThresh: true)) return Math.Clamp(value, 0, 100);
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out _)) return Math.Clamp(value, 0, 100);
            return 0;
        }

        private int ReadStars()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;
            if (_vision.FindElement(screenshot, @"ui\3star", 0.55, ResultStarsRoi, out _) != null || _vision.FindElement(screenshot, @"ui\three_star", 0.55, ResultStarsRoi, out _) != null) return 3;
            if (_vision.FindElement(screenshot, @"ui\2star", 0.55, ResultStarsRoi, out _) != null || _vision.FindElement(screenshot, @"ui\two_star", 0.55, ResultStarsRoi, out _) != null) return 2;
            if (_vision.FindElement(screenshot, @"ui\1star", 0.55, ResultStarsRoi, out _) != null || _vision.FindElement(screenshot, @"ui\one_star", 0.55, ResultStarsRoi, out _) != null) return 1;
            return 0;
        }

        private bool TapFirstVisible(string[] templates, double threshold, Rect? roi, CancellationToken token, out string matchedTemplate)
        {
            matchedTemplate = string.Empty;
            if (token.IsCancellationRequested) return false;

            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in templates)
            {
                Point? center = _vision.FindElement(screenshot, template, threshold, roi, out double score);
                if (center == null) continue;

                matchedTemplate = template;
                Console.WriteLine($"[BB-ATTACK] phase=template status=success template=\"{template}\" score={score:F2} center=({center.Value.X},{center.Value.Y})");
                _adb.Tap(center.Value.X, center.Value.Y);
                return true;
            }

            return false;
        }
    }
}
