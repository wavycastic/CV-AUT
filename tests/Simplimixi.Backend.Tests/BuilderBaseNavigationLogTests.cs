using CvAut;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class BuilderBaseNavigationLogTests
    {
        [Fact]
        public void Format_UsesStableBbNavSchema()
        {
            string line = BuilderBaseNavigationLog.Format(
                phase: "switch",
                status: "tap_switch",
                target: "builder_base",
                attempt: 2,
                details: "template=boat score=0.91 tap=(42,77)");

            Assert.Equal("[BB_NAV] phase=switch status=tap_switch target=builder_base attempt=2 details=\"template=boat score=0.91 tap=(42,77)\"", line);
        }

        [Fact]
        public void Format_SanitizesMultilineDetails()
        {
            string line = BuilderBaseNavigationLog.Format(
                phase: "verify target",
                status: "retry",
                target: "main village",
                details: "line1\r\n\"line2\"");

            Assert.Equal("[BB_NAV] phase=verify_target status=retry target=main_village details=\"line1  'line2'\"", line);
        }

        [Fact]
        public void Format_UsesUnknownForBlankRequiredTokens()
        {
            string line = BuilderBaseNavigationLog.Format("", " ", "\t");

            Assert.Equal("[BB_NAV] phase=unknown status=unknown target=unknown", line);
        }
    }
}
