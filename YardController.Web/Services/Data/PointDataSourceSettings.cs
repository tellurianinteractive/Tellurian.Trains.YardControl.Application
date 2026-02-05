namespace YardController.Web.Services.Data;

public record PointDataSourceSettings
{
    public string Path { get; init; } = "Data/Points.txt";
}
