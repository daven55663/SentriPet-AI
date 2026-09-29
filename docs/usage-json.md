# Usage for other programs: usage.json and the OBS page

**English** | [繁體中文](usage-json.zh-TW.md)

SentriPet can hand its numbers to your own scripts, a Stream Deck or a live stream (since 2.3,
[#22](https://github.com/daven55663/SentriPet-AI/issues/22)). Both options are off by default; turn them on under
**Settings → For other programs**. Nothing leaves your computer: the file is in your settings folder and the web page
only answers on `127.0.0.1`.

## usage.json

With **Write usage.json** on, SentriPet keeps this file up to date:

| System | File |
|---|---|
| Windows | `%APPDATA%\SentriPet\usage.json` |
| macOS | `~/Library/Application Support/SentriPet/usage.json` |
| Linux | `~/.config/SentriPet/usage.json` (or `$XDG_CONFIG_HOME/SentriPet/usage.json`) |

It is rewritten when a number changes, and at least once a minute: if `updatedAt` is more than a couple of minutes
old, SentriPet is not running. The file is replaced in one step (written to `usage.json.tmp`, then renamed), so a
reader never sees half of it. Turning the option off deletes the file.

### Example

```json
{
  "version": 1,
  "app": "SentriPet",
  "appVersion": "2.3.0",
  "language": "en",
  "updatedAt": "2026-09-29T08:00:00Z",
  "lowestRemaining": 58.3,
  "providers": [
    {
      "id": "claude",
      "name": "Claude",
      "color": "#D97757",
      "plan": "Max",
      "hasData": true,
      "active": true,
      "stale": false,
      "unlimited": false,
      "remaining": 58.3,
      "mood": "good",
      "status": null,
      "error": null,
      "observedAt": "2026-09-29T07:57:00Z",
      "meters": [
        {
          "key": "fh",
          "label": "5 hours",
          "shortLabel": "5h",
          "windowMinutes": 300,
          "unlimited": false,
          "used": 41.7,
          "remaining": 58.3,
          "estimated": true,
          "resetsAt": "2026-09-29T10:13:00Z",
          "resetEstimated": false,
          "runsOutAt": null,
          "value": null
        }
      ]
    }
  ]
}
```

### Fields

Top level:

| Field | Type | Meaning |
|---|---|---|
| `version` | number | Format version, now `1` (see [Versions](#versions)) |
| `app`, `appVersion` | string | `"SentriPet"` and its version |
| `language` | string | The language of `label` and `status` (`zh-TW`, `zh-CN`, `en`, `ja`, `ko`) |
| `updatedAt` | string | When this was written (UTC, ISO 8601) |
| `lowestRemaining` | number or null | The lowest remaining % of any AI with a limit — what the tray icon shows |
| `providers` | array | One entry per AI shown in the widget, in the widget's order |

Each provider:

| Field | Type | Meaning |
|---|---|---|
| `id` | string | `claude`, `codex`, `copilot`, `ollama`, or a JSON plugin's id — use this in scripts |
| `name`, `color`, `plan` | string | Display name, colour (`#RRGGBB`), plan name (or null) |
| `hasData` | bool | False when nothing could be read (then `error` says why) |
| `active` | bool | The AI is working right now |
| `stale` | bool | The numbers are old |
| `unlimited` | bool | No limit (local models); `remaining` is then null |
| `remaining` | number or null | Remaining % of the window that runs out first (0–100, one decimal) |
| `mood` | string | `great` (≥ 60 %), `good` (≥ 30 %), `worried` (≥ 12 %), `critical`, `empty`, `unknown` |
| `status`, `error` | string or null | The status line shown on the card, and why there is no data |
| `observedAt` | string or null | When the numbers were measured (UTC) |
| `meters` | array | Every usage window |

Each meter:

| Field | Type | Meaning |
|---|---|---|
| `key` | string | The window's id from its source (Claude `fh` / `sd`, Codex `codex:300`, Copilot `premium_interactions`, …); to tell windows apart, `windowMinutes` is simpler |
| `label`, `shortLabel` | string | Its name in the app's language (`5 hours` / `5h`) |
| `windowMinutes` | number or null | Length of the window (300 = 5 hours, 10080 = a week) |
| `unlimited` | bool | No limit; `used` and `remaining` are null |
| `used`, `remaining` | number or null | Percent used / left (one decimal) |
| `estimated` | bool | The number includes an estimate on top of the last real reading (Claude between desktop samples) |
| `resetsAt` | string or null | When the window resets (UTC) |
| `resetEstimated` | bool | The reset time is worked out, not reported by the service |
| `runsOutAt` | string or null | At the recent pace it runs out before the reset, at this time (UTC) |
| `value` | string or null | A raw count when the source gives one, e.g. `"250 / 300"` |

### Versions

`version` changes only when a field is removed or changes its meaning. New fields can appear in version 1 at any time,
so ignore fields you do not know. Times are always UTC with a `Z`; percentages are 0–100.

### Reading it

PowerShell:

```powershell
$u = Get-Content "$env:APPDATA\SentriPet\usage.json" -Raw | ConvertFrom-Json
$u.providers | ForEach-Object { "{0}: {1}%" -f $_.name, $_.remaining }
```

bash with [jq](https://jqlang.org/) (macOS path; on Linux use `~/.config/SentriPet/usage.json`):

```bash
jq -r '.providers[] | "\(.name): \(.remaining // "?")%"' ~/Library/Application\ Support/SentriPet/usage.json
```

Python:

```python
import json, os, pathlib
f = pathlib.Path(os.environ["APPDATA"]) / "SentriPet" / "usage.json"   # Windows
for p in json.loads(f.read_text(encoding="utf-8"))["providers"]:
    print(p["name"], p["remaining"])
```

## The local web page (OBS)

With **Live stream page (OBS)** on, SentriPet serves a small web page on `http://127.0.0.1:47291/`:

| Address | What |
|---|---|
| `/` | A page with a transparent background: usage bars, or the pet (see the options below) |
| `/usage.json` | The same JSON as the file (also works while the file option is off) |
| `/pet.png` | A picture of the widget as it looks right now (also while it is hidden, e.g. only in the tray) |

In OBS: **Sources → + → Browser**, paste the address, set the width and height (for example 360 × 400), and leave
"Custom CSS" as it is (the page's background is already transparent).

Options, added to the address (`http://127.0.0.1:47291/?view=both&ids=claude`):

| Option | Values | Default |
|---|---|---|
| `view` | `bars`, `pet`, `both` | `bars` |
| `ids` | AIs to show, e.g. `claude,codex` | all |
| `layout` | `column` (under each other) or `row` (side by side) | `column` |
| `compact` | `1`: one bar per AI, the one that runs out first | off |
| `scale` | e.g. `1.5` | `1` |
| `panel` | `0`: no dark panel behind the text | on |
| `text` | `dark`: dark text for a light background | light |
| `fps` | pictures of the pet per second, 1–10 | `4` |

Notes:

- Only this computer can connect: the server listens on `127.0.0.1` and answers only requests addressed to
  `127.0.0.1`, `localhost` or `[::1]`, so a web site you visit cannot read it through DNS tricks, and it sends no
  cross-origin headers. It only answers `GET`.
- Another port: set `"usagePort"` in `settings.json` in the settings folder (1024–65535), then turn the switch off and
  on. If the port is taken, the settings page says so.
- The pet on the page is a picture refreshed a few times a second, not the live animation; the speech bubbles that
  appear next to the widget in their own window are not in it.
