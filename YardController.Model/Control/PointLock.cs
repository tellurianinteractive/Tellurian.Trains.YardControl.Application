namespace Tellurian.Trains.YardController.Model.Control;

public record struct PointLock(PointCommand PointCommand, bool Committed);
