namespace YardController.Web.Components;

/// <summary>
/// The break drawn across a track line where it is closed off — at a point, the arm it is turned
/// away from; at a plain crossing, every arm of a way that is not laid. EBICOS shows both as a gap
/// in the rails rather than as a symbol of their own, so both are a patch of background laid over
/// the track.
/// </summary>
/// <remarks>
/// The two are not cut the same way, because the two do not stand the same way on the diagram.
/// A point's arms are the yard's own geometry — one level, one diagonal — and its break belongs to
/// the switch it leaves, so it is measured in the diagram's frame: level top and bottom, cut
/// parallel to the diagonal arm. A plain crossing has no level arm to measure against and no switch
/// to belong to, so its break is measured in the closed arm's own frame instead. Serving both from
/// the one measurement is what turned every point's break the wrong way round.
/// </remarks>
internal static class TrackCutOff
{
    /// <summary>
    /// The four corners of the break across a point's closed arm, or null where there is no arm to
    /// cut. The patch is a parallelogram standing a track's thickness tall in the diagram's own
    /// frame, its other pair of edges parallel to the diagonal arm.
    /// </summary>
    /// <param name="sx">The switch point, where the closed arm starts.</param>
    /// <param name="sy">Likewise.</param>
    /// <param name="towardX">A point along the closed arm, which is the direction the patch is set off in.</param>
    /// <param name="towardY">Likewise.</param>
    /// <param name="alongX">
    /// The direction the cut is made parallel to: the point's explicit end, which the topology
    /// always spells as the diagonal arm. Given as a vector from <paramref name="sx"/>,<paramref name="sy"/>.
    /// </param>
    /// <param name="alongY">Likewise.</param>
    /// <param name="trackThickness">How thick the rails are drawn, which is what the cut must span.</param>
    /// <remarks>
    /// One rule serves both positions of the point, and it is the diagram that makes it work.
    /// Standing diverging, the closed arm is the level one: the level edges then lie exactly along
    /// its rails and the diagonal edges cut across it, slanting with the arm that is now open.
    /// Standing straight, the closed arm is the diagonal one: the diagonal edges now run along it
    /// and the level edges are the cut, so the arm ends level with the track that passes over it —
    /// which is the break a point standing straight has always drawn.
    /// </remarks>
    public static (double X, double Y)[]? AtPoint(
        double sx, double sy, double towardX, double towardY, double alongX, double alongY, double trackThickness)
    {
        var armDx = towardX - sx;
        var armDy = towardY - sy;
        var armLen = Math.Sqrt(armDx * armDx + armDy * armDy);
        var alongLen = Math.Sqrt(alongX * alongX + alongY * alongY);
        if (armLen <= 0 || alongLen <= 0) return null;

        var tx = armDx / armLen;
        var ty = armDy / armLen;
        var ax = alongX / alongLen;
        var ay = alongY / alongLen;

        // A cut direction that is itself level has no slant to give the patch, and its edges would
        // never meet the level ones. Nothing in the topology asks for it: the explicit end of a
        // point is its diagonal arm.
        if (Math.Abs(ay) <= 0.01) return null;

        var halfHeight = trackThickness / 2.0;
        // How far the level edges reach, so the gap between the slanting ones measures a track's
        // thickness plus one across them; and how far a slanting edge slides sideways between the
        // top of the track and the bottom.
        var halfWidth = (halfHeight + 0.5) / Math.Abs(ay);
        var shear = halfHeight * ax / ay;

        (double X, double Y)[] corners =
        [
            (-shear - halfWidth, -halfHeight),
            (-shear + halfWidth, -halfHeight),
            (shear + halfWidth, halfHeight),
            (shear - halfWidth, halfHeight),
        ];

        // Set the patch out along the closed arm until its nearest corner stands a pixel clear of
        // the switch, so the break reads as a gap in the track rather than as a bite out of it.
        var nearest = corners.Min(corner => corner.X * tx + corner.Y * ty);
        var centreX = sx + tx * (1.0 - nearest);
        var centreY = sy + ty * (1.0 - nearest);

        return [.. corners.Select(corner => (centreX + corner.X, centreY + corner.Y))];
    }

