using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
#if DEBUG
    /// <summary>
    /// DEBUG-only verification that all IKeyboardInputHandler implementations are registered.
    /// Runs at startup to catch forgotten registrations during development.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HandlerRegistryVerification
    {
        static HandlerRegistryVerification()
        {
            try
            {
                VerifyAllHandlersRegistered();
            }
            catch (Exception ex)
            {
                Log.Error($"[HandlerRegistryVerification] Verification failed: {ex}");
            }
        }

        private static void VerifyAllHandlersRegistered()
        {
            // Find all IKeyboardInputHandler implementations
            var allHandlerTypes = typeof(HandlerRegistry).Assembly
                .GetTypes()
                .Where(t => typeof(IKeyboardInputHandler).IsAssignableFrom(t))
                .Where(t => !t.IsInterface && !t.IsAbstract)
                .ToHashSet();

            // Get registered handler types
            var registeredTypes = KeyboardInputRouter.GetRegisteredTypes();

            // Report unregistered handlers
            var unregistered = allHandlerTypes.Except(registeredTypes).ToList();

            if (unregistered.Count > 0)
            {
                Log.Warning($"[HandlerRegistryVerification] Found {unregistered.Count} unregistered handlers:");
                foreach (var type in unregistered)
                {
                    Log.Warning($"  - {type.Name} implements IKeyboardInputHandler but is not registered in HandlerRegistry!");
                }
                Log.Warning("[HandlerRegistryVerification] Add these handlers to HandlerRegistry.RegisterAll() or remove the IKeyboardInputHandler interface if not needed.");
            }
            else
            {
                Log.Message($"[HandlerRegistryVerification] All {allHandlerTypes.Count} handlers are properly registered.");
            }
        }
    }
#endif
}
