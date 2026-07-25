using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using Point = OpenCvSharp.Point;

namespace CvAut
{
    internal sealed class BattleOutcomeWatcher
    {
        private readonly IADBHelper _adb;
        private readonly IVisionEngine _vision;
        private readonly BuilderBaseNavigator _navigator;
        private readonly HeroAbilityController _heroController;
        private readonly AttackBarScanner _barScanner;
        private readonly ReturnHomeController _returnHomeController;
        private readonly AttackEntryFlow _entryFlow;

        private int _clanGamesNoCompleteBarChecks;

        public BattleOutcomeWatcher(
            IADBHelper adb,
            IVisionEngine vision,
            BuilderBaseNavigator navigator,
            HeroAbilityController heroController,
            AttackBarScanner barScanner,
            ReturnHomeController returnHomeController,
            AttackEntryFlow entryFlow)
        {
            _adb = adb;
            _vision = vision;
            _navigator = navigator;
            _heroController = heroController;
            _barScanner = barScanner;
            _returnHomeController = returnHomeController;
            _entryFlow = entryFlow;
        }

        public void ResetClanGamesChecks() => _clanGamesNoCompleteBarChecks = 0;

        public BuilderBaseBattleResult WaitBattleAndReturn(BuilderBaseBattleOptions options, CancellationToken token, TroopDeploymentExecutor deploymentExecutor)
        {
            Console.WriteLine("[BB-ATTACK] phase=wait_end status=start");
            DateTime timeout = DateTime.Now.AddSeconds(150);
            int lastDamage = 0;
            int sameDamageTicks = 0;
            bool stage2 = false;

            while (DateTime.Now < timeout && !token.IsCancellationRequested)
            {
                if (BBGoldEnd("EndBattleBB"))
                {
                    int stars = ReadStars();
                    int finalDamage = Math.Max(lastDamage, ReadResultDamage());
                    Console.WriteLine($"[BB-ATTACK] phase=end_battle status=early_detected damage={finalDamage} stars={stars}");
                    bool returnedHome = _returnHomeController.ReturnHomeDropTrophyBB(token);
                    return new(returnedHome, finalDamage, stars, stage2);
                }

                _heroController.CheckMachineAbilityLoop();
                if (options.HandleBomber) _heroController.CheckBomberAbilityLoop(deploymentExecutor.ActiveBomberSlots);

                if (TryHandleProblemAffect(token, "EndBattleBB"))
                {
                    return new(false, lastDamage, ReadStars(), stage2);
                }

                int damage = ReadDamage();
                if (damage > 0)
                {
                    sameDamageTicks = damage == lastDamage ? sameDamageTicks + 1 : 0;
                    lastDamage = damage;
                    Console.WriteLine($"[BB-ATTACK] phase=damage status=read value={damage} same_ticks={sameDamageTicks}");
                }

                if (!stage2 && damage >= 100)
                {
                    Console.WriteLine("[BB-ATTACK] phase=stage2 status=pending action=wait_transition reason=damage_reached_100");
                    if (!WaitForStage2BattleReady(token))
                    {
                        Console.WriteLine("[BB-ATTACK] phase=stage2 status=skip reason=stage2_not_confirmed_possible_result_screen");
                        continue;
                    }

                    stage2 = true;
                    lastDamage = 0;
                    sameDamageTicks = 0;
                    ZoomOutBattleView(token, "stage2");
                    Console.WriteLine("[BB-ATTACK] phase=stage2 status=detected action=redeploy_remaining reason=attack_bar_ready");
                    deploymentExecutor.DeployAllVisibleTroops(options, token, secondAttack: true);
                    timeout = DateTime.Now.AddSeconds(150);
                    continue;
                }

                if (sameDamageTicks >= 25 && damage > 0)
                {
                    Console.WriteLine($"[BB-ATTACK] phase=wait_end status=stalled action=surrender reason=same_damage_ticks damage={damage} ticks={sameDamageTicks}");
                    bool surrendered = _returnHomeController.ReturnHomeDropTrophyBB(token);
                    return new(surrendered, lastDamage, ReadStars(), stage2);
                }

                if (TryDismissBattlePopup(token))
                {
                    Console.WriteLine("[BB-ATTACK] phase=wait_end status=pending action=dismiss_popup");
                    continue;
                }

                if (_entryFlow.TapFirstVisible(BuilderBaseAttackLayout.ReturnHomeTemplates, 0.48, BuilderBaseAttackLayout.ResultRoi, token, out string matched))
                {
                    if (IsBonusOrChallengeTemplate(matched))
                    {
                        Console.WriteLine($"[BB-ATTACK] phase=bonus status=detected template=\"{matched}\" action=acknowledge");
                    }

                    Console.WriteLine($"[BB-ATTACK] phase=return_home status=pending template=\"{matched}\"");
                    int stars = ReadStars();
                    int finalDamage = Math.Max(lastDamage, ReadResultDamage());
                    Console.WriteLine($"[BB-ATTACK] phase=result status=read damage={finalDamage} stars={stars}");
                    for (int verify = 1; verify <= 3 && !token.IsCancellationRequested; verify++)
                    {
                        Sleep(1200, token);
                        if (_navigator.IsOnBuilderBase())
                        {
                            Console.WriteLine($"[BB-ATTACK] phase=return_home status=success verify={verify}");
                            return new(true, finalDamage, stars, stage2);
                        }
                    }
                    Console.WriteLine("[BB-ATTACK] phase=return_home status=pending reason=button_tapped_but_base_not_detected");
                    continue;
                }

                if (BBGoldEnd("EndBattleBB"))
                {
                    int stars = ReadStars();
                    int finalDamage = Math.Max(lastDamage, ReadResultDamage());
                    Console.WriteLine($"[BB-ATTACK] phase=end_battle status=success reason=result_sentinel damage={finalDamage} stars={stars}");
                    return new(true, finalDamage, stars, stage2);
                }

                if (_navigator.IsOnBuilderBase())
                {
                    return new(true, lastDamage, ReadStars(), stage2);
                }

                if (IsBBAttackPage())
                {
                    Console.WriteLine("[BB-ATTACK] phase=wait_end status=pending reason=attack_page_active");
                }

                if (TryHandleProblemAffect(token, "EndBattleBB"))
                {
                    return new(false, lastDamage, ReadStars(), stage2);
                }

                Sleep(3000, token);
            }

            if (token.IsCancellationRequested) return new(false, lastDamage, 0, stage2);

            Console.WriteLine("[BB-ATTACK] phase=wait_end status=timeout_or_stalled action=surrender_fallback");
            bool returned = _returnHomeController.ReturnHomeDropTrophyBB(token);
            return new(returned, lastDamage, ReadStars(), stage2);
        }

