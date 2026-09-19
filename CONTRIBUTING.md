# Contributing

Thanks for helping keep Window Pane free, private, and useful.

## Principles

1. **No telemetry / no login / no cloud dependency** in the app
2. Prefer **minimal, modular** changes over large rewrites
3. Fail **closed** on security checks (integrity, session, shell windows)
4. Docking must remain **user-initiated** and **restorable**

## Development

Requires .NET 8 SDK on Windows.

```powershell
cd WindowPane
dotnet build -c Release
dotnet run -c Release
```

## Pull requests

- Keep diffs focused
- Do not add analytics SDKs, auto-updaters, or account systems
- Document user-visible behavior changes in the PR description
- For security-sensitive changes, explain the threat model impact

## License

By contributing, you agree your contributions are licensed under the MIT License (see [LICENSE](LICENSE)).
