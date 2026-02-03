using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Explicit registration of all keyboard input handlers.
    /// Adding a new handler requires adding it here - this is intentional.
    /// Provides compile-time verification and visible dependencies.
    /// </summary>
    public static class HandlerRegistry
    {
        /// <summary>
        /// Register all keyboard input handlers. Called once at mod initialization.
        /// </summary>
        public static void RegisterAll()
        {
            var handlers = new List<IKeyboardInputHandler>
            {
                // TextInput band - blocks all other input
                // TODO: Add text input handlers as they are migrated
                // Examples: ZoneRenameState, StorageRenameState, DialogTextFieldState

                // Modal band - overlays atop menus
                // TODO: Add modal handlers as they are migrated
                // Examples: QuantityMenuState, WindowlessInspectionState, ConfirmationDialogState

                // Menu band - primary menus (mutually exclusive)
                // TODO: Add menu handlers as they are migrated
                // Examples: CaravanFormationState, TradeNavigationState, QuestMenuState, AnimalsMenuState

                // Global band - fallback shortcuts
                // TODO: Add global shortcuts handler
                // Examples: GlobalShortcutsHandler (R, Alt+F, time announcements)
            };

            KeyboardInputRouter.Initialize(handlers);
        }
    }
}
