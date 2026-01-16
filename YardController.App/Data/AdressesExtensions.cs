
namespace Tellurian.Trains.YardController.Data;

internal static class AdressesExtensions
{
    extension(int[] adresses)
    {
        public bool IsAdressesAndLockAdressesOverlaping(int? lockAddressOffset) =>
            lockAddressOffset > 0 &&
            adresses.Length != 0 &&
            adresses.Min() + lockAddressOffset.Value <= adresses.Max();
    }
}
