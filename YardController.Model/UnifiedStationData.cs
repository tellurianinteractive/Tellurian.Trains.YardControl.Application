using Tellurian.Trains.YardController.Model.Control;

namespace Tellurian.Trains.YardController.Model;

/// <summary>
/// Complete station configuration parsed from a unified Station.txt file.
/// </summary>
public record UnifiedStationData(
    string Name,
    YardTopology Topology,
    IReadOnlyList<Point> Points,
    IReadOnlyList<TurntableTrack> TurntableTracks,
    IReadOnlyList<TrainRouteCommand> TrainRoutes,
    IReadOnlyList<SignalHardware> SignalAddresses,
    IReadOnlyList<ShuntingArea> ShuntingAreas,
    IReadOnlyList<OccupancySection> OccupancySections,
    ButtonLayout Buttons,
    LabelTranslationData? Translations,
    int LockAddressOffset,
    int LockReleaseDelaySeconds);

/// <summary>
/// Maps a signal name to its LocoNet hardware address and optional feedback address.
/// </summary>
public record SignalHardware(string SignalName, int Address, int? FeedbackAddress = null);

/// <summary>
/// Raw label translation data parsed from the [Translations] section.
/// </summary>
public record LabelTranslationData(IReadOnlyList<string> Languages, IReadOnlyList<string[]> Rows);
