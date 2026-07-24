using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CvAut.Backend.Tests
{
    public class BackendDependencyRegistrationTests
    {
        [Fact]
        public void AddCvAutBackend_RegistersConcreteAdbAndInterfaceAsSingletons()
        {
            var services = new ServiceCollection();

            services.AddCvAutBackend("Config/test_config.json");

            ServiceDescriptor concrete = Assert.Single(
                services.Where(descriptor => descriptor.ServiceType == typeof(ADBHelper)));
            ServiceDescriptor abstraction = Assert.Single(
                services.Where(descriptor => descriptor.ServiceType == typeof(IADBHelper)));

            Assert.Equal(ServiceLifetime.Singleton, concrete.Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, abstraction.Lifetime);
            Assert.NotNull(concrete.ImplementationFactory);
            Assert.NotNull(abstraction.ImplementationFactory);
        }
    }
}
