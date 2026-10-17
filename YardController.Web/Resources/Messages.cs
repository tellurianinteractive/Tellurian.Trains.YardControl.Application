using System.Globalization;
using Tellurian.Trains.YardController.Model.Control;

namespace YardController.Web.Resources;

internal static partial class Messages
{
    /// <summary>
    /// Why a press on the button panel was refused, in the operator's language.
    /// <para>
    /// The language is passed rather than taken from the ambient culture: a panel repaints on
    /// whichever thread raised the event, where <c>CurrentUICulture</c> answers with the machine's
    /// own culture and not the one the yard is being worked in.
    /// </para>
    /// </summary>
    internal static string PanelRefusal(PanelEntryRejection rejection, string language)
    {
        var name = rejection switch
        {
            PanelEntryRejection.NotImplemented => nameof(PanelNotSupportedYet),
            PanelEntryRejection.NoSuchNumber => nameof(PanelNoSuchNumber),
            PanelEntryRejection.Incomplete => nameof(PanelCommandIncomplete),
            PanelEntryRejection.CommandAlreadyChosen => nameof(PanelCommandAlreadyChosen),
            PanelEntryRejection.ButtonSetLocked => nameof(PanelButtonSetLocked),
            PanelEntryRejection.WildcardNotAccepted => nameof(PanelWildcardNotAccepted),
            PanelEntryRejection.WildcardNotFirst => nameof(PanelWildcardNotFirst),
            PanelEntryRejection.WildcardNotImplemented => nameof(PanelWildcardNotSupportedYet),
            PanelEntryRejection.NothingToSettle => nameof(PanelNothingToSettle),
            PanelEntryRejection.NoRoleInEntry => nameof(PanelKeyHasNoRole),
            _ => "",
        };
        if (name.Length == 0) return "";

        var culture = TryCulture(language);
        return ResourceManager.GetString(name, culture) ?? name;
    }

    private static CultureInfo? TryCulture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return Culture;
        try { return CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { return Culture; }
    }

    internal static string LocalizedPosition(PointPosition position) => position switch
    {
        PointPosition.Straight => PositionStraight,
        PointPosition.Diverging => PositionDiverging,
        _ => position.ToString()
    };
}
