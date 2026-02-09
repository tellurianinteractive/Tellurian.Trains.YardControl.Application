namespace YardController.Web.Services.Data;

public record SerialPortSettings
{
    public string PortName { get; init; } = "COM3";
    public int BaudRate { get; init; } = 57600;
}
