# 外掛：把任何 AI 服務接上桌寵

把一個 `.json` 設定檔放進這個資料夾（`%APPDATA%\SentriPet\providers`），
然後在「設定 → AI 服務」按 **重新載入外掛**，就會多一隻新的桌寵。

資料夾裡的 `*.json.example` 是範例，**改名成 `.json`** 就會生效。
檔名無所謂，一個檔案就是一個 AI 服務。`id` 和內建的 claude / codex / copilot / ollama 相同時，會取代內建版本。

---

## 1. 最萬用：跑一支程式，印出 JSON

任何語言都可以（Python、PowerShell、Node、exe……），只要程式最後印出這種格式：

```json
{
  "plan": "Pro",
  "note": "想顯示在詳細資訊裡的一句話（選填）",
  "meters": [
    { "label": "5 小時", "used": 37, "resetsAt": "2026-09-23T18:00:00+08:00", "windowMinutes": 300 },
    { "label": "每週",   "used": 12, "windowMinutes": 10080 }
  ]
}
```

外掛設定：

```json
{
  "id": "my-ai",
  "name": "我的 AI",
  "color": "#F472B6",
  "mascot": "cat",
  "intervalSeconds": 60,
  "source": { "type": "command", "command": "python", "args": ["C:\\tools\\my_usage.py"], "timeoutSeconds": 20 }
}
```

`.ps1` 會自動用 PowerShell 執行，`.cmd` / `.bat` 會用 cmd 執行。範例：`my_usage_example.py`。

## 2. 直接呼叫 HTTP API

```json
{
  "id": "openrouter",
  "name": "OpenRouter",
  "detect": { "env": ["OPENROUTER_API_KEY"] },
  "source": {
    "type": "http",
    "url": "https://openrouter.ai/api/v1/credits",
    "headers": { "Authorization": "Bearer ${env:OPENROUTER_API_KEY}" }
  },
  "meters": [
    { "label": "儲值額度", "used": "$.data.total_usage", "total": "$.data.total_credits", "valueText": "${used} / ${total}" }
  ]
}
```

金鑰請放在環境變數，用 `${env:名稱}` 引用，不要直接寫在檔案裡。

## 3. 讀取別的程式寫出來的 JSON 檔

```json
{
  "id": "file-demo",
  "name": "檔案範例",
  "detect": { "paths": ["~/my-ai-usage.json"] },
  "source": { "type": "file", "path": "~/my-ai-usage.json" },
  "metersFrom": {
    "path": "$.limits",
    "used": "percent",
    "resetsAt": "reset",
    "labels": { "daily": "每日", "monthly": "每月" },
    "windowMinutes": { "daily": 1440, "monthly": 43200 }
  }
}
```

`metersFrom` 會把某個物件（或陣列）裡的每一項都變成一條額度。

---

## 欄位說明

| 欄位 | 說明 |
|---|---|
| `id` / `name` | 識別碼／顯示名稱 |
| `color` | 桌寵顏色，例如 `#22C55E`（不填會自動配色） |
| `mascot` | 頭上的配件：`sparkle` 星星、`prompt` 小螢幕、`goggles` 護目鏡、`llama` 長耳朵、`cat` 貓耳、`antenna` 天線 |
| `detect` | 什麼時候算「有安裝」：`paths`（資料夾或檔案，可用 `*`）、`commands`（PATH 上的指令）、`env`（環境變數）、`editorExtensions`（VS Code 擴充前綴）、`always: true`。不寫就一律顯示 |
| `intervalSeconds` | 多久更新一次（最少 10 秒，預設 120） |
| `source.type` | `command` / `http` / `file` |
| `plan` / `note` | 文字，或用 `$.路徑` 從資料裡取 |
| `meters[].used` | 已用百分比 0–100，或 `$.路徑` |
| `meters[].remaining` | 剩餘百分比（和 `used` 二選一） |
| `meters[].total` | 有給的話，`used` / `remaining` 會被當成「數量」自動換算成百分比 |
| `meters[].scale` | 原始數值要乘的倍數（例如 API 給 0~1 就寫 `100`） |
| `meters[].resetsAt` | 重置時間：ISO 字串、Unix 秒或毫秒 |
| `meters[].windowMinutes` | 週期長度（分鐘），用來決定標籤和藥水瓶的形狀 |
| `meters[].valueText` | 額外顯示文字，可用 `{used}` `{remaining}` `{total}` `{percent}` |
| `meters[].unlimited` | `true` 代表沒有上限（顯示 ∞） |

路徑語法：`$.a.b[0].c`。字串裡可以用 `%USERPROFILE%`、`~`、`${env:NAME}`。

出問題時可以看記錄檔：`%APPDATA%\SentriPet\logs\app.log`。
