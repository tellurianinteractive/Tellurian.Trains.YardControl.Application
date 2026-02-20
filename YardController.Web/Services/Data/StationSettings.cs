namespace YardController.Web.Services.Data;

public record StationConfig
{
    public string Name { get; init; } = "";
    public string DataFolder { get; init; } = "Data";
}

public record StationSettings
{
    public List<StationConfig> Stations { get; init; } = [];
}
