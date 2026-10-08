# Your own lines: lines.json

**English** | [繁體中文](custom-lines.zh-TW.md)

The pets can say your own lines — your jokes, your tone, your language (since 2.4,
[#25](https://github.com/daven55663/SentriPet-AI/issues/25)). *Settings → Appearance → Your own lines → Open lines file*
creates `lines.json` in the settings folder with a small example and opens it. Saved changes are picked up within a few
seconds; the settings page shows how many of your lines are in use and anything that is wrong.

| System | File |
|---|---|
| Windows | `%APPDATA%\SentriPet\lines.json` |
| macOS | `~/Library/Application Support/SentriPet/lines.json` |
| Linux | `~/.config/SentriPet/lines.json` |

## Format

```jsonc
{
  "version": 1,
  "mix": true,               // false (default): your lines replace the built-in ones for that event; true: both are said
  "zh-TW": {
    "poke": ["又來戳我！{name} 還有 {pct} 喔", "今天的 bug 修完了嗎？"],
    "done": "{name} 做完了，花了 {time}"        // one line can be a plain string
  },
  "en": {
    "poke": ["Hey! {name} still has {pct} left"]
  },
  "all": {
    "greeting": ["(=^･ω･^=) {names}"]         // "all": every language
  }
}
```

- Group the lines by language — `zh-TW`, `zh-CN`, `en`, `ja`, `ko`, or `all` — then by event.
- For each event, the lines of the language in use are taken first, then `all`; an event with neither keeps the built-in lines.
- Comments (`//`) and trailing commas are fine. Keys starting with `_` are ignored.
- A line that uses a placeholder its event doesn't have (a typo like `{nmae}`) is skipped and the settings page says which
  one; if no line of an event is left, the built-in ones are used. A file that can't be read at all changes nothing.
- A [custom theme](custom-themes.md#its-own-lines) can bring a `lines.json` of its own (since 2.5). While it is in use,
  its lines and yours are said together; the built-in lines join in only when every file with lines for that event
  says `"mix": true`.

## Events

| Event | When | Placeholders |
|---|---|---|
| `greeting` | The first hello after starting | `{names}` |
| `poke` | You click a pet | `{name}` `{pct}` `{meter}` `{reset}` `{plan}` `{second}` `{secondpct}` |
| `idleGreat` | Now and then, 60 % or more left | `{name}` `{pct}` `{meter}` `{reset}` |
| `idleGood` | … 30–60 % left | same |
| `idleWorried` | … 12–30 % left | same |
| `idleLow` | … less than 12 % left | same |
| `idleEmpty` | … used up | same |
| `working` | … while the AI is working | same |
| `stale` | … when the numbers are old | same |
| `warn` | A quota passes the warning threshold (also the notification) | same |
| `critical` | A quota passes the critical threshold (also the notification) | same |
| `reset` | A quota resets | same |
| `runsOut` | At the recent pace a quota runs out before its reset | same + `{time}` |
| `useIt` | A weekly/monthly quota is about to reset unused | same + `{quota}` `{left}` `{when}` `{upct}` |
| `done` | Claude Code / Codex finished a task | `{name}` `{project}` `{time}` |
| `waiting` | … waits for your reply | `{name}` `{project}` |
| `permission` | … needs your OK | `{name}` `{project}` |
| `weekSummary` | A weekly/monthly quota ended | `{name}` `{quota}` `{used}` `{left}` |

## Placeholders

| Placeholder | Becomes |
|---|---|
| `{name}` | The AI's name (`Claude`) |
| `{pct}` | Left in the main quota (`58%`) |
| `{meter}` | That quota's name (`5-hour`) |
| `{reset}` | Time until it resets (`2h 13m`) |
| `{plan}` | The plan (`Max`) |
| `{second}`, `{secondpct}` | The second quota's name and what's left of it |
| `{time}` | `runsOut`: when it runs out (`today 16:54`); `done`: how long the task took (`1m 30s`) |
| `{quota}` | The quota as a phrase (`weekly quota`) |
| `{left}` | `useIt`: time until the reset; `weekSummary`: the part that was wasted (`18%`) |
| `{when}` | When the quota resets (`Thu 23:00`) |
| `{upct}` | Left in the quota that is about to expire |
| `{used}` | How much of the quota was used (`82%`) |
| `{project}` | The folder the AI worked in (may be empty) |
| `{names}` | All the AIs found (`Claude, Codex`) |

Lines are shown as written; in Korean, SentriPet keeps words together on one line for you.
