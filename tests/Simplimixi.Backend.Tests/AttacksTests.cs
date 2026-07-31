using CvAut;
using CvAut.AttackPipelines;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AttacksTests
    {
        [Fact]
        public void StandardBarchStrategy_HasCorrectName()
        {
            ITroopDeploymentStrategy strategy = new StandardBarchStrategy();
            Assert.Equal("barch_standard", strategy.Name);
        }

        [Theory]
        [InlineData(14, 0.80, 14, true)]
        [InlineData(0, 0.80, 14, true)]
        [InlineData(21, 0.80, 14, false)]
        [InlineData(4, 0.40, 14, false)]
        public void TroopCountReader_RejectsImpossibleOrLowConfidenceCounts(
            int value,
            double confidence,
            int maximumExpected,
            bool expected)
        {
            Assert.Equal(expected, TroopCountReader.IsPlausible(value, confidence, maximumExpected));
        }

        [Fact]
        public void SpellPlan_UsesDetectedFourRageAndThreeFreeze()
        {
            bool valid = AttackSpellDeploymentStrategy.TryCreatePlan(
                4, 3, 2, 6, 3, 11, out SpellDeploymentPlan plan, out string reason);

            Assert.True(valid, reason);
            Assert.Equal(2, plan.RageInitial);
            Assert.Equal(3, plan.Freeze);
            Assert.Equal(2, plan.RageRemaining);
            Assert.Equal(11, plan.SpellSpace);
        }

        [Fact]
        public void SpellPlan_SupportsFiveRageAndOneFreezeWithinCoordinates()
        {
            bool valid = AttackSpellDeploymentStrategy.TryCreatePlan(
                5, 1, 2, 6, 3, 11, out SpellDeploymentPlan plan, out string reason);

            Assert.True(valid, reason);
            Assert.Equal(2, plan.RageInitial);
            Assert.Equal(1, plan.Freeze);
            Assert.Equal(3, plan.RageRemaining);
        }

        [Fact]
        public void SpellPlan_SupportsFourRageAndOneFreezeAtCapacityNine()
        {
            int expectedSpace = AttackSpellDeploymentStrategy.ResolveExpectedSpellSpace(4, 1);
            bool valid = AttackSpellDeploymentStrategy.TryCreatePlan(
                4, 1, 2, 6, 3, expectedSpace, out SpellDeploymentPlan plan, out string reason);

            Assert.True(valid, reason);
            Assert.Equal(9, expectedSpace);
            Assert.Equal(2, plan.RageInitial);
            Assert.Equal(1, plan.Freeze);
            Assert.Equal(2, plan.RageRemaining);
        }

        [Theory]
        [InlineData(4, 8, 2, 6, 3, 11)]
        [InlineData(4, 3, 2, 6, 1, 11)]
        [InlineData(2, 7, 2, 6, 3, 11)]
        public void SpellPlan_RejectsInvalidSpaceOrCoordinateShortage(
            int rage,
            int freeze,
            int initialSlots,
            int freezeSlots,
            int remainingSlots,
            int spellSpace)
        {
            Assert.False(AttackSpellDeploymentStrategy.TryCreatePlan(
                rage, freeze, initialSlots, freezeSlots, remainingSlots, spellSpace, out _, out _));
        }

        [Theory]
        [InlineData("reason=no_candidate_accepted samples=c1:rgb:value=0:confidence=0.00:reason=no_result", true)]
        [InlineData("reason=no_candidate_accepted samples=c1:rgb:value=24:confidence=0.77:reason=out_of_range", false)]
        [InlineData("reason=screenshot_empty", false)]
        public void SpellEmptyBadge_IsDistinguishedFromUnreadableOcr(string diagnostic, bool expected)
        {
            Assert.Equal(expected, AttackSpellDeploymentStrategy.IsEmptyBadgeDiagnostic(diagnostic));
        }

        [Theory]
        [InlineData(12, 14, 12)]
        [InlineData(16, 17, 16)]
        [InlineData(20, 17, 17)]
        [InlineData(0, 14, 0)]
        [InlineData(-1, 14, 0)]
        public void Deployment_UsesDetectedCountWithinAvailableCoordinates(
            int detectedCount,
            int availableCoordinates,
            int expected)
        {
            Assert.Equal(
                expected,
                AttackTroopDeploymentStrategy.ResolveTapCount(detectedCount, availableCoordinates));
        }

        [Fact]
        public void DeployBarScanner_RejectsSiegeAtDragonLocation()
        {
            var tabs = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase)
            {
                ["dragon"] = new Point(177, 815)
            };

            bool duplicate = AttackDeployBarScanner.IsDuplicate(
                new Point(177, 816),
                tabs,
                "siege_machine");

            Assert.True(duplicate);
        }

        [Fact]
        public void QuantityBadgeRoi_ExcludesPrefixAndKeepsTwoDigits()
        {
            using Mat screenshot = new(new Size(1600, 900), MatType.CV_8UC3, Scalar.Black);
            var tab = new Point(299, 816);
            var prefix = new Rect(300, 752, 16, 17);
            var firstDigit = new Rect(318, 749, 8, 19);
            var secondDigit = new Rect(328, 749, 18, 20);
            Cv2.Rectangle(screenshot, prefix, Scalar.White, -1);
            Cv2.Rectangle(screenshot, firstDigit, Scalar.White, -1);
            Cv2.Rectangle(screenshot, secondDigit, Scalar.White, -1);

            bool found = TroopCountReader.TryBuildQuantityRoi(screenshot, tab, 17, out Rect roi);

            Assert.True(found);
            Assert.True(roi.Left > prefix.Right - 1);
            Assert.True(roi.Left <= firstDigit.Left);
            Assert.True(roi.Right >= secondDigit.Right);
        }
    }
}
