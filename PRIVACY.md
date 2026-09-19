# Privacy

Window Pane is designed to work **entirely offline**.

## What we do not do

- No accounts or login
- No telemetry, analytics, advertising IDs, or phoning home
- No automatic crash reporting to a server
- No updater that phones home (you update from GitHub when you choose)

## What stays on your PC

Settings are stored only in:

`%AppData%\WindowPane\settings.json`

Typical contents: empty-pane preference, border on/off, theme name.

Window titles, process names, and HWNDs are used **in memory** while docking so the app can host and restore windows. They are not uploaded.

## Deleting local data

Close Window Pane, then delete the folder:

`%AppData%\WindowPane\`

## Third parties

Windows itself and any app you dock may collect data under their own policies. Window Pane does not add a network path for that.

## Contact

Privacy questions: open a GitHub Discussion or Issue on the repository (non-sensitive topics only). Security issues: see [SECURITY.md](SECURITY.md).
