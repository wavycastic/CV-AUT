using CvAut;
using CvAut.AttackPipelines;
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
    }
}
