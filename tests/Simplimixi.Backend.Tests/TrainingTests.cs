using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class TrainingTests
    {
        [Fact]
        public void TrainingConfig_ParsesPropertiesCorrectly()
        {
            var config = new TrainingConfig("quick_train", 1, "barch_standard");
            Assert.Equal("quick_train", config.Mode);
            Assert.Equal(1, config.QuickSlot);
            Assert.Equal("barch_standard", config.AttackStrategy);
        }

        [Fact]
        public void ReadinessPolicy_DoesNotRebuildUnknownOcrState()
        {
            var state = new ArmyState(
                TrainingDetectionState.Unknown,
                TrainingDetectionState.Ready,
                TrainingDetectionState.Ready,
                HeroesReady: true);

            TrainingReadiness readiness = new TrainingReadinessPolicy().Evaluate(state);

            Assert.False(readiness.IsReady);
            Assert.False(readiness.RebuildArmy);
            Assert.False(readiness.RebuildSpells);
            Assert.False(readiness.RebuildSiege);
        }

        [Theory]
        [InlineData(320, true)]
        [InlineData(120, true)]
        [InlineData(20, false)]
        [InlineData(326, false)]
        [InlineData(0, false)]
        public void ArmyCapacity_RejectsPartialOcrValues(int capacity, bool expected)
        {
            Assert.Equal(expected, ArmyStateDetector.IsPlausibleArmyCapacity(capacity));
        }

        [Theory]
        [InlineData("320320", 320, 320, true)]
        [InlineData("1111", 11, 11, true)]
        [InlineData("32326", 0, 0, false)]
        public void FractionParser_PrefersCompleteBalancedIndicator(
            string digits,
            int expectedCurrent,
            int expectedCapacity,
            bool expectedResult)
        {
            bool result = TrainingVision.TryParseBalancedFractionDigits(
                digits,
                out int current,
                out int capacity);

            Assert.Equal(expectedResult, result);
            Assert.Equal(expectedCurrent, current);
            Assert.Equal(expectedCapacity, capacity);
        }

    }
}
