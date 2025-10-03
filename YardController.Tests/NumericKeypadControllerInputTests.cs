using System;
using System.Collections.Generic;
using System.Text;
using Tellurian.Trains.YardController;

namespace YardController.Tests;

[TestClass]
public class NumericKeypadControllerInputTests
{
    [TestMethod]
    public async Task StartsAndStops()
    {
        var trainPathDataSource = new TextFileTrainPathDataSource("TestData/TrainPaths.txt");
        var sut = new NumericKeypadControllerInputs(null!, null!, trainPathDataSource);
        await sut.StartAsync(default);
        await sut.StopAsync(default);
        sut.Dispose();
    }
}
