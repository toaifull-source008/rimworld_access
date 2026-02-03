# Keyboard Input System Refactoring Plan

## Executive Summary

**Current State:** UnifiedKeyboardPatch.cs has grown to 3685 lines with a single method handling all keyboard input for 30+ different states.

**Goal:** Decompose the monolithic input handler into a clean, extensible architecture using the Strategy pattern with priority-based routing.

**Key Architectural Decisions:**
- **Coarse priority bands** based on UI layer semantics (4 bands total)
- **Explicit registration** at mod initialization with compile-time verification
- **Frame isolation handled entirely within the router**
- **Debug visualization** of active handler stack

**End Result:** UnifiedKeyboardPatch reduces to a pure Harmony stub (~15 lines) with all routing logic owned by the router.

---

## Problem Analysis

### Critical Issues

1. **God Method Anti-Pattern (3685 lines)**
   - Single `Prefix()` method handles 30+ different states
   - Impossible to understand, test, or modify safely

2. **Arbitrary Priority System**
   - Comment-based priorities using invented decimals (4.73, 4.779, 6.525)
   - No validation that priorities are correct or meaningful

3. **Massive Code Duplication (~750 lines)**
   - Typeahead handling repeated 15+ times
   - Home/End navigation repeated 12+ times
   - Escape with search clearing repeated 12+ times
   - Arrow navigation with typeahead awareness repeated 10+ times

4. **Inconsistent Input Handling**
   - 28 states have `HandleInput()` methods
   - Others have 50-100 lines of inline code in UnifiedKeyboardPatch
   - No common interface or contract

5. **No Testability**
   - Cannot unit test input handlers in isolation
   - Cannot mock or stub states

---

## Target Architecture

### Core Components

```
Input/
├── IKeyboardInputHandler.cs       # Core interface
├── KeyboardInputRouter.cs         # Registry, dispatcher, frame isolation
├── KeyboardInputContext.cs        # Input context (key, modifiers)
├── InputPriorityBand.cs           # 4-band enum
├── HandlerRegistry.cs             # Explicit registration with verification
├── BaseNavigationHandler.cs       # Base class for list navigation
├── TypeaheadNavigationHandler.cs  # Base class with typeahead search
└── UnifiedKeyboardPatch.cs        # Minimal Harmony stub

[Feature]/
└── [Feature]State.cs              # Implements IKeyboardInputHandler
```

### Interface Design

```csharp
public interface IKeyboardInputHandler
{
    /// <summary>
    /// Priority band for this handler. Bands are coarse categories;
    /// handlers within the same band should be mutually exclusive.
    /// </summary>
    InputPriorityBand Priority { get; }

    /// <summary>
    /// Whether this handler should receive input this frame.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Handle keyboard input.
    /// </summary>
    /// <returns>True if event was consumed, false to continue routing</returns>
    bool HandleInput(KeyboardInputContext context);
}
```

### Priority System

Four bands based on UI layer semantics:

```csharp
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
```

**Why only four bands?**

The original system's 30+ distinct priorities created the illusion of fine-grained control. In practice, most handlers are mutually exclusive—you can't have both the Animals menu and the Quest menu open simultaneously. Fine-grained ordering between mutually exclusive handlers is meaningless.