        public bool BBGoldEnd(string logText = "BBGoldEnd")
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            Point goldEndPoint = MbrScreenScaling.ScaleMbrPoint(632, 406, screenshot.Width, screenshot.Height);
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
                if (_vision.FindElement(screenshot, template, 0.50, BuilderBaseAttackLayout.ResultRoi, out double score) != null)
                {
                    Console.WriteLine($"[BB-ATTACK] phase=end_battle status=detected template=\"{template}\" score={score:F2}");
                    return true;
                }
            }

            return false;
        }

        public bool TryHandleProblemAffect(CancellationToken token, string logText)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            foreach (string template in BuilderBaseAttackLayout.ProblemAffectTemplates)
            {
                Point? center = _vision.FindElement(screenshot, template, 0.55, null, out double score);
                if (center == null) continue;

                Console.WriteLine($"[BB-ATTACK WARNING] phase=problem_affect status=detected log=\"{logText}\" template=\"{template}\" score={score:F2} action=acknowledge_or_abort");
                _adb.Tap(center.Value.X, center.Value.Y);
                Sleep(1500, token);
                return true;
            }

            foreach (string resultTemplate in BuilderBaseAttackLayout.ReturnHomeTemplates)
            {
                if (_vision.FindElement(screenshot, resultTemplate, 0.48, BuilderBaseAttackLayout.ResultRoi, out _) != null)
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

        public static bool TryDetectBlockingDialogShape(Mat screenshot, out Rect dialogRect)
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

        public bool IsBBAttackPage() => _entryFlow.IsBBAttackPage();

        public bool WaitForStage2BattleReady(CancellationToken token)
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

                if (_entryFlow.WaitForBattleScreenQuick(token) && _entryFlow.IsEnemyVillageLoaded())
                {
                    List<BuilderBaseTroopSlot> slots = _barScanner.ReadAttackBarSlots(remaining: false, secondAttack: true);
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

        public void ZoomOutBattleView(CancellationToken token, string phase)
        {
            if (token.IsCancellationRequested) return;
            Console.WriteLine($"[BB-ATTACK] phase=zoom_out status=start context={phase}");
            _adb.PinchInZoomOut(count: 2, durationMs: 450, intervalMs: 350);
            Sleep(900, token);
        }

        public bool TryDismissBattlePopup(CancellationToken token)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return false;

            if (_vision.FindElement(screenshot, @"ui\bonus", 0.50, BuilderBaseAttackLayout.ResultRoi, out double bonusScore) != null)
            {
                Console.WriteLine($"[BB-ATTACK] phase=bonus status=found template=\"ui\\bonus\" score={bonusScore:F2} action=tap_confirm");
                _adb.Tap(960, 560);
                Sleep(900, token);
                return true;
            }

            if (_vision.FindElement(screenshot, @"ui\challenge_complete", 0.50, BuilderBaseAttackLayout.ResultRoi, out double challengeScore) != null)
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

        public bool CheckClanGamesCompletedLikeMbr(Mat screenshot, out int completeBarHits, out int noBarChecks)
        {
            completeBarHits = 0;
            noBarChecks = _clanGamesNoCompleteBarChecks;
            Rect completeRoi = MbrScreenScaling.ScaleMbrRect(770, 474, 830, 534, screenshot.Width, screenshot.Height);
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

            Point barPoint = MbrScreenScaling.ScaleMbrPoint(830, 500, screenshot.Width, screenshot.Height);
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

        public int ReadDamage() => ReadNumberFromRoi(BuilderBaseAttackLayout.DamageRoi);
        public int ReadResultDamage() => ReadNumberFromRoi(BuilderBaseAttackLayout.ResultDamageRoi);

        public int ReadNumberFromRoi(Rect roi)
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out int value, out _, useRgbThresh: true)) return Math.Clamp(value, 0, 100);
            if (_vision.TryExtractNumericalMetrics(screenshot, roi, out value, out _)) return Math.Clamp(value, 0, 100);
            return 0;
        }

        public int ReadStars()
        {
            using Mat? screenshot = _adb.TakeScreenshot();
            if (screenshot == null || screenshot.Empty()) return 0;
            if (_vision.FindElement(screenshot, @"ui\3star", 0.55, BuilderBaseAttackLayout.ResultStarsRoi, out _) != null || _vision.FindElement(screenshot, @"ui\three_star", 0.55, BuilderBaseAttackLayout.ResultStarsRoi, out _) != null) return 3;
            if (_vision.FindElement(screenshot, @"ui\2star", 0.55, BuilderBaseAttackLayout.ResultStarsRoi, out _) != null || _vision.FindElement(screenshot, @"ui\two_star", 0.55, BuilderBaseAttackLayout.ResultStarsRoi, out _) != null) return 2;
            if (_vision.FindElement(screenshot, @"ui\1star", 0.55, BuilderBaseAttackLayout.ResultStarsRoi, out _) != null || _vision.FindElement(screenshot, @"ui\one_star", 0.55, BuilderBaseAttackLayout.ResultStarsRoi, out _) != null) return 1;
            return 0;
        }

        private static bool IsBonusOrChallengeTemplate(string template)
        {
            return template.IndexOf("bonus", StringComparison.OrdinalIgnoreCase) >= 0
                || template.IndexOf("challenge", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetPixel(Mat image, int x, int y, out Vec3b pixel)
        {
            pixel = default;
            if (x < 0 || y < 0 || x >= image.Width || y >= image.Height) return false;
            pixel = image.At<Vec3b>(y, x);
            return true;
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

        private static bool Sleep(int milliseconds, CancellationToken token) => token.WaitHandle.WaitOne(milliseconds);
    }
}
