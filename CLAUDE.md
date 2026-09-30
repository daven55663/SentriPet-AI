# SentriPet

Desktop widget that shows AI plan usage (Claude, Codex, Copilot, Ollama, JSON plugins) as animated themes, on Windows, macOS and Linux. C# on .NET 10 with Avalonia 12. GitHub: https://github.com/daven55663/SentriPet-AI. User-facing docs are in `README.md` (Traditional Chinese — reply to the user in Traditional Chinese, including progress updates). The Windows-only WPF version (≤ 1.4) was retired in 2.0; it lives in the tag `v1.4.0`.

## Layout

- `src/Core` — usage model, settings, reminders (`Trackers`), lines the pets say, translations (`I18n.cs`), `AppPaths` per OS, `Probe`, `ClaudeStatusLine` (the Claude Code status-line bridge, #9: `SentriPet --statusline` saves the official `rate_limits` and runs the user's own status line; Connect/Disconnect edit `~/.claude/settings.json` with a backup — only when the user turns it on). `AgentHooks` (#14: `SentriPet --hook claude|codex` records "done / waiting" events next to the tool's settings; Connect/Disconnect edit Claude Code's hooks and Codex's `notify` with a backup, keeping the user's own), `Quiet` (#17 quiet hours), `UsageHistory` (#15 pace/forecast, #16 report of weekly/monthly windows in report.json, incl. how long before the reset a quota ran out), `CustomLines` (#25: the user's lines.json; every line in `Lines` goes through `Choose`), `Progress` (#23 growing pets: experience for planned — not maximal — use, levels, achievements, progress.json), `ThemeSpec` (#24: parses and checks the user's themes/<folder>/theme.json — data only, pictures confined to the folder, problems collected instead of thrown), `Account`/`AccountSetup` (#26: other Claude Code / Codex accounts in `AppSettings.Accounts`; `ClaudeProvider(account)` reads only that folder's status line — `SentriPet --statusline --claude-dir <dir>` — and `CodexProvider(account)` runs app-server with CODEX_HOME). No UI code (colours are `Rgba`).
- `src/Providers` — one class per AI (detection + `Fetch`), `CustomProvider` for JSON plugins; `ClaudeCodeUsage` reads Claude Code transcript token counts to extrapolate between the desktop app's ~15-minute usage samples; `TokenLedger` collects the last month's token counts (Claude Code + Codex) for the usage report.
- Usage report & export (2.3): `ApiPrices` + embedded `src/Core/prices.json` (official API prices with the date checked — refresh when the core test says it is old); `UsageExport`/`UsageExporter` write the versioned `usage.json` (format in `docs/usage-json.md`, keep it and the zh-TW copy in step; bump `version` only for breaking changes); `UsageServer` serves the OBS page (`src/Core/overlay.html`, embedded) on 127.0.0.1 only, checking the Host header. Desktop: `ReportWindow`, `ReportChart`, `ShareCard`, `TrayArt.DrawNumber`, `PetWindow.Picture`.
- `src/Lang/*.json` — translations; `src/Tests` — core checks (run by `xplat/SentriPet.Tests`).
- `xplat/SentriPet.Desktop` (assembly `SentriPet`) — the app: `PetWindow` (transparent widget; hover is polled against provider bounding boxes), `DetailWindow.cs` (hover card, speech bubble, placement), `SettingsWindow`, `DesktopController` (menus, tray, reminders), `Themes/` (8 themes, provider elements tagged `Tag = "pv:{id}"`; `CustomTheme` draws a `ThemeSpec`, `ThemeCatalog.Choices` = built-in + the user's, `ExampleTheme` installs the embedded examples/themes/cloud), `Integration.cs` (everything that differs per OS: autostart, notifications, click-through, full screen, single instance, Start menu shortcut, global cursor), `TrayArt`, `DesktopTests` (`--selftest`).

## Build / run / install

- .NET 10 SDK: `"C:\Program Files\dotnet\dotnet.exe"` (not on the tool shell's PATH). Set `DOTNET_CLI_TELEMETRY_OPTOUT=1`.
- `dotnet build SentriPet.slnx -c Release`; the app is `xplat/SentriPet.Desktop/bin/Release/net10.0/SentriPet.exe`.
- `install.cmd` publishes a single-file build to `%LOCALAPPDATA%\Programs\SentriPet` and starts it through `explorer.exe`; `uninstall.cmd` removes it. Run scripts by absolute path (`cmd /c "<repo>\install.cmd"`).
- `xplat/package.sh <win-x64|osx-arm64|osx-x64|linux-x64>` builds a self-contained package into `dist/`.
- Package managers: this repo is also a Scoop bucket (`bucket/sentripet.json`) and a Homebrew tap (`Casks/sentripet.rb`); `packaging/winget/` holds the manifests for microsoft/winget-pkgs. `python packaging/update-manifests.py <version>` rewrites all of them from a published release (the Release workflow does this and then runs `packages.yml`, which installs through Scoop and Homebrew and self-tests).

## Checking changes without touching the user's mouse

All of these use a separate dev profile:

- `SentriPet.exe --selftest <file>` — the app's checks (themes, faces, card and placement, bubble, settings page, languages, tray icon, integration); exit code = failures. `xplat/SentriPet.Tests/bin/Release/net10.0/SentriPet.Tests.exe <file>` — the core checks. Run both before every push.
- `SentriPet.exe --snapshot <dir> [--theme id] [--frames N] [--lang xx]` renders every theme with sample data (sets a/b/c; c = quota about to expire unused at levels 1–3), the hover cards, the speech bubble and the settings page to PNG, headless.
- `SentriPet.exe --dev --smoke-test <file>` runs the real app for 15 s and reports window, frames, theme, tray and logged errors (a window appears on screen).
- `SentriPet.exe --demo <file.gif> [--lang xx]` renders the README demo (`docs/images/demo.gif` = en, `demo.zh-TW.gif`); `--social-card <file.png>` the repository's social preview (`docs/images/social-preview.png`).
- `SentriPet.exe --dev --perf-test <file> [--perf-seconds N] [--perf-scale 1.3] [--perf-themes glass,pet]` runs the real widget and measures its CPU per theme at fixed frame rates, the automatic one and hidden (a window appears on screen).
- `--probe <file>` prints detection + live usage (incl. the Claude estimate calibration) for every provider. `--dev --show-detail <id>` forces one hover card open for 45 s. `--lang <code>` forces a language.
- READMEs: `README.md` (English) and `README.zh-TW.md` (Traditional Chinese) — keep both in step.
- The desktop app's `get_usage` tool (ccd_session_mgmt) returns the official live Claude numbers — use it to check the Claude estimate.
- CI (`.github/workflows/ci.yml`) runs the core tests, `--selftest`, `--probe`, `--smoke-test` (xvfb on Linux) and `--snapshot` on Windows, macOS and Linux, and builds the packages. Pushing a `v*` tag runs `release.yml` (draft → packages self-tested → published); ask the user before tagging a release.

## Languages (i18n)

- The Traditional Chinese text in the code is the translation key: wrap user-facing text in `L.T("…")`, sentences with values in `L.F("…{0}…", x)` (never build sentences by concatenation), and texts stored in tables in `L.N("…")` (translated later with `L.T`). `Lines` uses named placeholders (`{name}`, `{pct}`…).
- Translations live in `src/Lang/{zh-CN,en,ja,ko}.json`, embedded by `SentriPet.Core.csproj` as `SentriPet.Lang.<code>.json`. The core tests fail when any CJK string literal in `src`/`xplat` (test files excluded) has no entry in every file, or placeholders differ. Lines that must not be translated (regex, font names, language names) carry `// i18n-ignore`.
- Korean: Avalonia breaks Korean between any two syllables, so `L.KeepWords` puts U+2060 WORD JOINER between a syllable and any non-space neighbour (applied to the ko table, `L.F` and `Lines`). Text glued together outside `L.T`/`L.F` must go through `L.Finish(...)`; the self-test checks every Korean text on screen.
- Check layout per language with `--snapshot <dir> --lang <code>` — Japanese/Korean phrases often need to be shorter than the Chinese ones to fit the pet plates.

## Platform notes

- Windows draws on the CPU by default (`--gpu` for the GPU): about 110 MB instead of 250 MB. Microsoft JhengHei draws ≈ badly through Skia, so Windows shows `~` for estimates (`G.Approx`).
- macOS: brush-style CJK fonts (Kaiti, BiauKai, Klee…) are downloaded on demand and stall text layout — never list them on a Mac. Transform origins: Avalonia rotates/scales around the element centre by default (WPF used the top-left); use `G.At(x, y)`.
- CPU (#8): almost all of it is drawing, not the themes' code. Never put a `DropShadowEffect` on a container whose children change (every change inside re-blurs the whole thing): Borders take `BoxShadow = G.Shadow(...)`, effects (`G.Glow`, `G.ShapeShadow`) only on small elements. Only set properties when the value changed (springs creep forever; a new geometry or brush every frame redraws every frame). The widget animates at ~16 fps idle and ~22 fps while something happens (`PetWindow.Lively`), and not at all while hidden.
- Off-screen drawing at a scale goes through `Snapshots.Draw` (a scale transform at 96 dpi): at a higher dpi Avalonia draws a Border that has a BoxShadow without the dpi scale.
- Templated controls (switches, sliders) only get their look inside a window; headless snapshots of the settings page show a real (headless) window first.

## Claude desktop (MSIX) virtualization

Processes started from the Claude desktop app's tools inherit its package file virtualization: **new top-level folders created under `%APPDATA%` or `%LOCALAPPDATA%` land in `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\...`** instead of the real location (registry writes are not affected). So:
- start the real app with `explorer.exe "<exe>"` (install.cmd does), never directly from the tool shell;
- read the real settings/logs via `\\localhost\C$\Users\%USERNAME%\AppData\Roaming\SentriPet\...`;
- don't create files under AppData from the tool shell.

Record progress in `docs/DEVLOG.md`. The development/test machine and tool versions are in `docs/environment.md` and `docs/environment.zh-TW.md` (linked from both READMEs) — update both when they change, and copy the table into each release's notes (the user asked for it).
