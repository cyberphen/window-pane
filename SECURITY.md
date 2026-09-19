# Security Policy

## Supported versions

Security fixes are applied to the latest source on `main` for current Windows 10/11 builds.

## Reporting a vulnerability

Please report privately via **GitHub Security Advisories** on this repository:

https://github.com/cyberphen/window-pane/security/advisories/new

Include:

- Window Pane version / commit
- Windows version and architecture
- Steps to reproduce
- Impact (what an attacker or failure mode could cause)
- Logs **without** secrets or personal window contents

We aim to acknowledge within **3 business days** and share a status update within **14 business days**.

Please do **not** open a public issue for unreleased vulnerabilities.

## Scope

In scope: the Window Pane application, installer/binaries we publish, docking/capture behavior, and local settings handling.

Out of scope: social engineering, third-party apps you dock, unsupported forks/builds, and asking users to disable SmartScreen or Defender.

## Our safety posture

- No login, accounts, or cloud dependency
- No telemetry or automatic network calls from the app
- Runs `asInvoker` (no silent elevation)
- Captures only windows you explicitly dock; restores them on close when possible
- Cross-process `SetParent` is inherently fragile — we fail closed on integrity/session mismatches

## Safe harbor

Good-faith security research that stays within this policy and avoids harm to users or systems is welcome.