The four bands reflect actual UI layering:
- Text input must block everything (you're typing)
- Modals sit atop their parent and must be handled first
- Menus are the primary interaction layer
- Global shortcuts are fallbacks

If two handlers might genuinely be active simultaneously and conflict, they belong in different bands. If they're mutually exclusive, same-band membership is correct and the `IsActive` check discriminates between them.

---

## Router Implementation

```csharp
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

            if (handler.HandleInput(context))
            {
                LogActiveHandlerStack(context, handler);
                return true;
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
        return handlers.Any(h => h.Priority == band && h.IsActive);
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
        if (!ModSettings.DebugInputRouting)
            return;

        var activeStack = handlers
            .Where(h => h.IsActive)
            .Select(h => h == consumedBy ? $"[{h.GetType().Name}]" : h.GetType().Name);

        Log.Message($"[Input] {context.Key} -> {string.Join(" > ", activeStack)}");
    }

    /// <summary>
    /// Get current input context description for screen reader announcement.
    /// </summary>
    public static string GetActiveContextDescription()
    {
        var active = handlers.Where(h => h.IsActive).ToList();
        if (active.Count == 0)
            return "Main game view";

        return string.Join(" > ", active.Select(h => h.GetType().Name.Replace("State", "")));
    }
}
```

---

## Explicit Registration

```csharp
public static class HandlerRegistry
{
    /// <summary>
    /// Register all keyboard input handlers. Called once at mod initialization.
    /// Adding a new handler requires adding it here - this is intentional.
    /// </summary>
    public static void RegisterAll()
    {
        var handlers = new List<IKeyboardInputHandler>
        {
            // TextInput band - blocks all other input
            ZoneRenameState.Instance,
            WindowlessDialogState.Instance,

            // Modal band - overlays atop menus
            QuantityMenuState.Instance,
            WindowlessInspectionState.Instance,
            ConfirmationDialogState.Instance,

            // Menu band - primary menus (mutually exclusive)
            CaravanFormationState.Instance,
            SplitCaravanState.Instance,
            TradeNavigationState.Instance,
            QuestMenuState.Instance,
            AnimalsMenuState.Instance,
            WildlifeMenuState.Instance,
            WindowlessSaveMenuState.Instance,
            WindowlessPauseMenuState.Instance,
            WindowlessResearchMenuState.Instance,
            StorageSettingsMenuState.Instance,
            // ... all other menu states

            // Global band - fallback shortcuts
            GlobalShortcutsHandler.Instance,
        };

        KeyboardInputRouter.Initialize(handlers);
    }
}
```

**Why explicit registration instead of reflection?**

1. **Compile-time verification** - A missing handler causes a compiler error, not silent runtime failure
2. **Visible dependencies** - The registry file shows exactly what handlers exist
3. **Predictable initialization** - No dependency on static constructor ordering
4. **Testability** - Tests pass explicit handler lists without special initialization paths
5. **No startup latency** - No assembly scanning on first input event

---

## Compile-Time Verification

```csharp
#if DEBUG
[StaticConstructorOnStartup]
public static class HandlerRegistryVerification
{
    static HandlerRegistryVerification()
    {
        // Find all IKeyboardInputHandler implementations
        var allHandlerTypes = typeof(HandlerRegistry).Assembly
            .GetTypes()
            .Where(t => typeof(IKeyboardInputHandler).IsAssignableFrom(t))
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .ToHashSet();

        // Get registered handler types
        var registeredTypes = GetRegisteredHandlerTypes();

        // Report unregistered handlers
        var unregistered = allHandlerTypes.Except(registeredTypes);
        foreach (var type in unregistered)
        {
            Log.Error($"[HandlerRegistry] {type.Name} implements IKeyboardInputHandler but is not registered!");
        }
    }

    private static HashSet<Type> GetRegisteredHandlerTypes()
    {
        // This requires either:
        // 1. A test-only method on HandlerRegistry that returns registered types
        // 2. Parsing the RegisterAll method (fragile)
        // 3. Calling RegisterAll and inspecting the router
        
        // Option 3 is simplest:
        HandlerRegistry.RegisterAll();
        return KeyboardInputRouter.GetRegisteredTypes();
    }
}
#endif
```

Add supporting method to router:

```csharp
// In KeyboardInputRouter
public static HashSet<Type> GetRegisteredTypes()
{
    return handlers?.Select(h => h.GetType()).ToHashSet() ?? new HashSet<Type>();
}
```

---

## Input Context

```csharp
public readonly struct KeyboardInputContext
{
    public KeyCode Key { get; }
    public bool Shift { get; }
    public bool Ctrl { get; }
    public bool Alt { get; }

    public KeyboardInputContext(Event evt)
    {
        Key = evt.keyCode;
        Shift = evt.shift;
        Ctrl = evt.control;
        Alt = evt.alt;
    }

    public KeyboardInputContext(KeyCode key, bool shift = false, bool ctrl = false, bool alt = false)
    {
        Key = key;
        Shift = shift;
        Ctrl = ctrl;
        Alt = alt;
    }

    public bool HasModifier => Shift || Ctrl || Alt;
    public bool NoModifiers => !HasModifier;
}
```

---

## Base Classes

### BaseNavigationHandler

```csharp
/// <summary>
/// Base class for simple list navigation (Up/Down/Enter/Escape).
/// </summary>
public abstract class BaseNavigationHandler : IKeyboardInputHandler
{
    public abstract InputPriorityBand Priority { get; }
    public abstract bool IsActive { get; }

    public bool HandleInput(KeyboardInputContext context)
    {
        switch (context.Key)
        {
            case KeyCode.UpArrow:
                SelectPrevious();
                return true;

            case KeyCode.DownArrow:
                SelectNext();
                return true;

            case KeyCode.Home:
                SelectFirst();
                return true;

            case KeyCode.End:
                SelectLast();
                return true;

            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                ExecuteSelected();
                return true;

            case KeyCode.Escape:
                Close();
                return true;
        }

        return HandleAdditionalInput(context);
    }

    protected abstract void SelectNext();
    protected abstract void SelectPrevious();
    protected abstract void SelectFirst();
    protected abstract void SelectLast();
    protected abstract void ExecuteSelected();

    protected virtual void Close()
    {
        KeyboardInputRouter.NotifyHandlerClosed();
        CloseMenu();
    }

    protected abstract void CloseMenu();

    protected virtual bool HandleAdditionalInput(KeyboardInputContext context) => false;
}
```

### TypeaheadBuffer

```csharp
public class TypeaheadBuffer
{
    private readonly StringBuilder buffer = new();

    public string Value => buffer.ToString();
    public bool HasContent => buffer.Length > 0;

    public void Add(char c)
    {
        buffer.Append(c);
    }

    public void RemoveLast()
    {
        if (buffer.Length > 0)
            buffer.Length--;
    }

    public void Clear()
    {
        buffer.Clear();
    }
}
```

### TypeaheadNavigationHandler

```csharp
/// <summary>
/// Base class for menus with typeahead search.
/// </summary>
public abstract class TypeaheadNavigationHandler : BaseNavigationHandler
{
    protected readonly TypeaheadBuffer searchBuffer = new();

    public override bool HandleInput(KeyboardInputContext context)
    {
        // Backspace removes last search character
        if (context.Key == KeyCode.Backspace && searchBuffer.HasContent)
        {
            searchBuffer.RemoveLast();
            AnnounceSearchState();
            return true;
        }

        // Alphanumeric input adds to search
        if (TryGetCharacter(context.Key, out char c))
        {
            searchBuffer.Add(c);
            JumpToNextMatch();
            AnnounceSearchState();
            return true;
        }

        return base.HandleInput(context);
    }

    protected override void SelectNext()
    {
        if (searchBuffer.HasContent)
            JumpToNextMatch();
        else
            SelectNextUnfiltered();
    }

    protected override void SelectPrevious()
    {
        if (searchBuffer.HasContent)
            JumpToPreviousMatch();
        else
            SelectPreviousUnfiltered();
    }

    protected override void Close()
    {
        if (searchBuffer.HasContent)
        {
            searchBuffer.Clear();
            AnnounceSearchState();
            // Don't close menu - just clear search
        }
        else
        {
            base.Close();
        }
    }

    protected abstract void SelectNextUnfiltered();
    protected abstract void SelectPreviousUnfiltered();
    protected abstract void JumpToNextMatch();
    protected abstract void JumpToPreviousMatch();
    protected abstract void AnnounceSearchState();

    private static bool TryGetCharacter(KeyCode key, out char c)
    {
        if (key >= KeyCode.A && key <= KeyCode.Z)
        {
            c = (char)('a' + (key - KeyCode.A));
            return true;
        }
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
        {
            c = (char)('0' + (key - KeyCode.Alpha0));
            return true;
        }
        c = default;
        return false;
    }
}
```

---

## Minimal Harmony Stub

```csharp
[HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
public static class UnifiedKeyboardPatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        if (Event.current.type != EventType.KeyDown)
            return;

        if (Event.current.keyCode == KeyCode.None)
            return;

        var context = new KeyboardInputContext(Event.current);

        if (KeyboardInputRouter.ProcessInput(context))
        {
            Event.current.Use();
        }
    }
}
```

---

## Handler Migration Pattern

### Before (inline in UnifiedKeyboardPatch)

```csharp
// In UnifiedKeyboardPatch.Prefix(), somewhere around line 2847
if (QuestMenuState.IsActive)
{
    if (key == KeyCode.UpArrow)
    {
        if (QuestMenuState.typeahead.HasActiveSearch && !QuestMenuState.typeahead.HasNoMatches)
        {
            int newIndex = QuestMenuState.typeahead.GetPreviousMatch(QuestMenuState.selectedIndex);
            if (newIndex >= 0)
            {
                QuestMenuState.selectedIndex = newIndex;
                QuestMenuState.AnnounceCurrentQuest();
            }
        }
        else
        {
            QuestMenuState.SelectPrevious();
        }
        Event.current.Use();
        return;
    }
    // ... 80 more lines for other keys
}
```

### After (self-contained handler)

```csharp
public class QuestMenuState : TypeaheadNavigationHandler
{
    public static readonly QuestMenuState Instance = new();

    private static bool isActive;
    private static int selectedIndex;
    private static List<Quest> quests;

    public override InputPriorityBand Priority => InputPriorityBand.Menu;
    public override bool IsActive => isActive;

    public static void Open(List<Quest> questList)
    {
        quests = questList;
        selectedIndex = 0;
        isActive = true;
        Instance.searchBuffer.Clear();
    }

    protected override void CloseMenu()
    {
        isActive = false;
        quests = null;
    }

    protected override void SelectNextUnfiltered()
    {
        if (selectedIndex < quests.Count - 1)
        {
            selectedIndex++;
            AnnounceCurrentQuest();
        }
    }

    protected override void SelectPreviousUnfiltered()
    {
        if (selectedIndex > 0)
        {
            selectedIndex--;
            AnnounceCurrentQuest();
        }
    }

    protected override void SelectFirst()
    {
        selectedIndex = 0;
        AnnounceCurrentQuest();
    }

    protected override void SelectLast()
    {
        selectedIndex = quests.Count - 1;
        AnnounceCurrentQuest();
    }

    protected override void JumpToNextMatch()
    {
        var search = searchBuffer.Value;
        for (int i = selectedIndex + 1; i < quests.Count; i++)
        {
            if (quests[i].name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                return;
            }
        }
        // Wrap around
        for (int i = 0; i <= selectedIndex; i++)
        {
            if (quests[i].name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                return;
            }
        }
    }

    protected override void JumpToPreviousMatch()
    {
        var search = searchBuffer.Value;
        for (int i = selectedIndex - 1; i >= 0; i--)
        {
            if (quests[i].name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                return;
            }
        }
        // Wrap around
        for (int i = quests.Count - 1; i >= selectedIndex; i--)
        {
            if (quests[i].name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                return;
            }
        }
    }

    protected override void ExecuteSelected()
    {
        if (selectedIndex >= 0 && selectedIndex < quests.Count)
        {
            OpenQuestDetail(quests[selectedIndex]);
        }
    }

    protected override void AnnounceSearchState()
    {
        if (searchBuffer.HasContent)
        {
            SpeechHelper.Say($"Search: {searchBuffer.Value}");
        }
        else
        {
            SpeechHelper.Say("Search cleared");
        }
        AnnounceCurrentQuest();
    }

    private void AnnounceCurrentQuest()
    {
        if (selectedIndex >= 0 && selectedIndex < quests.Count)
        {
            var quest = quests[selectedIndex];
            SpeechHelper.Say($"{quest.name}, {selectedIndex + 1} of {quests.Count}");
        }
    }

    private void OpenQuestDetail(Quest quest)
    {
        // Implementation
    }
}
```

---

## Window Cancel Key Patch

Some RimWorld windows handle Escape internally. To prevent double-closing when a modal overlay is open:

```csharp
[HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
public static class Window_OnCancelKeyPressed_Patch
{
    public static bool Prefix(Window __instance)
    {
        // Block RimWorld's Escape handling if a modal overlay is active
        if (__instance is Dialog_FormCaravan or Dialog_SplitCaravan)
        {
            if (KeyboardInputRouter.HasActiveHandler(InputPriorityBand.Modal))
                return false;
        }
        return true;
    }
}
```

---

## Unit Tests

```csharp
[TestFixture]
public class KeyboardInputRouterTests
{
    [SetUp]
    public void SetUp()
    {
        KeyboardInputRouter.Initialize(Array.Empty<IKeyboardInputHandler>());
    }

    [Test]
    public void ProcessInput_RoutesToHighestPriorityActiveHandler()
    {
        var modal = new MockHandler(InputPriorityBand.Modal, isActive: true);
        var menu = new MockHandler(InputPriorityBand.Menu, isActive: true);

        KeyboardInputRouter.Initialize(new IKeyboardInputHandler[] { menu, modal });

        var context = new KeyboardInputContext(KeyCode.Return);
        KeyboardInputRouter.ProcessInput(context);

        Assert.IsTrue(modal.WasCalled);
        Assert.IsFalse(menu.WasCalled);
    }

    [Test]
    public void ProcessInput_SkipsInactiveHandlers()
    {
        var handler = new MockHandler(InputPriorityBand.Menu, isActive: false);

        KeyboardInputRouter.Initialize(new IKeyboardInputHandler[] { handler });

        var context = new KeyboardInputContext(KeyCode.Return);
        bool consumed = KeyboardInputRouter.ProcessInput(context);

        Assert.IsFalse(consumed);
        Assert.IsFalse(handler.WasCalled);
    }

    [Test]
    public void ProcessInput_ConsumesEscapeAfterHandlerClosed()
    {
        var handler = new MockHandler(InputPriorityBand.Menu, isActive: false);

        KeyboardInputRouter.Initialize(new IKeyboardInputHandler[] { handler });
        KeyboardInputRouter.NotifyHandlerClosed();

        var context = new KeyboardInputContext(KeyCode.Escape);
        bool consumed = KeyboardInputRouter.ProcessInput(context);

        Assert.IsTrue(consumed);
        Assert.IsFalse(handler.WasCalled);
    }

    [Test]
    public void ProcessInput_PassesThroughWhenNoHandlerConsumes()
    {
        var handler = new MockHandler(InputPriorityBand.Menu, isActive: true, consumes: false);

        KeyboardInputRouter.Initialize(new IKeyboardInputHandler[] { handler });

        var context = new KeyboardInputContext(KeyCode.A);
        bool consumed = KeyboardInputRouter.ProcessInput(context);

        Assert.IsFalse(consumed);
        Assert.IsTrue(handler.WasCalled);
    }

    [Test]
    public void HasActiveHandler_ReturnsTrueWhenHandlerInBandIsActive()
    {
        var modal = new MockHandler(InputPriorityBand.Modal, isActive: true);
        var menu = new MockHandler(InputPriorityBand.Menu, isActive: false);

        KeyboardInputRouter.Initialize(new IKeyboardInputHandler[] { modal, menu });

        Assert.IsTrue(KeyboardInputRouter.HasActiveHandler(InputPriorityBand.Modal));
        Assert.IsFalse(KeyboardInputRouter.HasActiveHandler(InputPriorityBand.Menu));
    }
}

[TestFixture]
public class TypeaheadNavigationHandlerTests
{
    [Test]
    public void HandleInput_AddsCharacterToSearch()
    {
        var handler = new MockTypeaheadHandler();
        handler.SetActive(true);

        handler.HandleInput(new KeyboardInputContext(KeyCode.A));

        Assert.AreEqual("a", handler.SearchValue);
    }

    [Test]
    public void HandleInput_BackspaceRemovesLastCharacter()
    {
        var handler = new MockTypeaheadHandler();
        handler.SetActive(true);
        handler.HandleInput(new KeyboardInputContext(KeyCode.A));
        handler.HandleInput(new KeyboardInputContext(KeyCode.B));

        handler.HandleInput(new KeyboardInputContext(KeyCode.Backspace));

        Assert.AreEqual("a", handler.SearchValue);
    }

    [Test]
    public void HandleInput_EscapeClearsSearchBeforeClosing()
    {
        var handler = new MockTypeaheadHandler();
        handler.SetActive(true);
        handler.HandleInput(new KeyboardInputContext(KeyCode.A));

        handler.HandleInput(new KeyboardInputContext(KeyCode.Escape));

        Assert.AreEqual("", handler.SearchValue);
        Assert.IsTrue(handler.IsActive);
    }

    [Test]
    public void HandleInput_EscapeClosesWhenSearchEmpty()
    {
        var handler = new MockTypeaheadHandler();
        handler.SetActive(true);

        handler.HandleInput(new KeyboardInputContext(KeyCode.Escape));

        Assert.IsFalse(handler.IsActive);
    }

    [Test]
    public void HandleInput_NumberKeysAddToSearch()
    {
        var handler = new MockTypeaheadHandler();
        handler.SetActive(true);

        handler.HandleInput(new KeyboardInputContext(KeyCode.Alpha5));

        Assert.AreEqual("5", handler.SearchValue);
    }
}

public class MockHandler : IKeyboardInputHandler
{
    public InputPriorityBand Priority { get; }
    public bool IsActive { get; }
    public bool WasCalled { get; private set; }

    private readonly bool consumes;

    public MockHandler(InputPriorityBand priority, bool isActive, bool consumes = true)
    {
        Priority = priority;
        IsActive = isActive;
        this.consumes = consumes;
    }

    public bool HandleInput(KeyboardInputContext context)
    {
        WasCalled = true;
        return consumes;
    }
}

public class MockTypeaheadHandler : TypeaheadNavigationHandler
{
    private bool isActive;

    public override InputPriorityBand Priority => InputPriorityBand.Menu;
    public override bool IsActive => isActive;

    public string SearchValue => searchBuffer.Value;

    public void SetActive(bool active) => isActive = active;

    protected override void SelectNextUnfiltered() { }
    protected override void SelectPreviousUnfiltered() { }
    protected override void SelectFirst() { }
    protected override void SelectLast() { }
    protected override void JumpToNextMatch() { }
    protected override void JumpToPreviousMatch() { }
    protected override void ExecuteSelected() { }
    protected override void CloseMenu() => isActive = false;
    protected override void AnnounceSearchState() { }
}
```

---

## State Categories by Priority Band

### TextInput Band
- `ZoneRenameState`
- `WindowlessDialogState` (when text field focused)
- `SearchFieldState`

### Modal Band
- `QuantityMenuState`
- `WindowlessInspectionState`
- `ConfirmationDialogState`
- `DeleteConfirmationState`

### Menu Band
- `CaravanFormationState`
- `SplitCaravanState`
- `TradeNavigationState`
- `QuestMenuState`
- `AnimalsMenuState`
- `WildlifeMenuState`
- `AssignMenuState`
- `WindowlessSaveMenuState`
- `WindowlessPauseMenuState`
- `WindowlessOptionsMenuState`
- `WindowlessScheduleState`
- `WindowlessResearchMenuState`
- `WindowlessResearchDetailState`
- `StorageSettingsMenuState`
- `ThingFilterMenuState`
- `WindowlessFloatMenuState`
- `SettlementBrowserState`
- `CaravanStatsState`
- `RoutePlannerState`
- `WorldScannerState`
- `HistoryState`
- `HistoryMessagesState`
- `HistoryStatisticsState`
- `TransportPodLoadingState`
- `TransportPodLaunchState`
- `AreaPaintingState`
- `ArchitectState`
- `HealthTabState`
- `GizmoNavigationState`
- `InfoCardState`

### Global Band
- `GlobalShortcutsHandler` (R, Alt+F, Alt+M, Alt+H, Alt+N, F-keys, time announcements)

---

## Success Metrics

| Metric | Before | After |
|--------|--------|-------|
| UnifiedKeyboardPatch lines | 3685 | ~15 |
| Largest handler | 3685 | <200 |
| Duplicated navigation code | ~750 lines | 0 |
| Priority levels | 30+ arbitrary decimals | 4 semantic bands |
| States with common interface | 28 of 30+ | All |