using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Central registry and dispatcher for keyboard input handlers.
    /// Routes input to active handlers in priority order (lower band number = higher priority).
    /// Handles frame isolation to prevent Escape key from propagating to parent windows.
    /// </summary>
    public static class KeyboardInputRouter
    {
        private static List<IKeyboardInputHandler> handlers;
        private static int lastClosedFrame = -1;

        /// <summary>
        /// Initialize the router with all handlers. Called once at mod startup.
        /// </summary>
        public static void Initialize(IEnumerable<IKeyboardInputHandler> registeredHandlers)
        {
            handlers = registeredHandlers
                .OrderBy(h => (int)h.Priority)
                .ToList();

            ValidateRegistry();
            Log.Message($"[KeyboardInputRouter] Initialized with {handlers.Count} handlers");
        }

        /// <summary>
        /// Process keyboard input. Handles frame isolation internally.
        /// </summary>
        public static bool ProcessInput(KeyboardInputContext context)
        {
            if (handlers == null || handlers.Count == 0)
            {
                Log.Warning("[KeyboardInputRouter] ProcessInput called before Initialize or with no handlers");
                return false;
            }

            // Frame isolation: if a handler closed this frame, consume Escape
            // to prevent it propagating to parent windows
            if (context.Key == KeyCode.Escape && WasHandlerClosedThisFrame())
            {
                ClearClosedFlag();
                return true;
            }

            foreach (var handler in handlers)
            {
                if (!handler.IsActive)
                    continue;

                try
                {
                    if (handler.HandleInput(context))
                    {
                        LogActiveHandlerStack(context, handler);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[KeyboardInputRouter] Handler {handler.GetType().Name} threw exception: {ex}");
                    // Continue to next handler instead of breaking entire input system
                }
            }

            return false;
        }

        /// <summary>
        /// Notify router that a handler closed. Called by handlers in their Close() method.
        /// </summary>
        public static void NotifyHandlerClosed()
        {
            lastClosedFrame = Time.frameCount;
        }

        /// <summary>
        /// Check if any handler in the specified band is currently active.
        /// </summary>
        public static bool HasActiveHandler(InputPriorityBand band)
        {
            if (handlers == null)
                return false;

            return handlers.Any(h => h.Priority == band && h.IsActive);
        }

        /// <summary>
        /// Get current input context description for screen reader announcement.
        /// </summary>
        public static string GetActiveContextDescription()
        {
            if (handlers == null)
                return "Main game view";

            var active = handlers.Where(h => h.IsActive).ToList();
            if (active.Count == 0)
                return "Main game view";

            return string.Join(" > ", active.Select(h => h.GetType().Name.Replace("State", "")));
        }

        /// <summary>
        /// Get registered handler types (for verification).
        /// </summary>
        public static HashSet<Type> GetRegisteredTypes()
        {
            return handlers?.Select(h => h.GetType()).ToHashSet() ?? new HashSet<Type>();
        }

        private static bool WasHandlerClosedThisFrame()
        {
            return Time.frameCount == lastClosedFrame;
        }

        private static void ClearClosedFlag()
        {
            lastClosedFrame = -1;
        }

        private static void ValidateRegistry()
        {
            var duplicates = handlers
                .GroupBy(h => h.GetType())
                .Where(g => g.Count() > 1)
                .Select(g => g.Key.Name);

            if (duplicates.Any())
            {
                Log.Error($"[KeyboardInputRouter] Duplicate handlers registered: {string.Join(", ", duplicates)}");
            }
        }

        [Conditional("DEBUG")]
        private static void LogActiveHandlerStack(KeyboardInputContext context, IKeyboardInputHandler consumedBy)
        {
            var activeStack = handlers
                .Where(h => h.IsActive)
                .Select(h => h == consumedBy ? $"[{h.GetType().Name}]" : h.GetType().Name);

            Log.Message($"[KeyboardInputRouter] {context.Key} -> {string.Join(" > ", activeStack)}");
        }
    }
}
