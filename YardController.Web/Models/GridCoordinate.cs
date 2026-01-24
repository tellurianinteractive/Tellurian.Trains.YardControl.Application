namespace YardController.Web.Models;

public readonly record struct GridCoordinate(int Row, int Column) : IComparable<GridCoordinate>
{
    /// <summary>
    /// Compares coordinates for ordering: column first, then row.
    /// Links should go left-to-right (increasing column).
    /// </summary>
    public int CompareTo(GridCoordinate other)
    {
        var colCompare = Column.CompareTo(other.Column);
        return colCompare != 0 ? colCompare : Row.CompareTo(other.Row);
    }

    public static bool operator <(GridCoordinate left, GridCoordinate right) => left.CompareTo(right) < 0;
    public static bool operator >(GridCoordinate left, GridCoordinate right) => left.CompareTo(right) > 0;
    public static bool operator <=(GridCoordinate left, GridCoordinate right) => left.CompareTo(right) <= 0;
    public static bool operator >=(GridCoordinate left, GridCoordinate right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Parse "x.y" format where x=row, y=column
    /// </summary>
    public static GridCoordinate Parse(string s)
    {
        var dotIndex = s.IndexOf('.');
        if (dotIndex < 0)
            throw new FormatException($"Invalid coordinate format: '{s}'. Expected 'row.column'.");

        var rowPart = s[..dotIndex];
        var colPart = s[(dotIndex + 1)..];

        return new GridCoordinate(int.Parse(rowPart), int.Parse(colPart));
    }

    public static bool TryParse(string s, out GridCoordinate result)
    {
        result = default;
        var dotIndex = s.IndexOf('.');
        if (dotIndex < 0) return false;

        if (!int.TryParse(s[..dotIndex], out var row)) return false;
        if (!int.TryParse(s[(dotIndex + 1)..], out var col)) return false;

        result = new GridCoordinate(row, col);
        return true;
    }

    public override string ToString() => $"{Row}.{Column}";
}
