using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class BackendDependencyRegistrationTests
    {
        [Fact]
        public void AddCvAutBackend_RegistersAbstractionsAsSingletons()
        {
            var services = new ServiceCollection();

            services.AddCvAutBackend("Config/test_config.json");

            var provider = services.BuildServiceProvider();

            var adb = provider.GetRequiredService<IADBHelper>();
            var vision = provider.GetRequiredService<IVisionEngine>();
            var config = provider.GetRequiredService<IConfigService>();

            Assert.IsType<ADBHelper>(adb);
            Assert.IsType<VisionEngine>(vision);
            Assert.IsType<ConfigService>(config);

            Assert.Same(adb, provider.GetRequiredService<IADBHelper>());
            Assert.Same(vision, provider.GetRequiredService<IVisionEngine>());
            Assert.Same(config, provider.GetRequiredService<IConfigService>());
        }
    }
}
