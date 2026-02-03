# State Migration Guide - Old to New Input System

This guide walks through migrating a State class from the legacy manual routing in UnifiedKeyboardPatch to the new IKeyboardInputHandler system.

## Before You Start

1. **Read input_refactor.md** - Understand the architecture and priority bands
2. **Check input_refactor_progress.md** - See what's been completed and what's pending
3. **Choose a state to migrate** - Start with TextInput or Modal band for simplicity

## Migration Steps

### Step 1: Understand the Current Implementation

**Find the state's input handling in UnifiedKeyboardPatch.cs**

Search for the state name (e.g., "QuestMenuState") in UnifiedKeyboardPatch.cs. You'll find a block like:

```csharp
// Around line 2847 in UnifiedKeyboardPatch.cs
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

**Identify the pattern:**
- Simple navigation (Up/Down/Enter/Escape)? Use `BaseNavigationHandler`
- Typeahead search? Use `TypeaheadNavigationHandler`
- Tabbed interface? Use `TabNavigationHandler`

### Step 2: Choose the Right Base Class

#### Option A: BaseNavigationHandler (Simple Menus)

For states with basic navigation (Up/Down/Home/End/Enter/Escape):

```csharp
public class MyState : BaseNavigationHandler
{
    public static readonly MyState Instance = new MyState();

    public override InputPriorityBand Priority => InputPriorityBand.Menu;
    public override bool IsActive => isActive;

    // Implement required methods
    protected override bool OnSelectNext() { /* ... */ }
    protected override bool OnSelectPrevious() { /* ... */ }
    protected override bool OnExecuteSelected() { /* ... */ }
    protected override bool OnGoBack() { /* ... */ }
}
```

#### Option B: TypeaheadNavigationHandler (Searchable Menus)

For states with typeahead search (most menus):

```csharp
public class MyState : TypeaheadNavigationHandler
{
    public static readonly MyState Instance = new MyState();

    public override InputPriorityBand Priority => InputPriorityBand.Menu;
    public override bool IsActive => isActive;

    // Implement required methods
    protected override TypeaheadSearchHelper GetTypeaheadHelper() { /* ... */ }
    protected override List<string> GetItemLabels() { /* ... */ }
    protected override int GetCurrentIndex() { /* ... */ }
    protected override void SetCurrentIndex(int index) { /* ... */ }
    protected override void AnnounceCurrentSelection() { /* ... */ }
    protected override void CloseMenu() { /* ... */ }
    protected override void NavigateNextNormal() { /* ... */ }
    protected override void NavigatePreviousNormal() { /* ... */ }
}
```

#### Option C: Direct IKeyboardInputHandler (Complex States)

For states with complex, custom input patterns:

```csharp
public class MyState : IKeyboardInputHandler
{
    public static readonly MyState Instance = new MyState();

    public InputPriorityBand Priority => InputPriorityBand.Modal;
    public bool IsActive => isActive;

    public bool HandleInput(KeyboardInputContext context)
    {
        // Custom input handling
        switch (context.Key)
        {
            case KeyCode.Space:
                // ...
                return true;
        }
        return false;
    }
}
```

### Step 3: Implement the Handler

#### 3.1: Add Singleton Instance

```csharp
public static readonly QuestMenuState Instance = new QuestMenuState();
private QuestMenuState() { } // Private constructor for singleton
```

#### 3.2: Implement Priority

Choose the appropriate band:

```csharp
public override InputPriorityBand Priority => InputPriorityBand.Menu;
```

**Priority Band Guidelines:**
- **TextInput** (0): Zone rename, text input fields
- **Modal** (1): Overlays that appear atop other UI (quantity menu, inspection, confirmations)
- **Menu** (2): Primary menus (quest list, animals, trade, caravan formation)
- **Global** (3): Shortcuts that work when no menu is open (R for draft, Alt+F for unforbid)

#### 3.3: Migrate Input Handling Code

**From UnifiedKeyboardPatch:**
```csharp
if (QuestMenuState.IsActive)
{
    if (key == KeyCode.UpArrow)
    {
        QuestMenuState.SelectPrevious();
        Event.current.Use();
        return;
    }
}
```

**To Handler:**
```csharp
protected override bool OnSelectPrevious()
{
    SelectPrevious(); // Call existing method
    return true; // Event consumed
}
```

#### 3.4: Handle Escape Properly

Call `NotifyRouterClosed()` when closing to prevent double-closing:

```csharp
protected override bool OnGoBack()
{
    if (searchHelper.HasActiveSearch)
    {
        searchHelper.ClearSearch();
        return true; // Clear search first
    }

    CloseMenu();
    NotifyRouterClosed(); // IMPORTANT: Notify router for Escape isolation
    return true;
}
```

### Step 4: Register in HandlerRegistry

**Edit HandlerRegistry.cs:**

```csharp
var handlers = new List<IKeyboardInputHandler>
{
    // ... existing handlers

    // Menu band
    QuestMenuState.Instance,  // Add your handler

    // ... other handlers
};
```

### Step 5: Remove from UnifiedKeyboardPatch

**Find and delete** the entire input handling block for this state in UnifiedKeyboardPatch.cs:

```csharp
// DELETE THIS ENTIRE BLOCK:
if (QuestMenuState.IsActive)
{
    // ... 100 lines of input handling
}
```

### Step 6: Test Thoroughly

1. **Build the project**: `dotnet build`
2. **Launch RimWorld**
3. **Check console logs**:
   - Look for "[KeyboardInputRouter] Initialized with X handlers"
   - Look for "[HandlerRegistryVerification] All X handlers are properly registered"
4. **Test in-game**:
   - Open the menu (e.g., Quest menu with F7)
   - Test all keyboard shortcuts (Up/Down/Home/End/Enter/Escape)
   - Test typeahead search (if applicable)
   - Test Escape key (should close menu, not double-close parent windows)
5. **Check for priority conflicts**:
   - Look for warnings: "[KeyboardInputRouter] PRIORITY CONFLICT"
   - If found, check if states are truly mutually exclusive

## Complete Example: QuestMenuState

### Before (in UnifiedKeyboardPatch.cs)

```csharp
// Priority 4.73: Quest Menu
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
    // ... 80 more lines
}
```

### After (in QuestMenuState.cs)

```csharp
public class QuestMenuState : TypeaheadNavigationHandler
{
    public static readonly QuestMenuState Instance = new QuestMenuState();

