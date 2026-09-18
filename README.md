# Window Pane

A free, open-source Windows utility that docks other app windows into one clean pane.

**Developer:** Dr. Psych

## Principles

- **No login**
- **No telemetry / no tracking / no phoning home**
- **No accounts, no cloud dependency**
- Settings stay on your machine (`%AppData%\WindowPane\settings.json`)
- Free for **personal and commercial** use — use, copy, modify, distribute, and sell

Licensed under the [MIT License](LICENSE).

## What it does

- Borderless host pane with a thin optional border
- Drag a window **by its title bar** onto the pane to dock it
- Docked windows leave the taskbar and Alt+Tab; they live only inside the pane
- Closing the pane restores apps to their previous size, position, and visibility
- Left traffic lights: close · minimize · maximize
- Bottom: full screen · settings (empty content, border, themes)

## Run

Requires [.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```powershell
cd WindowPane
dotnet run -c Release
```

Or run:

`WindowPane\bin\Release\net8.0-windows\WindowPane.exe`

## Controls

| Action | How |
|--------|-----|
| Dock a window | Drag by its title bar onto the pane, then release |
| Dock foreground | `Ctrl+Shift+A` or right-click → Capture |
| Switch docked windows | Bottom tabs |
| Release one window | Middle-click its tab |
| Move pane | Drag empty space on the left bar |
| Full screen | Square icon (or `F11`) |
| Settings | Gear icon |
| Resize | Drag the thin outer border / corners |
| Close (restore all) | Red light, or `Esc` when not full screen |

## Notes

- Some UWP apps and elevated (admin) windows may refuse hosting unless Window Pane is also elevated.
- A few Chromium / Electron apps can look odd after reparenting; release them if that happens.
- Windows Snap still works at screen edges; drop inside the pane (away from edges) to dock.

## Contributing

Issues and PRs welcome. Keep the project free of telemetry and account walls.
