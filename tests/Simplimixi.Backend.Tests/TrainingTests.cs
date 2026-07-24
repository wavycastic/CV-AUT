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
    }
}
