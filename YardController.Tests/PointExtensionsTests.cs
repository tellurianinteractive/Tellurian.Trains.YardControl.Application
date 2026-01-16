using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class PointExtensionsTests
{
    #region ToPoint String Extension Tests

    [TestMethod]
    public void ToPoint_ParsesValidFormat()
    {
        var point = "1:801".ToPoint();

        Assert.AreEqual(1, point.Number);
        Assert.HasCount(1, point.Addresses);
        Assert.Contains(801, point.Addresses);
    }

    [TestMethod]
    public void ToPoint_ParsesMultipleAddresses()
    {
        var point = "5:801,802,803".ToPoint();

        Assert.AreEqual(5, point.Number);
        Assert.HasCount(3, point.Addresses);
        Assert.Contains(801, point.Addresses);
        Assert.Contains(802, point.Addresses);
        Assert.Contains(803, point.Addresses);
    }

    [TestMethod]
    public void ToPoint_ReturnsUndefined_ForNull()
    {
        var point = ((string?)null).ToPoint();

        Assert.AreEqual(0, point.Number);
        Assert.IsEmpty(point.Addresses);
    }

    [TestMethod]
    public void ToPoint_ReturnsUndefined_ForEmptyString()
    {
        var point = "".ToPoint();

        Assert.AreEqual(0, point.Number);
        Assert.IsEmpty(point.Addresses);
    }

    [TestMethod]
    public void ToPoint_ReturnsUndefined_ForSingleChar()
    {
        var point = "1".ToPoint();

        Assert.AreEqual(0, point.Number);
        Assert.IsEmpty(point.Addresses);
    }

    [TestMethod]
    public void ToPoint_ReturnsUndefined_ForMissingColon()
    {
        var point = "1801".ToPoint();

        Assert.AreEqual(0, point.Number);
        Assert.IsEmpty(point.Addresses);
    }

    [TestMethod]
    public void ToPoint_ReturnsUndefined_ForInvalidNumber()
    {
        var point = "abc:801".ToPoint();

        Assert.AreEqual(0, point.Number);
    }

    [TestMethod]
    public void ToPoint_ReturnsUndefined_ForInvalidAddresses()
    {
        var point = "1:abc".ToPoint();

        Assert.AreEqual(0, point.Number);
        Assert.IsEmpty(point.Addresses);
    }

    [TestMethod]
    public void ToPoint_IgnoresInvalidAddressesInList()
    {
        var point = "1:801,abc,802".ToPoint();

        Assert.AreEqual(1, point.Number);
        Assert.HasCount(2, point.Addresses);
        Assert.Contains(801, point.Addresses);
        Assert.Contains(802, point.Addresses);
    }

    [TestMethod]
    public void ToPoint_HandlesWhitespace()
    {
        var point = " 1 : 801 , 802 ".ToPoint();

        Assert.AreEqual(1, point.Number);
        Assert.HasCount(2, point.Addresses);
    }

    #endregion

    #region IsUndefined Tests

    [TestMethod]
    public void IsUndefined_ReturnsTrue_ForZeroNumber()
    {
        var point = new Point(0, [801], 0);

        Assert.IsTrue(point.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsTrue_ForEmptyAddresses()
    {
        var point = new Point(1, [], 0);

        Assert.IsTrue(point.IsUndefined);
    }

    [TestMethod]
    public void IsUndefined_ReturnsFalse_ForValidPoint()
    {
        var point = new Point(1, [801], 0);

        Assert.IsFalse(point.IsUndefined);
    }

    #endregion

    #region AddressesFor Dictionary Extension Tests

    [TestMethod]
    public void AddressesFor_ReturnsAddresses_WhenPointExists()
    {
        var points = new Dictionary<int, Point>
        {
            { 1, new Point(1, [801, 802], 0) }
        };

        var addresses = points.AddressesFor(1);

        Assert.HasCount(2, addresses);
        Assert.Contains(801, addresses);
        Assert.Contains(802, addresses);
    }

    [TestMethod]
    public void AddressesFor_ReturnsEmpty_WhenPointDoesNotExist()
    {
        var points = new Dictionary<int, Point>
        {
            { 1, new Point(1, [801], 0) }
        };

        var addresses = points.AddressesFor(99);

        Assert.IsEmpty(addresses);
    }

    [TestMethod]
    public void AddressesFor_ReturnsEmpty_ForEmptyDictionary()
    {
        var points = new Dictionary<int, Point>();

        var addresses = points.AddressesFor(1);

        Assert.IsEmpty(addresses);
    }

    #endregion
}
