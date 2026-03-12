namespace Tellurian.Trains.YardController.Model.Control;

[Flags]
public enum AccessoryMessageKind
{
    Command = 1,
    Notification = 2,
    Both = Command | Notification
}
