namespace YardController.Web.Services.Data;

public record SignalDataSourceSettings
{
    public string Path { get; init; } = "Data/Signals.txt";
}
