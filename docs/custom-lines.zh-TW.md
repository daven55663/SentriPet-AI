# 自訂台詞：lines.json

[English](custom-lines.md) | **繁體中文**

桌寵可以說你自己寫的台詞：你的梗、你的口氣、你的語言（2.4 起，[#25](https://github.com/daven55663/SentriPet-AI/issues/25)）。
**設定 → 外觀 → 自訂台詞 →「開啟台詞檔」** 會在設定資料夾建立附範例的 `lines.json` 並打開它。存檔後幾秒內就會生效；
設定頁會顯示目前用了幾句自訂台詞，寫錯的地方也會列出來。

| 系統 | 檔案 |
|---|---|
| Windows | `%APPDATA%\SentriPet\lines.json` |
| macOS | `~/Library/Application Support/SentriPet/lines.json` |
| Linux | `~/.config/SentriPet/lines.json` |

## 格式

```jsonc
{
  "version": 1,
  "mix": true,               // false（預設）：該事件只用你的台詞；true：和內建的混著說
  "zh-TW": {
    "poke": ["又來戳我！{name} 還有 {pct} 喔", "今天的 bug 修完了嗎？"],
    "done": "{name} 做完了，花了 {time}"        // 只有一句時可以直接寫字串
  },
  "en": {
    "poke": ["Hey! {name} still has {pct} left"]
  },
  "all": {
    "greeting": ["(=^･ω･^=) {names}"]         // "all"：所有語言都用
  }
}
```

- 先依語言分組：`zh-TW`、`zh-CN`、`en`、`ja`、`ko` 或 `all`，再依事件分組。
- 每個事件先找目前語言的那一組，再找 `all`；兩邊都沒有就用內建台詞。
- 可以寫註解（`//`）和多餘的逗號；`_` 開頭的鍵會被忽略。
- 用了該事件沒有的記號（例如打錯成 `{nmae}`）的那一句會被略過，設定頁會說是哪一句；整個事件都沒有可用的句子時用內建的。
  整個檔案讀不懂時什麼都不會改變。
- [自訂造型](custom-themes.zh-TW.md#造型自帶的台詞)也可以附一個自己的 `lines.json`（2.5 起）。換上那個造型時，造型的台詞和你的台詞會一起說；
  某個事件只有在每個寫了它的檔案都設 `"mix": true` 時，才會再混進內建的台詞。

## 事件

| 事件 | 什麼時候 | 可以用的記號 |
|---|---|---|
| `greeting` | 開啟後第一次打招呼 | `{names}` |
| `poke` | 點桌寵 | `{name}` `{pct}` `{meter}` `{reset}` `{plan}` `{second}` `{secondpct}` |
| `idleGreat` | 偶爾自己說話，剩 60% 以上 | `{name}` `{pct}` `{meter}` `{reset}` |
| `idleGood` | …剩 30～60% | 同上 |
| `idleWorried` | …剩 12～30% | 同上 |
| `idleLow` | …剩不到 12% | 同上 |
| `idleEmpty` | …用完了 | 同上 |
| `working` | …AI 正在工作時 | 同上 |
| `stale` | …資料太舊時 | 同上 |
| `warn` | 用量超過提醒門檻（也是通知的內容） | 同上 |
| `critical` | 用量超過緊急門檻（也是通知的內容） | 同上 |
| `reset` | 額度重置 | 同上 |
| `runsOut` | 照最近的速度會在重置前用完 | 同上＋`{time}` |
| `useIt` | 每週／每月額度快重置卻還剩很多 | 同上＋`{quota}` `{left}` `{when}` `{upct}` |
| `done` | Claude Code／Codex 做完任務 | `{name}` `{project}` `{time}` |
| `waiting` | …在等你回覆 | `{name}` `{project}` |
| `permission` | …要你確認 | `{name}` `{project}` |
| `weekSummary` | 每週／每月額度結束時的總結 | `{name}` `{quota}` `{used}` `{left}` |

## 記號

| 記號 | 會換成 |
|---|---|
| `{name}` | AI 的名稱（`Claude`） |
| `{pct}` | 主要額度還剩多少（`58%`） |
| `{meter}` | 那個額度的名稱（`5 小時`） |
| `{reset}` | 距離重置還有多久（`2時13分`） |
| `{plan}` | 方案（`Max`） |
| `{second}`、`{secondpct}` | 第二個額度的名稱與剩餘 |
| `{time}` | `runsOut`：幾點用完（`今天 16:54`）；`done`：任務花了多久（`1分30秒`） |
| `{quota}` | 額度的說法（`每週額度`） |
| `{left}` | `useIt`：距離重置還有多久；`weekSummary`：浪費掉的部分（`18%`） |
| `{when}` | 額度什麼時候重置（`週四 23:00`） |
| `{upct}` | 快過期的那個額度還剩多少 |
| `{used}` | 那一期用掉多少（`82%`） |
| `{project}` | AI 工作的資料夾名稱（可能是空的） |
| `{names}` | 找到的所有 AI（`Claude、Codex`） |

台詞會照你寫的顯示；韓文的話，SentriPet 會自動讓每個詞不被拆到兩行。
