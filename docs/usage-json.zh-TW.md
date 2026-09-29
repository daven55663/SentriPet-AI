# 給其他程式用：usage.json 與 OBS 直播畫面

[English](usage-json.md) | **繁體中文**

SentriPet 可以把用量交給你自己的腳本、Stream Deck 或直播畫面（2.3 起，
[#22](https://github.com/daven55663/SentriPet-AI/issues/22)）。兩個選項預設都關閉，在 **設定 → 給其他程式用** 打開。
資料不會離開你的電腦：檔案在設定資料夾裡，網頁只在 `127.0.0.1` 回應。

## usage.json

打開 **輸出 usage.json** 後，SentriPet 會持續更新這個檔案：

| 系統 | 檔案 |
|---|---|
| Windows | `%APPDATA%\SentriPet\usage.json` |
| macOS | `~/Library/Application Support/SentriPet/usage.json` |
| Linux | `~/.config/SentriPet/usage.json`（或 `$XDG_CONFIG_HOME/SentriPet/usage.json`） |

數字一變就重寫，沒變也至少每分鐘寫一次：`updatedAt` 超過兩三分鐘沒更新，就代表 SentriPet 沒在執行。
檔案是一次換掉的（先寫 `usage.json.tmp` 再改名），讀的程式不會讀到寫一半的內容。關掉選項會刪除這個檔案。

### 範例

```json
{
  "version": 1,
  "app": "SentriPet",
  "appVersion": "2.3.0",
  "language": "zh-TW",
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
          "label": "5 小時",
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

### 欄位

最外層：

| 欄位 | 型別 | 意思 |
|---|---|---|
| `version` | 數字 | 格式版本，目前是 `1`（見[版本](#版本)） |
| `app`、`appVersion` | 字串 | `"SentriPet"` 與它的版本 |
| `language` | 字串 | `label` 與 `status` 用的語言（`zh-TW`、`zh-CN`、`en`、`ja`、`ko`） |
| `updatedAt` | 字串 | 寫出的時間（UTC，ISO 8601） |
| `lowestRemaining` | 數字或 null | 所有有上限的 AI 裡最低的剩餘 %，也就是系統匣圖示顯示的數字 |
| `providers` | 陣列 | 桌寵上顯示的每個 AI 一筆，順序和桌寵一樣 |

每個 AI：

| 欄位 | 型別 | 意思 |
|---|---|---|
| `id` | 字串 | `claude`、`codex`、`copilot`、`ollama`，或 JSON 外掛的 id；腳本請用這個判斷 |
| `name`、`color`、`plan` | 字串 | 顯示名稱、顏色（`#RRGGBB`）、方案名稱（或 null） |
| `hasData` | 布林 | 讀不到資料時是 false（原因在 `error`） |
| `active` | 布林 | 正在工作 |
| `stale` | 布林 | 數字比較舊 |
| `unlimited` | 布林 | 沒有上限（本機模型），這時 `remaining` 是 null |
| `remaining` | 數字或 null | 最先用完的那個額度還剩幾 %（0–100，小數一位） |
| `mood` | 字串 | `great`（≥ 60%）、`good`（≥ 30%）、`worried`（≥ 12%）、`critical`、`empty`、`unknown` |
| `status`、`error` | 字串或 null | 卡片上的狀態文字，以及沒有資料的原因 |
| `observedAt` | 字串或 null | 數字量到的時間（UTC） |
| `meters` | 陣列 | 每一個額度視窗 |

每個額度：

| 欄位 | 型別 | 意思 |
|---|---|---|
| `key` | 字串 | 資料來源給的代號（Claude `fh`／`sd`、Codex `codex:300`、Copilot `premium_interactions`…）；要分辨是哪個視窗，用 `windowMinutes` 比較簡單 |
| `label`、`shortLabel` | 字串 | 用目前語言寫的名稱（`5 小時`／`5h`） |
| `windowMinutes` | 數字或 null | 視窗長度（300 = 5 小時，10080 = 一週） |
| `unlimited` | 布林 | 沒有上限，`used` 與 `remaining` 是 null |
| `used`、`remaining` | 數字或 null | 用掉／剩下的 %（小數一位） |
| `estimated` | 布林 | 數字包含在最後一次實際讀數之上的估計（Claude 在桌面版兩次取樣之間） |
| `resetsAt` | 字串或 null | 重置時間（UTC） |
| `resetEstimated` | 布林 | 重置時間是推算的，不是服務直接給的 |
| `runsOutAt` | 字串或 null | 照最近的速度會在重置前用完，用完的時間（UTC） |
| `value` | 字串或 null | 來源有給原始次數時，例如 `"250 / 300"` |

### 版本

只有在移除欄位或改變欄位意思時 `version` 才會變。版本 1 之後仍可能加入新欄位，請忽略看不懂的欄位。
時間一律是結尾帶 `Z` 的 UTC；百分比是 0–100。

### 讀取範例

PowerShell：

```powershell
$u = Get-Content "$env:APPDATA\SentriPet\usage.json" -Raw | ConvertFrom-Json
$u.providers | ForEach-Object { "{0}: {1}%" -f $_.name, $_.remaining }
```

bash 搭配 [jq](https://jqlang.org/)（macOS 路徑；Linux 改成 `~/.config/SentriPet/usage.json`）：

```bash
jq -r '.providers[] | "\(.name): \(.remaining // "?")%"' ~/Library/Application\ Support/SentriPet/usage.json
```

Python：

```python
import json, os, pathlib
f = pathlib.Path(os.environ["APPDATA"]) / "SentriPet" / "usage.json"   # Windows
for p in json.loads(f.read_text(encoding="utf-8"))["providers"]:
    print(p["name"], p["remaining"])
```

## 本機網頁（OBS）

打開 **直播畫面（OBS）** 後，SentriPet 會在 `http://127.0.0.1:47291/` 提供一個小網頁：

| 網址 | 內容 |
|---|---|
| `/` | 透明背景的網頁：用量條，或桌寵（見下面的選項） |
| `/usage.json` | 和檔案一樣的 JSON（沒開檔案選項也能用） |
| `/pet.png` | 桌寵現在的樣子（桌寵隱藏時也可以，例如只顯示在系統匣） |

在 OBS：**來源 → ＋ → 瀏覽器**，貼上網址，設定寬高（例如 360 × 400），「自訂 CSS」維持原樣即可（網頁背景本來就是透明的）。

選項加在網址後面（例如 `http://127.0.0.1:47291/?view=both&ids=claude`）：

| 選項 | 值 | 預設 |
|---|---|---|
| `view` | `bars`（用量條）、`pet`（桌寵）、`both`（兩個都要） | `bars` |
| `ids` | 只顯示這些 AI，例如 `claude,codex` | 全部 |
| `layout` | `column`（上下排）或 `row`（左右排） | `column` |
| `compact` | `1`：每個 AI 只顯示最快用完的那條 | 關 |
| `scale` | 例如 `1.5` | `1` |
| `panel` | `0`：文字後面不放深色底 | 開 |
| `text` | `dark`：深色文字（背景是淺色時） | 淺色 |
| `fps` | 桌寵畫面每秒更新幾張，1–10 | `4` |

注意：

- 只有這台電腦連得到：伺服器只聽 `127.0.0.1`，而且只回應寫著 `127.0.0.1`、`localhost` 或 `[::1]` 的請求，
  你瀏覽的網站無法透過 DNS 手法讀取；也不送跨網域（CORS）標頭，只接受 `GET`。
- 換連接埠：在設定資料夾的 `settings.json` 設定 `"usagePort"`（1024–65535），再把開關關掉重開。連接埠被占用時設定頁會提示。
- 網頁上的桌寵是每秒更新幾次的圖片，不是即時動畫；在桌寵旁邊另開視窗的對話泡泡不會出現在裡面。
