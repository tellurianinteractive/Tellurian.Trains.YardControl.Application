namespace Tellurian.Trains.YardController.Model.Control.Extensions;

public static class OperatorMessageExtensions
{
    extension(IOperatorMessage message)
    {
        /// <summary>True where the message is this role's to read: its own, or addressed to both.</summary>
        public bool IsFor(OperatorRole role) => message.Audience is null || message.Audience == role;
    }
}
