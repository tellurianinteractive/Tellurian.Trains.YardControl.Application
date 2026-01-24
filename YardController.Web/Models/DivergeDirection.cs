namespace YardController.Web.Models;

/// <summary>
/// Direction a point diverges relative to the main line.
/// </summary>
public enum DivergeDirection
{
    /// <summary>Forward (>) - diverges right, straight arm to the right</summary>
    Forward,

    /// <summary>Backward (&lt;) - diverges left, straight arm to the left</summary>
    Backward
}
