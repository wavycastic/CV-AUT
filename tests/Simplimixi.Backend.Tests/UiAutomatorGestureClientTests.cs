using System.Text.Json.Nodes;
using CvAut.Adb;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class UiAutomatorGestureClientTests
    {
        [Fact]
        public void BuildPinchParameters_ProducesAotSafeSelectorPayload()
        {
            JsonArray parameters = UiAutomatorGestureClient.BuildPinchParameters(90, 15);

            Assert.Equal(3, parameters.Count);
            var selector = Assert.IsType<JsonObject>(parameters[0]);
            Assert.Equal(0, selector["mask"]!.GetValue<int>());
            Assert.Empty(Assert.IsType<JsonArray>(selector["childOrSibling"]));
            Assert.Empty(Assert.IsType<JsonArray>(selector["childOrSiblingSelector"]));
            Assert.Equal(90, parameters[1]!.GetValue<int>());
            Assert.Equal(15, parameters[2]!.GetValue<int>());
        }

        [Fact]
        public void BuildRequest_ProducesJsonRpcEnvelope()
        {
            JsonObject request = UiAutomatorGestureClient.BuildRequest(
                "pinchIn",
                UiAutomatorGestureClient.BuildPinchParameters(100, 20));

            Assert.Equal("2.0", request["jsonrpc"]!.GetValue<string>());
            Assert.Equal(1, request["id"]!.GetValue<int>());
            Assert.Equal("pinchIn", request["method"]!.GetValue<string>());
            Assert.IsType<JsonArray>(request["params"]);
        }
    }
}
