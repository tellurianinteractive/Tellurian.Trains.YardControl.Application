using System;
using System.Collections.Generic;
using System.Text;
using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class KeyInfoTests()
{
    [TestMethod]
    public void SerializationAndDeserialization()
    {
        var keyInfo = new ConsoleKeyInfo('A', ConsoleKey.A, true, false, false);
        var json = keyInfo.Serialize();
        var deserialized = ConsoleKeyInfo.Deserialize(json);
        Assert.AreEqual(keyInfo.Key, deserialized.Key);
        Assert.AreEqual(keyInfo.KeyChar, deserialized.KeyChar);
        Assert.AreEqual(keyInfo.Modifiers, deserialized.Modifiers);
    }
}