    /// <summary>
    /// The four corners of the break across a plain crossing's closed arm, or null where there is
    /// no arm to cut.
    /// </summary>
    /// <param name="sx">The crossing, where the closed arm starts.</param>
    /// <param name="sy">Likewise.</param>
    /// <param name="towardX">A point along the closed arm, which is the direction the patch is set off in.</param>
    /// <param name="towardY">Likewise.</param>
    /// <param name="alongX">
    /// The direction the cut is made parallel to, which at a plain crossing is the track that
    /// passes over. Given as a vector from <paramref name="sx"/>,<paramref name="sy"/>.
    /// </param>
    /// <param name="alongY">Likewise.</param>
    /// <param name="trackThickness">How thick the rails are drawn, which is what the cut must span.</param>
    /// <remarks>
    /// <para>
    /// A crossing's arms are all diagonals, and each is skew to the track that cuts it, so the patch
    /// is measured in the closed arm's own frame: one pair of edges runs <em>along the arm</em>, a
    /// track's thickness apart, so the break follows the rails whichever way they run; the other
    /// pair runs parallel to the cut direction, a track's thickness plus one apart, so the gap reads
    /// as wide as it should. Measuring the first pair vertically instead — which is right only for
    /// an arm that happens to run level — is what left the break on a 45° arm looking skewed, its
    /// ends horizontal against a track that is not.
    /// </para>
    /// <para>
    /// The patch begins where the track that passes over ends, not where the closed arm leaves the
    /// crossing: the two ways cross, so the first stretch of the closed arm lies under the other's
    /// rails, and a break laid there paints over the way that is laid.
    /// </para>
    /// <para>
    /// Where the cut direction is parallel to the arm there is nothing to cut along, so the break is
    /// made square across it instead.
    /// </para>
    /// </remarks>
    public static (double X, double Y)[]? AtCrossing(
        double sx, double sy, double towardX, double towardY, double alongX, double alongY, double trackThickness)
    {
        var armDx = towardX - sx;
        var armDy = towardY - sy;
        var armLen = Math.Sqrt(armDx * armDx + armDy * armDy);
        if (armLen <= 0) return null;

        // The arm, and the normal to it. Everything below is measured in that frame rather than in
        // the screen's, which is what keeps the break square to the track at any angle.
        var tx = armDx / armLen;
        var ty = armDy / armLen;
        var nx = -ty;
        var ny = tx;

        var alongLen = Math.Sqrt(alongX * alongX + alongY * alongY);
        var ax = alongLen > 0 ? alongX / alongLen : nx;
        var ay = alongLen > 0 ? alongY / alongLen : ny;

        var across = ax * nx + ay * ny;
        if (Math.Abs(across) <= 0.01) { ax = nx; ay = ny; across = 1.0; }
        var alongArm = ax * tx + ay * ty;

        var halfWidth = trackThickness / 2.0;
        // How far a cut line slides along the arm between one rail edge and the other, and how far
        // apart the two cut lines stand so the gap between them is a track's thickness plus one.
        var shear = halfWidth * alongArm / across;
        var halfSpan = (trackThickness + 1.0) / (2.0 * Math.Abs(across));

        // The break's near edge runs parallel to the track that passes over, so it stands the same
        // distance from it its whole length: set that to half a track's thickness and a pixel, and
        // the break begins just clear of the rails it must not paint over. Measuring a pixel from
        // the crossing itself instead is what took a bite out of the way that is laid — the two
        // ways cross, so the closed arm starts inside the other's rails and stays there for as far
        // as they are thick. The shallower the crossing, the further along the closed arm that is,
        // which is why the clearance is divided by the angle between them rather than added to.
        var near = (halfWidth + 1.0) / Math.Abs(across);
        var far = near + 2.0 * halfSpan;

        return
        [
            At(near + shear, halfWidth),
            At(far + shear, halfWidth),
            At(far - shear, -halfWidth),
            At(near - shear, -halfWidth),
        ];

        (double X, double Y) At(double alongTrack, double acrossTrack) =>
            (sx + alongTrack * tx + acrossTrack * nx, sy + alongTrack * ty + acrossTrack * ny);
    }
}
