using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class BuilderBaseReportReliabilityTests
    {
        [Theory]
        [InlineData(12, 12)]
        [InlineData(10, 10)]
        [InlineData(6, 6)]
        public void ParseStarPair_DedicatedRoiAcceptsCompletedBonusShorthand(int raw, int expectedMax)
        {
            (int remaining, int max) = BuilderBaseReport.ParseStarPair(raw, allowCompletedShorthand: true);

            Assert.Equal(0, remaining);
            Assert.Equal(expectedMax, max);
        }

        [Theory]
        [InlineData(12)]
        [InlineData(10)]
        [InlineData(6)]
        public void ParseStarPair_DefaultModeKeepsAmbiguousValuesUnknown(int raw)
        {
            (int remaining, int max) = BuilderBaseReport.ParseStarPair(raw);

            Assert.Equal(0, remaining);
            Assert.Equal(0, max);
        }

        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void IsAttackAvailabilityKnown_RequiresDirectAttackOrBonusEvidence(
            bool attackButtonDetected,
            bool starBonusKnown,
            bool expected)
        {
            bool actual = BuilderBaseReport.IsAttackAvailabilityKnown(attackButtonDetected, starBonusKnown);

            Assert.Equal(expected, actual);
        }
    }
}
