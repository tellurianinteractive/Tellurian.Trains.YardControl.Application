namespace Tellurian.Trains.YardController.Model.Control.Extensions;

public static class TrainRouteStateExtensions
{
    extension(TrainRouteState state)
    {
        public bool IsSet => state == TrainRouteState.SetMain || state == TrainRouteState.SetShunting;
        public bool IsClear => state == TrainRouteState.Clear;
        public bool IsCancel => state == TrainRouteState.Cancel;
        public bool IsTeardown => state == TrainRouteState.Clear || state == TrainRouteState.Cancel;
    }
}
