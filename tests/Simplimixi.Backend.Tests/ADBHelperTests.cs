using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class ADBHelperTests
    {
        [Fact]
        [Trait("Category", "Integration")]
        public void ADBHelper_ConstructsWithValidHostAndPort()
        {
            var adb = new ADBHelper("127.0.0.1", 5555);
            Assert.Equal("127.0.0.1", adb.Host);
            Assert.Equal(5555, adb.Port);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void ADBHelper_DefaultDeviceAddressIsPopulated()
        {
            var adb = new ADBHelper("127.0.0.1", 5556);
            Assert.NotNull(adb.DeviceAddress);
        }
    }
}
