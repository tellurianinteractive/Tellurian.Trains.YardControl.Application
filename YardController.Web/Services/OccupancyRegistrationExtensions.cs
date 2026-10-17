using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Services;

public static class OccupancyRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers <typeparamref name="TInner"/> behind a <see cref="LatchingOccupancySectionService"/>,
        /// so the stretches declared for manual release hold their indication whichever detector is
        /// behind them — the LocoNet and Z21 hardware in production, the diagram's simulation in
        /// development. <typeparamref name="TInner"/> must already be registered as itself.
        /// </summary>
        public IServiceCollection AddLatchingOccupancy<TInner>() where TInner : class, IOccupancySectionService
        {
            services.AddSingleton(sp => new LatchingOccupancySectionService(
                sp.GetRequiredService<TInner>(),
                sp.GetRequiredService<IYardDataService>(),
                sp.GetRequiredService<ILogger<LatchingOccupancySectionService>>()));
            services.AddSingleton<IOccupancySectionService>(sp => sp.GetRequiredService<LatchingOccupancySectionService>());
            services.AddSingleton<IOccupancyHoldService>(sp => sp.GetRequiredService<LatchingOccupancySectionService>());
            return services;
        }
    }
}
