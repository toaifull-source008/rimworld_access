namespace RimWorldAccess
{
    /// <summary>
    /// Coarse priority bands for keyboard input routing.
    /// Lower value = higher priority.
    ///
    /// Handlers within the same band should be mutually exclusive
    /// (never active simultaneously). If two handlers in the same band
    /// could be active together, one belongs in a different band.
    /// </summary>
    public enum InputPriorityBand
    {
        /// <summary>
        /// Text input fields. Blocks all other input while active.
        /// Examples: Zone rename, dialog text fields, search boxes.
        /// </summary>
        TextInput = 0,

        /// <summary>
        /// Modal overlays that appear atop other UI.
        /// Examples: Quantity selector, inspection overlay, confirmation dialogs.
        /// </summary>
        Modal = 1,

        /// <summary>
        /// Primary menus and dialogs.
        /// Examples: Caravan formation, trade screen, quest menu, animals menu.
        /// These are mutually exclusive - only one primary menu is open at a time.
        /// </summary>
        Menu = 2,

        /// <summary>
        /// Global shortcuts available when no menu is focused.
        /// Examples: R (draft), Alt+F (unforbid), time announcements.
        /// </summary>
        Global = 3
    }
}
