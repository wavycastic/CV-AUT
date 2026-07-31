using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class MainVillageBuilderAvailabilityDetectorTests
    {
        [Theory]
        [InlineData(3, 0, 3)]
        [InlineData(16, 1, 6)]
        [InlineData(26, 2, 6)]
        [InlineData(36, 3, 6)]
        [InlineData(46, 4, 6)]
        [InlineData(17, 1, 7)]
        public void TryParseBuilderCount_ParsesCounterWithoutSlash(
            int raw,
            int expectedFree,
            int expectedTotal)
        {
            bool parsed = MainVillageBuilderAvailabilityDetector.TryParseBuilderCount(
                raw,
                out int freeBuilders,
                out int totalBuilders);

            Assert.True(parsed);
            Assert.Equal(expectedFree, freeBuilders);
            Assert.Equal(expectedTotal, totalBuilders);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(70)]
        [InlineData(98)]
        [InlineData(100)]
        public void TryParseBuilderCount_RejectsImplausibleValues(int raw)
        {
            bool parsed = MainVillageBuilderAvailabilityDetector.TryParseBuilderCount(
                raw,
                out _,
                out _);

            Assert.False(parsed);
        }

        [Fact]
        public void TryResolveBuilderCount_PrefersPlausibleGrayReading()
        {
            bool resolved = MainVillageBuilderAvailabilityDetector.TryResolveBuilderCount(
                76, 0.76, 26, 0.77,
                out int freeBuilders,
                out int totalBuilders,
                out _);

            Assert.True(resolved);
            Assert.Equal(2, freeBuilders);
            Assert.Equal(6, totalBuilders);
        }

        [Fact]
        public void TryResolveBuilderCount_InfersZeroFromTwoImpossibleReadingsWithSameTotal()
        {
            bool resolved = MainVillageBuilderAvailabilityDetector.TryResolveBuilderCount(
                83, 0.74, 63, 0.75,
                out int freeBuilders,
                out int totalBuilders,
                out _);

            Assert.True(resolved);
            Assert.Equal(0, freeBuilders);
            Assert.Equal(3, totalBuilders);
        }
    }
}