    private static bool isActive;
    private static int selectedIndex;
    private static List<Quest> quests;
    private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

    private QuestMenuState() { }

    public override InputPriorityBand Priority => InputPriorityBand.Menu;
    public override bool IsActive => isActive;

    public static void Open(List<Quest> questList)
    {
        quests = questList;
        selectedIndex = 0;
        isActive = true;
        typeahead.ClearSearch();
    }

    protected override TypeaheadSearchHelper GetTypeaheadHelper() => typeahead;

    protected override List<string> GetItemLabels()
    {
        return quests.Select(q => q.name).ToList();
    }

    protected override int GetCurrentIndex() => selectedIndex;

    protected override void SetCurrentIndex(int index) => selectedIndex = index;

    protected override void AnnounceCurrentSelection()
    {
        if (selectedIndex >= 0 && selectedIndex < quests.Count)
        {
            var quest = quests[selectedIndex];
            TolkHelper.Speak($"{quest.name}, {selectedIndex + 1} of {quests.Count}");
        }
    }

    protected override void CloseMenu()
    {
        isActive = false;
        quests = null;
    }

    protected override void NavigateNextNormal()
    {
        if (selectedIndex < quests.Count - 1)
        {
            selectedIndex++;
            AnnounceCurrentSelection();
        }
    }

    protected override void NavigatePreviousNormal()
    {
        if (selectedIndex > 0)
        {
            selectedIndex--;
            AnnounceCurrentSelection();
        }
    }

    protected override bool OnExecuteSelected()
    {
        if (selectedIndex >= 0 && selectedIndex < quests.Count)
        {
            OpenQuestDetail(quests[selectedIndex]);
        }
        return true;
    }

    private void OpenQuestDetail(Quest quest)
    {
        // Implementation
    }
}
```

## Common Pitfalls

### 1. Forgetting NotifyRouterClosed()

**Problem**: Pressing Escape closes TWO windows instead of one.

**Solution**: Always call `NotifyRouterClosed()` in your Close() or OnGoBack() method:

```csharp
protected override bool OnGoBack()
{
    CloseMenu();
    NotifyRouterClosed(); // MUST call this!
    return true;
}
```

### 2. Wrong Priority Band

**Problem**: Handler never receives input, or receives input when it shouldn't.

**Solution**: Check if your handler is in the correct band:
- Is it mutually exclusive with other handlers in the same band?
- Does it need to run before/after specific handlers?

### 3. Not Making Constructor Private

**Problem**: Multiple instances created, singleton pattern broken.

**Solution**: Make constructor private:

```csharp
public static readonly MyState Instance = new MyState();
private MyState() { } // Private!
```

### 4. Forgetting to Register

**Problem**: Handler never receives input, verification logs warning.

**Solution**: Add handler to HandlerRegistry.RegisterAll():

```csharp
var handlers = new List<IKeyboardInputHandler>
{
    // ...
    MyState.Instance, // Don't forget!
};
```

## Need Help?

- Check existing migrated handlers for examples
- Read the base class documentation (BaseNavigationHandler, TypeaheadNavigationHandler)
- Check console logs for warnings and errors
- Test with DEBUG build to catch unregistered handlers

## Migration Checklist

- [ ] Understand current implementation in UnifiedKeyboardPatch
- [ ] Choose appropriate base class (BaseNavigationHandler, TypeaheadNavigationHandler, or direct)
- [ ] Add singleton Instance field
- [ ] Implement Priority property with correct band
- [ ] Implement IsActive property
- [ ] Migrate input handling code
- [ ] Call NotifyRouterClosed() in Close/OnGoBack
- [ ] Register in HandlerRegistry.RegisterAll()
- [ ] Remove from UnifiedKeyboardPatch
- [ ] Build project (check for errors)
- [ ] Test in-game (all keyboard shortcuts work)
- [ ] Check console for warnings (priority conflicts, unregistered handlers)
