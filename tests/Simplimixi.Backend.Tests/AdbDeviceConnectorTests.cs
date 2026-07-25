using CvAut.Adb;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class AdbDeviceConnectorTests
    {
        [Fact]
        public void BuildFallbackPorts_PreservesOrderAndRemovesDuplicates()
        {
            var result = AdbDeviceConnector.BuildFallbackPorts(
                configuredPort: 5556,
                discoveredPorts: new[] { 5556, 6000, 6000, -1 },
                defaultPorts: new[] { 5555, 6000, 70000, 5557 });

            Assert.Equal(new[] { 6000, 5555, 5557 }, result);
        }

        [Theory]
        [InlineData("127.0.0.1:5556", "127.0.0.1", 5556)]
        [InlineData("localhost:5555", "localhost", 5555)]
        public void TryParseEndpointSerial_ValidEndpoint_ReturnsParts(
            string serial,
            string expectedHost,
            int expectedPort)
        {
            bool parsed = AdbDeviceConnector.TryParseEndpointSerial(serial, out string host, out int port);

            Assert.True(parsed);
            Assert.Equal(expectedHost, host);
            Assert.Equal(expectedPort, port);
        }

        [Theory]
        [InlineData("")]
        [InlineData("emulator-5554")]
        [InlineData("127.0.0.1:0")]
        [InlineData("127.0.0.1:70000")]
        public void TryParseEndpointSerial_InvalidEndpoint_ReturnsFalse(string serial)
        {
            Assert.False(AdbDeviceConnector.TryParseEndpointSerial(serial, out _, out _));
        }
    }
}
