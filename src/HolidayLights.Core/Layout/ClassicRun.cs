namespace HolidayLights.Core.Layout;

/// <summary>
/// The 5.4 run of side bulbs along one strip: the fitting
/// loop of <c>TopBulbWorld::Layout</c> and its siblings, the fractional gap, and the positions of
/// <c>HorizontalBulbWorld::Render</c> / <c>VerticalBulbWorld::Render</c>.
/// </summary>
/// <remarks>
/// <para>Bulb <c>i</c> takes <c>spacing + cellLength + spacing</c>; bulbs are packed from the start until the next one would
/// pass the limit; the leftover is spread as a double-precision gap after every bulb, including the last, and every
/// position is truncated toward zero (MSVC <c>__ftol</c>). Doubles reproduce the 53-bit x87 arithmetic bit for bit.</para>
/// <para>Positions are relative to the strip start. 5.4 keeps them in 16-bit registers; strip-relative positions stay far
/// below 32,768 on any real display, so the wrap-around can never happen and is not emulated.</para>
/// </remarks>
internal static class ClassicRun
{
    /// <summary>Fits side bulbs between two positions (the 5.4 layout loop).</summary>
    /// <param name="edge">The measured edge.</param>
    /// <param name="firstIndex">The position <c>i</c> of the first bulb (0 for a strip; continued across outline pieces).</param>
    /// <param name="start">The first free position (after the start corner and its spacing).</param>
    /// <param name="limit">The end of the free run (before the end corner).</param>
    /// <returns>The number of bulbs, where the packed run ends and the strip thickness the side bulbs need.</returns>
    public static RunFit Fit(EdgeBulbs edge, int firstIndex, int start, int limit)
    {
        int position = start;
        int count = 0;
        int thickness = 0;
        if (edge.TypeCount == 0)
        {
            return new RunFit(0, position, 0);
        }

        for (int index = firstIndex; ; index++)
        {
            EdgeBulbType type = edge.TypeAt(index);
            SizeI cell = type.CellSize(edge.FlavorAt(index));
            int step = edge.LengthOf(cell) + 2 * type.Spacing;
            if (step <= 0 || position + step > limit)
            {
                break;
            }

            position += step;
            thickness = Math.Max(thickness, edge.ThicknessOf(cell));
            count++;
        }

        return new RunFit(count, position, thickness);
    }

    /// <summary>The fractional gap after every bulb: <c>(limit - end) / count</c>, 0 when the run fits exactly.</summary>
    /// <param name="fit">The fitted run.</param>
    /// <param name="limit">The end of the free run.</param>
    /// <returns>The gap; <see cref="double.PositiveInfinity"/> when no bulb fits (<see cref="StripLayout.Gap"/>).</returns>
    public static double Gap(RunFit fit, int limit) =>
        fit.Count == 0 ? double.PositiveInfinity : fit.End == limit ? 0.0 : (double)(limit - fit.End) / fit.Count;

    /// <summary>Places the fitted bulbs (the 5.4 render loop) and counts the chase index.</summary>
    /// <param name="edge">The measured edge.</param>
    /// <param name="firstIndex">The position <c>i</c> of the first bulb.</param>
    /// <param name="firstChase">The chase counter <c>k</c> of the first bulb (1 for a strip; continued across outline pieces).</param>
    /// <param name="count">The number of bulbs (from <see cref="Fit"/>).</param>
    /// <param name="start">The strip-relative start (the start corner's width plus its spacing, or 0).</param>
    /// <param name="gap">The gap (from <see cref="Gap"/>).</param>
    /// <param name="nextChase">The chase counter after the last bulb: the first bulb's counter on the next piece of the edge.</param>
    /// <returns>The bulbs in strip order.</returns>
    public static RunBulb[] Place(EdgeBulbs edge, int firstIndex, int firstChase, int count, int start, double gap, out int nextChase)
    {
        var bulbs = new RunBulb[count];
        double position = start;
        int chase = firstChase;
        for (int j = 0; j < count; j++)
        {
            int index = firstIndex + j;
            EdgeBulbType type = edge.TypeAt(index);
            int flavor = edge.FlavorAt(index);
            SizeI cell = type.CellSize(flavor);
            position += type.Spacing;
            bulbs[j] = new RunBulb(type.Id, index, flavor, cell, (int)position, chase);
            position = (type.Spacing + position) + edge.LengthOf(cell) + gap;

            // The chase counter skips single-phase bulbs (spacers, holly, snowmen).
            if (type.PhaseCount > 1)
            {
                chase++;
            }
        }

        nextChase = chase;
        return bulbs;
    }
}

/// <summary>The result of fitting a run.</summary>
/// <param name="Count">Number of side bulbs that fit.</param>
/// <param name="End">Where the packed bulbs end (relative to the strip start).</param>
/// <param name="Thickness">The largest cross-edge cell extent of the fitted bulbs.</param>
internal readonly record struct RunFit(int Count, int End, int Thickness);

/// <summary>One placed side bulb of a run.</summary>
/// <param name="BulbId">The arrangement id.</param>
/// <param name="Index">The position <c>i</c> along the edge.</param>
/// <param name="Flavor">The raw flavor <c>i / n</c>.</param>
/// <param name="Cell">The scaled cell size.</param>
/// <param name="Offset">The strip-relative position along the edge (truncated).</param>
/// <param name="ChaseIndex">The 5.4 chase counter <c>k</c>.</param>
internal readonly record struct RunBulb(string BulbId, int Index, int Flavor, SizeI Cell, int Offset, int ChaseIndex);
