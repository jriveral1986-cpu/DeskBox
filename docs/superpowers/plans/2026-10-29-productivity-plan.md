# Productivity Implementation Plan

> For agentic workers: use superpowers:subagent-driven-development task by task. Read the approved specification before the task brief.

**Goal:** Implement the three approved productivity improvements in the personal fork and verify them through GitHub Actions.

**Architecture:** Extend existing WidgetManager visibility operations, Todo view models and QuickCapture persistence. Keep policies testable without WinUI; integrate controls through existing UI and localization infrastructure.

**Tech Stack:** C#, WinUI 3, .NET 10, Rust 1.96.0, xUnit, GitHub Actions Windows x64.

## Global Constraints

- Approved behavior: docs/superpowers/specs/2026-10-29-productivity-design.md.
- Windows Smart App Control remains enabled. Do not change security settings, exclusions, certificates or Windows policies.
- All application builds and tests run in GitHub Actions, with x64/win-x64. No local launch or compilation of application code.
- Preserve Native AOT serialization, existing files and settings. New persisted properties must be backward-compatible.
- No automatic commits or pushes by workers; the controller owns GitHub runs and commits. No changes to upstream Tianyu199509/DeskBox.
- Retain all thirteen language resource key sets; Spanish and English copy must be idiomatic.

## Task 1: Persistent visibility exception

Files: WidgetConfig, WidgetManager visibility/session policies, widget/group context menus, language resources; tests/DeskBox.Tests/WidgetKeepVisibleTests.cs.

- [ ] Write failing behavior and persistence tests first; controller runs the red tests in GitHub Actions before production edits.
- [ ] Persist `KeepVisibleOnGlobalHide` default false. Resolve the effective option at group-surface level without losing member preferences.
- [ ] Extend the existing serialized tray toggle to ignore exempt panels both when hiding and deciding whether the next toggle should show.
- [ ] Preserve explicit hide, disable, shutdown, disconnected-screen and temporary Quick Reveal cleanup behavior.
- [ ] Expose a localized context-menu toggle on panels and groups; save and refresh state through current owners.
- [ ] Validate focused tests in Actions, review the task diff for compliance and quality, then record the result.

## Task 2: Today agenda and three daily priorities

Files: TodoItem, TodoItemViewModel, TodoWidgetViewModel filtering/operations, TodoWidgetContent and context-menu handlers, resources; tests/DeskBox.Tests/TodoTodayAgendaTests.cs.

- [ ] Write failing tests against filtering, persisted daily priority and visible ordering before production changes; run red in Actions.
- [ ] Add a pure date-injected agenda policy. Implement Principales/Vencidas/Hoy/Próximas (next seven local dates inclusive), no duplicates, maximum three active daily priorities.
- [ ] Integrate grouped labels without breaking selection, responsive detail panes, completed preferences or color filters.
- [ ] Retain manual order outside Today; disallow manual reordering in Today and avoid writing the date-sorted order into SortOrder.
- [ ] Recompute daily state at midnight and resume; detach subscriptions/timers correctly. Recurring tasks do not inherit daily priority.
- [ ] Run focused tests and regression suite in Actions. Review compliance and quality before completing the task.

## Task 3: Keyboard capture

Files: new quick-entry service/controller/window, App lifecycle and tray integration, QuickCapture settings and shortcut settings controls, resources; tests/DeskBox.Tests/QuickEntryTests.cs.

- [ ] Write failing persistence and routing tests before production changes; run red in Actions.
- [ ] Add opt-in global ordinary hotkey Ctrl+Alt+N, configurable using existing recorder. Detect collisions and reject reserved chords; retain the last valid registration on failure.
- [ ] Implement one compact entry window with Note/Task destination, remembered destination, session draft, Enter/Ctrl+Enter save, Shift+Enter newline, Escape retain draft.
- [ ] Save through QuickCaptureService for notes and the owning Todo view model for tasks. Offer explicit panel creation if no destination exists.
- [ ] Prevent double submission; retain text/destination after failed writes; show actionable localized feedback and return focus after a successful save.
- [ ] Dispose registration and subscriptions at shutdown and on disabling. No clipboard reads or network services.
- [ ] Focused tests and full suite in Actions, followed by task review.

## Task 4: Integration and delivery

- [ ] Whole-branch review against the approved specification; fix material findings and re-review.
- [ ] Open a draft PR in jriveral1986-cpu/DeskBox and attach it to this chat.
- [ ] Run the final exact commit through restore, build and full tests. Require passed = total, zero failed/aborted/notExecuted in TRX.
- [ ] Download application ZIP and TRX into the chat outputs folder with versioned names. Record build SHA and Actions URL.
- [ ] Explicitly distinguish automated verification from interactive visual testing, which is unavailable on the protected host.

## Verification commands (remote Windows runner)

```powershell
dotnet restore .\DeskBox.sln --locked-mode -p:Platform=x64 -p:RuntimeIdentifier=win-x64
dotnet build .\src\DeskBox\DeskBox.csproj --configuration Release --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -v:minimal
dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --configuration Release --no-restore -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:WindowsAppSdkBootstrapInitialize=false --blame-hang --blame-hang-timeout 5m --logger "trx;LogFileName=test-results.trx" -v:minimal
```

For focused runs append `--filter FullyQualifiedName~WidgetKeepVisibleTests`, `--filter FullyQualifiedName~TodoTodayAgendaTests`, or `--filter FullyQualifiedName~QuickEntryTests` to the test command. The controller keeps the required full run for final delivery.
