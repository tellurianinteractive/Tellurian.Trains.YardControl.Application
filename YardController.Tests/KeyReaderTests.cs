using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class KeyReaderTests
{
    ServiceProvider ServiceProvider = default!;

    [TestInitialize]
    public void TestInitialize()
    {
        ServiceProvider = ServiceProvider.InstanceForTesting;
    }

    [TestMethod]
    public async Task UdpKeyReaderStartsAndStops()
    {
        var logger = ServiceProvider.GetRequiredService<ILogger<UdpKeyReader>>();
        var sut = new UdpKeyReader(logger);
        await Task.Delay(100, default);
        await sut.DisposeAsync();
    }
}
