namespace RimWorldAccess
{
    /// <summary>
    /// Contract for keyboard input handlers.
    /// Handlers must be explicitly registered via HandlerRegistry and are invoked by KeyboardInputRouter in priority order.
    /// </summary>
    public interface IKeyboardInputHandler
    {
        /// <summary>
        /// Priority band for this handler. Bands are coarse categories;
        /// handlers within the same band should be mutually exclusive.
        /// Lower value = higher priority (processed first).
        /// </summary>
        InputPriorityBand Priority { get; }

        /// <summary>
        /// Whether this handler should receive input.
        /// Router only invokes active handlers.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Handle keyboard input.
        /// </summary>
        /// <param name="context">Input context with key and modifiers</param>
        /// <returns>True if event was consumed (prevent further routing), false to continue routing</returns>
        bool HandleInput(KeyboardInputContext context);
    }
}
