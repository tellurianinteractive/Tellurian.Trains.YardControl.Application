namespace Tellurian.Trains.YardController.Model.Control;

/// <summary>
/// A message from the input pipeline, together with who it is for. The yard is worked from two
/// screens at once — a dispatcher's and a shunter's — and each operator reads the answers to
/// their own commands: a route the other one set says nothing they can act on, and a refusal
/// belongs to the person who was refused.
/// <para>
/// Only the words are addressed. What the message reports about the yard is drawn on both
/// diagrams whoever caused it, because the two are looking at the same tracks.
/// </para>
/// </summary>
public interface IOperatorMessage
{
    /// <summary>
    /// The role the message answers, or null for one addressed to both. Null is for what the
    /// other operator must be told whatever caused it: the whole yard cleared at a stroke, a
    /// shunting area handed over or taken back, a shunter asking for one.
    /// </summary>
    OperatorRole? Audience { get; }
}
