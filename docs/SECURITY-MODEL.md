# Security model (Window Pane)

## What the app does

Window Pane can reparent another process’s top-level HWND into its own host (`SetParent`), hide it from Alt+Tab/taskbar, and later restore styles/parent/position.

Microsoft documents that cross-process parenting is legal but attaches input queues and is hard to manage when the child app did not expect it. See Raymond Chen / Windows documentation on cross-process parent/child relationships.

## Threats we care about

| Risk | Mitigation |
|------|------------|
| Docking an elevated / higher-integrity window into a medium IL host | Reject when integrity RIDs or user SID / session differ |
| Silent UAC / elevation | `asInvoker`, `uiAccess=false`; never auto-elevate |
| Looking like malware (hiding windows without consent) | Explicit drag/hotkey capture; visible chrome; restore on close |
| Crash leaving foreign windows stuck as children | Restore all captured windows on form close / dispose |
| SmartScreen friction on downloads | Prefer signed releases + published hashes (when publishing installers) |

## Capture policy (fail closed)

Before `SetParent`:

1. Resolve `GA_ROOT` top-level HWND
2. Reject self, shell classes, tiny windows, already-captured windows
3. Require same session and same user SID
4. Require equal integrity level (no docking up/down across UIPI)
5. Snapshot style/exstyle/rect/parent before mutation

## Out of scope for v1

- Full external watchdog process (planned enhancement)
- Authenticode signing pipeline (recommended for release binaries)
- Networked remote control
