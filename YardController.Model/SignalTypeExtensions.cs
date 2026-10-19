namespace Tellurian.Trains.YardController.Model;

public static class SignalTypeExtensions
{
    extension(SignalType type)
    {
        /// <summary>
        /// A shunting dwarf signal can only authorise shunting movements,
        /// never a main train route. So can a buffer stop, which ends one.
        /// </summary>
        public bool IsShuntingOnly => type is SignalType.ShuntingDwarf or SignalType.BufferStop or SignalType.FictitiousShunting;

        /// <summary>
        /// A main signal has no dwarf part and therefore no shunting aspect,
        /// so it stays at stop when a shunting route is set over it.
        /// </summary>
        public bool IsMainOnly => type is SignalType.OutboundMain or SignalType.InboundMain;

        /// <summary>
        /// A buffer stop (stoppbock) is the physical end of a dead-end track, not a signal:
        /// it can only be the end point of a movement and never shows a proceed aspect.
        /// </summary>
        public bool IsBufferStop => type == SignalType.BufferStop;

        /// <summary>
        /// A fictitious signal is an information point (informationspunkt, §6.2.23): a place a
        /// route can start or end at that has no signal standing there, so a tap on it is only
        /// ever part of choosing a route. <c>f</c> serves train and shunting routes alike,
        /// <c>g</c> shunting routes only (see <see cref="IsShuntingOnly"/>).
        /// </summary>
        public bool IsFictitious => type is SignalType.Fictitious or SignalType.FictitiousShunting;
    }
}
