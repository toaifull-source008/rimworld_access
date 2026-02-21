using System;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Manages zone renaming with text input.
    /// Allows typing a new zone name with Enter to confirm and Escape to cancel.
    /// Uses TextInputHelper for shared text input logic.
    /// </summary>
    public class ZoneRenameState : IKeyboardInputHandler
    {
        public static readonly ZoneRenameState Instance = new ZoneRenameState();

        private ZoneRenameState() { }

        public InputPriorityBand Priority => InputPriorityBand.TextInput;
        bool IKeyboardInputHandler.IsActive => isActive;

        private static bool isActive = false;

        /// <summary>
        /// Gets whether the rename dialog is currently active (backward compatibility).
        /// </summary>
        public static bool IsActive => isActive;
        private static Zone currentZone = null;
        private static string originalName = "";

        /// <summary>
        /// Opens the rename dialog for the specified zone.
        /// </summary>
        public static void Open(Zone zone)
        {
            if (zone == null)
            {
                Log.Error("Cannot open rename dialog: zone is null");
                return;
            }

            currentZone = zone;
            originalName = zone.label;
            TextInputHelper.SetText("");  // Start empty
            isActive = true;

            TolkHelper.Speak($"Renaming {originalName}. Type new name and press Enter, Escape to cancel.");
            Log.Message($"Opened rename dialog for zone: {originalName}");
        }

        /// <summary>
        /// Closes the rename dialog without saving.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentZone = null;
            originalName = "";
            TextInputHelper.Clear();
            KeyboardInputRouter.NotifyHandlerClosed();
        }

        /// <summary>
        /// Handles character input for text entry.
        /// </summary>
        public static void HandleCharacter(char character)
        {
            if (!isActive)
                return;

            TextInputHelper.HandleCharacter(character);
        }

        /// <summary>
        /// Handles backspace key to delete last character.
        /// </summary>
        public static void HandleBackspace()
        {
            if (!isActive)
                return;

            TextInputHelper.HandleBackspace();
        }

        /// <summary>
        /// Reads the current text.
        /// </summary>
        public static void ReadCurrentText()
        {
            if (!isActive)
                return;

            TextInputHelper.ReadCurrentText();
        }

        /// <summary>
        /// Confirms the rename and applies the new name.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive || currentZone == null)
                return;

            string newName = TextInputHelper.CurrentText;

            // Validate name
            if (string.IsNullOrWhiteSpace(newName))
            {
                TolkHelper.Speak("Cannot set empty name. Enter a name or press Escape to cancel.", SpeechPriority.High);
                return;
            }

            try
            {
                // Set the new name
                currentZone.label = newName;
                TolkHelper.Speak($"Renamed to {newName}", SpeechPriority.High);
                Log.Message($"Renamed zone from '{originalName}' to '{newName}'");
            }
            catch (Exception ex)
            {
                TolkHelper.Speak($"Error renaming zone: {ex.Message}", SpeechPriority.High);
                Log.Error($"Error renaming zone: {ex}");
            }
            finally
            {
                Close();
            }
        }

        /// <summary>
        /// Cancels the rename without saving.
        /// </summary>
        public static void Cancel()
        {
            if (!isActive)
                return;

            TolkHelper.Speak("Rename cancelled");
            Log.Message("Zone rename cancelled");
            Close();
        }

        /// <summary>
        /// Handles keyboard input via the new input system.
        /// </summary>
        public bool HandleInput(KeyboardInputContext context)
        {
            if (!isActive)
                return false;

            KeyCode key = context.Key;

            // Handle Escape - cancel
            if (key == KeyCode.Escape)
            {
                Cancel();
                return true;
            }

            // Handle Enter - confirm
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                Confirm();
                return true;
            }

            // Handle Backspace
            if (key == KeyCode.Backspace)
            {
                HandleBackspace();
                return true;
            }

            // Handle Ctrl+A to read current text
            if (context.Ctrl && key == KeyCode.A)
            {
                ReadCurrentText();
                return true;
            }

            // Handle character input
            bool isLetter = key >= KeyCode.A && key <= KeyCode.Z;
            bool isNumber = key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9;
            bool isSpace = key == KeyCode.Space;
            bool isUnderscore = context.Shift && key == KeyCode.Minus; // Shift + - = _
            bool isHyphen = !context.Shift && key == KeyCode.Minus;

            if (isLetter || isNumber || isSpace || isUnderscore || isHyphen)
            {
                char c;
                if (isLetter)
                {
                    // Check for shift to determine upper/lower case
                    c = context.Shift ? (char)('A' + (key - KeyCode.A)) : (char)('a' + (key - KeyCode.A));
                }
                else if (isNumber)
                {
                    c = (char)('0' + (key - KeyCode.Alpha0));
                }
                else if (isSpace)
                {
                    c = ' ';
                }
                else if (isUnderscore)
                {
                    c = '_';
                }
                else // isHyphen
                {
                    c = '-';
                }

                HandleCharacter(c);
                return true;
            }

            return false;
        }
    }
}
