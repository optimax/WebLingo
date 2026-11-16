using WebLingo.Core;
namespace WebLingo.Bytecode;

/// <summary>
/// Where a file's positions stand, as a WLBC file holds them so that a runtime error is located without the source
/// (specification, section 5.3, "As built"): for the program's own file (<paramref name="Source"/> null) or a file it
/// imports, named as its functions' <see cref="WebLingoFunction.Source"/>, the line and column of each position its
/// functions' spans start at, in the order of the positions.
/// </summary>
public sealed record WebLingoSourcePositions(string? Source, IReadOnlyList<WebLingoSourceLocation> Locations)
{
    /// <summary>
    /// A position's line and column: those given for it; for a position not given, those of the nearest given before it,
    /// the column moved on by the difference; line 1, column 1 before the first.
    /// </summary>
    public WebLingoSourceLocation GetLocation(int offset)
    {
        var (low, high, found) = (0, Locations.Count - 1, -1);
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (Locations[middle].Offset <= offset)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }
        if (found < 0) return new WebLingoSourceLocation(offset, 1, 1);
        var nearest = Locations[found];
        return nearest.Offset == offset ? nearest : nearest with { Offset = offset, Column = nearest.Column + (offset - nearest.Offset) };
    }
}
