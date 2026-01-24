
namespace Tellurian.Trains.YardController.Data;

internal static class AdressesExtensions
{
    extension(int[] adresses)
    {
        public bool IsAdressesAndLockAdressesOverlaping(int? lockAddressOffset)
        {
            if (lockAddressOffset is null || (lockAddressOffset > 0 && lockAddressOffset < PointCommand.MinLockAddressOffset) || adresses.Length == 0) return false;
            var positiveAdresses = adresses.Select(a => Math.Abs(a)).ToArray();
            return positiveAdresses.Min() + lockAddressOffset.Value <= positiveAdresses.Max();
        }
    }
}
