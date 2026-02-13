# BuyTroops Siege Crash Notes

Date: 2026-02-13

## Scenario
- Reported crash: destroying a catapult during a siege crashes when BuyTroops (and another mod) is enabled.
- Baseline from report: no crash with no mods.

## Changes applied in `BuyTroops/Base.cs` (bigger-picture fail-safe)
- Added a session-level circuit breaker:
- `_disabled` + `_disabledReason` state
- `DisableMod(reason, exception)` to shut down safely and report why
- `NotifyDisabled(context)` to repeatedly tell player why actions are blocked
- Added temporary unsafe-context pause with auto-resume:
- `_contextPaused` + `_contextPauseReason`
- `SetContextPause(...)` / `NotifyContextPaused(...)`
- `TryResumeFromContextPause(...)` re-enables when unsafe siege/combat context clears
- Added safety log file output:
- `Documents\\Mount and Blade II Bannerlord\\Configs\\BuyTroops_Safety.log`
- Added `ShouldBlockMenuAction(...)` guard for all menu conditions/consequences.
- Added registration safety gate `CanRegisterMenusNow(...)` to skip menu registration in unstable contexts.
- Added proactive safety event hooks:
- `OnPlayerSiegeStartedEvent`
- `MapEventStarted` (player map events)
- `MapEventEnded` (resume check)
- `OnSiegeEngineDestroyedEvent`
- `GameMenuOpened` (used to remind why BuyTroops is disabled)
- Unsafe events now trigger temporary pause. Code exceptions still trigger permanent safety shutdown.
- Kept siege-context detector `IsSiegeContextUnsafe(...)` as fail-closed.
- Added WarSails guard:
- Port menu option is only added if WarSails is detected.
- Otherwise a safety log entry is written and option registration is skipped.

## Why this should help
- If BuyTroops hits risky state, it pauses while risky and auto-resumes when safe.
- If BuyTroops hits real code exceptions, it disables itself instead of continuing execution.
- Player gets explicit in-game reason for blocked menu behavior.
- Crash-prone siege/combat transitions now block BuyTroops only during the unsafe window.

## Validation checklist
1. Build `BuyTroops` and deploy to module folder.
2. Start game, enter siege flow, and reproduce catapult-destruction flow.
3. Confirm BuyTroops enters safety mode (in-game message appears).
4. Confirm `BuyTroops_Safety.log` contains disable reason and context.
5. If crash still occurs, compare timestamp with safety log and capture crash report for correlation.

## Current status
- Circuit breaker and safety logging implemented.
- Compile passes when overriding module output paths:
- `dotnet build BuyTroops/BuyTroops.csproj -c Debug -p:Platform=x64 -p:GameModulesPath=... -p:DocsModulesPath=...`
- Normal build may fail to copy if Bannerlord Launcher is open and locking `BuyTroops.dll`.
- Runtime validation pending.
