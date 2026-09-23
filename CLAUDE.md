# SentriPet

Windows desktop widget (C# 5 / WPF on .NET Framework 4.8) that shows AI plan usage (Claude, Codex, Copilot, Ollama, JSON plugins) as animated themes. GitHub: https://github.com/daven55663/SentriPet-AI. User-facing docs are in `README.md` (Traditional Chinese — reply to the user in Traditional Chinese).

## Build / install

- `build.cmd` → `bin\SentriPet.exe`, compiled with the csc that ships with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`). C# 5 only: no `$""`, `?.`, `=>` members, `nameof`, auto-property initializers, `out var`.
- Run scripts by absolute path (`cmd /c "<repo>\build.cmd"`); this environment does not search the current directory for scripts.
- `install.cmd` copies to `%LOCALAPPDATA%\Programs\SentriPet` and starts the app through `explorer.exe`.

## Checking changes without touching the user's mouse

All of these run with a separate dev profile automatically:

- `bin\SentriPet.exe --snapshot <dir> --mock [--theme id] [--frames N]` renders every theme to PNG (mock set `c` = quota about to expire unused at levels 1–3; `--frames` adds N frames 0.25 s apart to check animations).
- `--snapshot-ui <dir>` renders the menu, settings page and hover card.
- `--selftest <file>` checks hover-card placement (exit code = failures).
- `--probe <file>` prints detection + live usage (incl. the Claude estimate calibration) for every provider.
- `--dev --show-detail <id>` runs a separate profile and forces one hover card open for 45 s.
- The desktop app's `get_usage` tool (ccd_session_mgmt) returns the official live Claude numbers — use it to check the Claude estimate.

## Claude desktop (MSIX) virtualization

Processes started from the Claude desktop app's tools inherit its package file virtualization: **new top-level folders created under `%APPDATA%` or `%LOCALAPPDATA%` land in `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\...`** instead of the real location (registry writes are not affected). So:
- start the real app with `explorer.exe "<exe>"` (install.cmd does), never directly from the tool shell;
- read the real settings/logs via `\\localhost\C$\Users\%USERNAME%\AppData\Roaming\SentriPet\...`;
- don't create files under AppData from the tool shell.

## Layout

- `src/Providers` — one class per AI (detection + `Fetch`), `CustomProvider` for JSON plugins; `ClaudeCodeUsage` reads Claude Code transcript token counts to extrapolate between the desktop app's ~15-minute usage samples.
- `src/Themes` — `Theme` base + 8 themes; provider elements are tagged `Tag = "pv:{id}"` for hover/click.
- `src/UI/PetWindow.cs` — transparent widget; hover is polled against provider bounding boxes (not mouse events).
- `src/UI/DetailWindow.cs` — hover card: owned, click-through window placed outside the widget content.
