using Tellurian.Trains.YardController.Model.Control;
using Tellurian.Trains.YardController.Model.Control.Extensions;

namespace YardController.Tests;

[TestClass]
public class AdressesExtensionsTests
{
    #region Early Return Conditions (No Overlap Check Performed)

    [TestMethod]
    public void NullOffset_ReturnsFalse()
    {
        int[] addresses = [801, 802];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(null);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void EmptyAddresses_ReturnsFalse()
    {
        int[] addresses = [];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(1000);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void OffsetBelowMinimum_ReturnsFalse()
    {
        // MinLockAddressOffset is 100, so 1-99 should return false
        int[] addresses = [1, 200];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(50);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void OffsetAtMinimumMinusOne_ReturnsFalse()
    {
        // Edge case: offset = 99 (just below MinLockAddressOffset = 100)
        int[] addresses = [1, 200];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(PointCommand.MinLockAddressOffset - 1);

        Assert.IsFalse(result);
    }

    #endregion

    #region Zero Offset (Always Overlaps)

    [TestMethod]
    public void ZeroOffset_SingleAddress_ReturnsTrue()
    {
        // With offset 0: address 801 + 0 = 801 <= 801, so overlap
        int[] addresses = [801];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(0);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ZeroOffset_MultipleAddresses_ReturnsTrue()
    {
        // With offset 0: min 801 + 0 = 801 <= max 803, so overlap
        int[] addresses = [801, 802, 803];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(0);

        Assert.IsTrue(result);
    }

    #endregion

    #region Valid Offset - No Overlap

    [TestMethod]
    public void ValidOffset_NoOverlap_ReturnsFalse()
    {
        // Addresses: 801-803, Lock addresses: 1801-1803
        // Check: 801 + 1000 = 1801 > 803, no overlap
        int[] addresses = [801, 802, 803];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(1000);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void OffsetAtMinimum_NoOverlap_ReturnsFalse()
    {
        // Edge case: offset exactly at MinLockAddressOffset (100)
        // Addresses: 1-5, Lock addresses: 101-105
        // Check: 1 + 100 = 101 > 5, no overlap
        int[] addresses = [1, 2, 3, 4, 5];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(PointCommand.MinLockAddressOffset);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void SingleAddress_ValidOffset_ReturnsFalse()
    {
        // Single address 50, offset 100
        // Check: 50 + 100 = 150 > 50, no overlap
        int[] addresses = [50];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(100);

        Assert.IsFalse(result);
    }

    #endregion

    #region Valid Offset - With Overlap

    [TestMethod]
    public void ValidOffset_WithOverlap_ReturnsTrue()
    {
        // Addresses: 1-200, Lock addresses: 101-300
        // Check: 1 + 100 = 101 <= 200, overlap!
        int[] addresses = [1, 100, 200];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(100);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ValidOffset_ExactBoundaryOverlap_ReturnsTrue()
    {
        // Addresses: 1, 101. Lock addresses: 101, 201
        // Check: 1 + 100 = 101 <= 101, overlap (boundary case)
        int[] addresses = [1, 101];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(100);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ValidOffset_JustOverBoundary_ReturnsFalse()
    {
        // Addresses: 1, 100. Lock addresses: 101, 200
        // Check: 1 + 100 = 101 > 100, no overlap
        int[] addresses = [1, 100];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(100);

        Assert.IsFalse(result);
    }

    #endregion

    #region Negative Addresses (Uses Absolute Value)

    [TestMethod]
    public void NegativeAddresses_UsesAbsoluteValue_NoOverlap()
    {
        // Negative addresses should be treated as positive
        // Addresses: |-801|=801, |-802|=802, Lock addresses: 1801, 1802
        // Check: 801 + 1000 = 1801 > 802, no overlap
        int[] addresses = [-801, -802];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(1000);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void MixedSignAddresses_UsesAbsoluteValue_NoOverlap()
    {
        // Mixed positive and negative addresses
        // Addresses: |801|=801, |-802|=802, Lock addresses: 1801, 1802
        int[] addresses = [801, -802];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(1000);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void NegativeAddresses_WithOverlap_ReturnsTrue()
    {
        // Addresses: |-1|=1, |-200|=200, offset 100
        // Check: 1 + 100 = 101 <= 200, overlap
        int[] addresses = [-1, -200];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(100);

        Assert.IsTrue(result);
    }

    #endregion

    #region Edge Cases

    [TestMethod]
    public void LargeAddressRange_NoOverlap()
    {
        // Large addresses with appropriate offset
        int[] addresses = [9000, 9001, 9002];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(1000);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void SingleAddressAtZero_ZeroOffset_ReturnsTrue()
    {
        // Edge case: address 0 with offset 0
        // Check: 0 + 0 = 0 <= 0, overlap
        int[] addresses = [0];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(0);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void AddressesOutOfOrder_StillCalculatesCorrectly()
    {
        // Addresses not in order: [803, 801, 802]
        // Min=801, Max=803, offset=1000
        // Check: 801 + 1000 = 1801 > 803, no overlap
        int[] addresses = [803, 801, 802];

        var result = addresses.IsAdressesAndLockAdressesOverlaping(1000);

        Assert.IsFalse(result);
    }

    #endregion
}
