# SentriPet

[English](README.md) | **繁體中文**

[![CI](https://github.com/daven55663/SentriPet-AI/actions/workflows/ci.yml/badge.svg)](https://github.com/daven55663/SentriPet-AI/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/daven55663/SentriPet-AI)](https://github.com/daven55663/SentriPet-AI/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**SentriPet** 是放在桌面上的 AI 用量監控桌寵。它會自動偵測電腦上的 AI 工具（Claude、Codex、Copilot…），
即時顯示還剩多少額度、多久後重置，不用再一直點開「設定 → 用量」；每週額度快重置卻還沒用完時，還會催你把它用掉。
支援 **Windows、macOS、Linux**。

<p align="center"><img src="docs/images/demo.zh-TW.gif" alt="示範動畫：果凍桌寵顯示 Claude、Codex、Copilot 的剩餘額度；額度快重置還沒用完時著急提醒；接著展示另外 7 種造型" width="720"></p>

## 特色

- **一眼看到剩多少**：大數字是最短的額度（例如 5 小時），下面的小條是每週額度，還有重置倒數
- **8 種造型**，依心情切換，也可以設定每天隨機換
- **會提醒**：用量越過門檻時提醒、額度重置時慶祝；每週額度快重置卻還剩很多時，桌寵會著急地催你用掉
- **多國語言**：繁體中文、简体中文、English、日本語、한국어，預設跟隨系統語言
- 可以拖到任何一個螢幕；開機自動啟動；看影片、玩遊戲的全螢幕畫面時自動躲起來
- 可用 JSON 外掛接上任何 AI 服務
- **只在本機讀取**用量資料，不讀取、也不傳送任何登入憑證
- 輕量：約 110 MB 記憶體，整體 CPU 不到 1%

## 畫面一覽

**8 種造型**（在桌寵上按右鍵 → 換造型）

<p align="center"><img src="docs/images/themes.png" alt="8 種造型：果凍桌寵、極簡玻璃、像素勇者、駭客終端、賽車儀表、魔法藥水、霓虹夜城、手寫便利貼"></p>

**滑鼠停在上面**：每個額度剩多少、什麼時候重置、資料來源與更新時間。
**額度快過期卻沒用完**：桌寵會著急（冒汗、鬧鐘、進度條閃爍），卡片上也會提醒。

<p align="center">
  <img src="docs/images/detail.png" alt="懸停詳情卡" width="400">
  <img src="docs/images/use-it.png" alt="額度快過期時著急的果凍" width="420">
</p>

**右鍵選單與設定頁**（雙擊桌寵開啟設定）

<p align="center">
  <img src="docs/images/menu.png" alt="右鍵選單與換造型" width="470">
  <img src="docs/images/settings.png" alt="設定頁" width="350">
</p>

**多國語言**（右鍵選單 → 語言 · Language）

<p align="center"><img src="docs/images/languages.png" alt="英文、日文、韓文介面"></p>

## 安裝手冊

### 用套件管理器安裝（推薦）

**Windows**（[Scoop](https://scoop.sh)）：裝好後桌寵會直接出現，之後用 `scoop update sentripet` 更新。

```
scoop bucket add sentripet https://github.com/daven55663/SentriPet-AI
scoop install sentripet
```

**macOS**（[Homebrew](https://brew.sh)）：裝到「應用程式」，之後用 `brew upgrade --cask sentripet` 更新。

```
brew tap daven55663/sentripet https://github.com/daven55663/SentriPet-AI
brew install --cask sentripet
```

Homebrew 安裝時會移除 macOS 的隔離標記，所以不會出現「無法確認開發者」的阻擋（這個 App 沒有 Apple 開發者簽章）。

### 下載安裝檔

到 **[Releases](https://github.com/daven55663/SentriPet-AI/releases/latest)** 下載最新版：

| 系統 | 檔案 | 需求 |
|---|---|---|
| Windows | `SentriPet-<版本>-win-x64.zip` | Windows 10／11 |
| macOS Apple 晶片（M1～M4） | `SentriPet-<版本>-osx-arm64.zip` | macOS 12 以上 |
| macOS Intel | `SentriPet-<版本>-osx-x64.zip` | macOS 12 以上 |
| Linux | `SentriPet-<版本>-linux-x64.tar.gz` | x64、X11 或 XWayland 桌面 |

每個檔案都已經包含執行需要的一切，不需要另外安裝 .NET。
（1.x 版的 Windows 專用版本（WPF）仍可在 [v1.4.0](https://github.com/daven55663/SentriPet-AI/releases/tag/v1.4.0) 下載，但不再更新。）

### Windows

1. 下載 `SentriPet-<版本>-win-x64.zip`，解壓縮到一個固定的資料夾（例如 `%LOCALAPPDATA%\Programs\SentriPet`）。
2. 執行 `SentriPet.exe`。如果出現「Windows 已保護您的電腦」，按「其他資訊」→「仍要執行」（程式沒有數位簽章）。
3. 桌寵出現在螢幕右下角，系統匣也會多一個果凍圖示；開始選單會多一個 SentriPet，之後開機會自動啟動（右鍵選單可以關）。

**從原始碼安裝**（需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)）：

```
git clone https://github.com/daven55663/SentriPet-AI.git
cd SentriPet-AI
install.cmd
```

`install.cmd` 會編譯、安裝到 `%LOCALAPPDATA%\Programs\SentriPet` 並啟動；移除請執行 `uninstall.cmd`。
從 1.x 版升級時設定會保留，程式直接換成新版。

### macOS

1. 下載對應晶片的 zip（Apple 晶片選 `osx-arm64`，Intel 選 `osx-x64`），解壓縮後把 `SentriPet.app` 拖到「應用程式」。
2. **第一次開啟**：這個 App 沒有 Apple 開發者簽章，macOS 會擋下來。在終端機執行下面這行就能打開：
   ```
   xattr -dr com.apple.quarantine /Applications/SentriPet.app
   ```
   或是先開一次（會被擋），再到「系統設定 → 隱私權與安全性」往下捲，按 SentriPet 旁邊的按鈕允許打開
   （macOS 14 以前也可以在 `SentriPet.app` 上按右鍵 →「打開」）。用 Homebrew 安裝就不需要這一步。
3. 桌寵出現在桌面上，選單列會有果凍圖示（不會出現在 Dock）。第一次跳通知時，macOS 可能會問要不要允許通知。
4. 開機自動啟動用的是 `~/Library/LaunchAgents/com.sentripet.app.plist`，右鍵選單可以關。

### Linux

```
tar xzf SentriPet-<版本>-linux-x64.tar.gz
cd SentriPet-<版本>-linux-x64
./install.sh
```

`install.sh` 會裝到 `~/.local/share/sentripet`、加進應用程式選單並啟動（不需要 root）。

- 需要有合成器的桌面才會是透明背景（GNOME、KDE、Xfce 等大多都有）。
- GNOME 要顯示系統匣圖示，需要「AppIndicator and KStatusNotifierItem Support」擴充；沒有系統匣時用桌寵的右鍵選單即可。
- 通知用 `notify-send`（Debian／Ubuntu：`sudo apt install libnotify-bin`）。
- Linux 沒有 Claude 桌面版：要看 Claude 的用量，請在 設定 → AI 服務 開啟「連接 Claude Code 狀態列」。

### 更新與移除

- **更新**：Scoop 用 `scoop update sentripet`、Homebrew 用 `brew upgrade --cask sentripet`（先在右鍵選單按「結束」）；
  自己下載的版本，下載新版覆蓋舊的檔案即可（Linux 重新執行 `install.sh`）。設定都會保留。
- **移除**：右鍵選單取消「開機自動啟動」→「結束」，再執行 `scoop uninstall sentripet`／`brew uninstall --cask sentripet`，
  或刪掉程式與設定資料夾（位置見最下面的「檔案位置」）。從原始碼安裝的 Windows 版執行 `uninstall.cmd` 即可。

## 使用方式

### 第一次開啟

SentriPet 會自動找出電腦上的 AI 工具，找到的每個 AI 就是一隻桌寵，並打聲招呼。要讀到用量，需要：

- **Claude**：開著 Claude 桌面版（它每 15 分鐘記錄一次用量；有用 Claude Code 時會在兩次之間即時推算），
  或開啟 設定 → AI 服務 →「連接 Claude Code 狀態列」，直接拿 Claude Code 提供的官方數字（Linux 只能用這個方式，見下面的說明）
- **Codex**：裝了 Codex 桌面版、VS Code 擴充或 `codex` 指令，並用 ChatGPT 帳號登入
- **Copilot**：用過一次 Copilot CLI（它會留下額度快取）

沒有偵測到的 AI 可以在 設定 → AI 服務 看到原因，或寫一個[外掛](#接上其他-ai外掛)接上。

### 操作

| 動作 | 效果 |
|---|---|
| 拖曳 | 移動（靠近螢幕邊緣會吸附），位置會記住 |
| 點一下 | 桌寵會回話 |
| 滑鼠停在上面 | 顯示詳細用量、重置時間、資料來源 |
| 右鍵 | 選單：換造型（每種造型旁邊標著心情，也可以交給命運隨機換）、語言、大小、透明度、移到螢幕、滑鼠穿透…… |
| 雙擊 | 開啟設定 |
| 系統匣／選單列圖示 | 左鍵顯示／隱藏，右鍵選單（滑鼠穿透時從這裡關掉）。圖示裡的果凍高度 = 最低的剩餘額度，AI 工作中會多一個藍點 |

### 設定頁

| 分區 | 可以做什麼 |
|---|---|
| 造型 | 看 8 種造型的即時預覽並切換；每天隨機換一個 |
| 外觀 | 大小、不透明度、永遠在最上層、全螢幕時躲起來、滑鼠穿透、會說話、省電模式 |
| AI 服務 | 每個 AI 偵測到什麼、目前讀到的數字；開關個別 AI；Codex 即時查詢頻率；Claude 每週重置時間、Claude Code 狀態列、即時推算 |
| 提醒 | 額度提醒通知、「催我用完週額度」、提醒與緊急門檻 |
| 一般 | 語言、開機自動啟動、開啟設定／記錄檔／程式資料夾 |

### 看懂畫面

- **大數字**固定顯示最短的額度（Claude、Codex 是 5 小時），不會在 5 小時和每週之間跳來跳去；每週額度看下面的小條。
  桌寵的表情跟著最吃緊的那個額度：額度多時開心、少時冒汗、用完就睡覺。
- 下面那行是重置倒數，會標明是哪個額度（`5h 2時11分後重置`）；5 小時還沒開始計時時，改顯示每週的重置時間。
- 數字前面有 **≈**（Windows 上顯示 **~**）表示是推算值：Claude 桌面版兩次紀錄之間，用 Claude Code 的 token 用量即時推算。
- 資料舊了（例如 Copilot CLI 很久沒用）會標「舊資料」。

### 「快用掉」提醒

每週／每月額度快重置、卻還剩不少時，桌寵會著急地催你把它用掉，不然就浪費了。畫面上不多加文字：
快過期的那條進度條會閃，桌寵的表情會變；詳細說明在滑鼠停上去的卡片裡。

| 距離重置 | 還剩 | 桌寵的反應 |
|---|---|---|
| 2 天內 | 30% 以上 | 進度條慢慢閃，眉毛上揚、偷瞄旁邊的鬧鐘，約每小時說一次 |
| 最後一天 | 10% 以上 | 進度條閃得更快，張大嘴冒汗，鬧鐘一響就嚇一跳，約每 25 分鐘說一次 |
| 最後 6 小時 | 5% 以上 | 進度條快閃，慌張發抖、鬧鐘狂響，約每 12 分鐘說一次 |

每升一級會跳一次通知（重開機也不會重複跳）。AI 正在工作時不會打擾你（桌寵會開心地看你用）；
5 小時額度用完時也先不催（反正用不了）。右鍵選單「催我用完週額度」或 設定 → 提醒 可以關掉。

### 語言

支援 **繁體中文、简体中文、English、日本語、한국어**。預設「自動」＝跟隨系統的顯示語言
（繁中地區用繁體、中國／新加坡用簡體、日文、韓文，其他語言用英文）。

- 切換：右鍵選單 → **語言 · Language**，或 設定 → 一般 → 語言。立即生效，不用重開。
- 選單、設定頁、詳情卡、桌寵說的話、造型名稱、通知都會換成該語言，字型也會跟著換。
- 設定 → Claude 每週重置時間 可以用任何一種語言填：`週四 23:00`、`周四 23:00`、`Thu 23:00`、`木曜日 23:00`、`목요일 23:00`。

想新增或修正翻譯：程式裡的繁體中文就是原文，翻譯放在 `src/Lang/<語言>.json`（`"原文": "翻譯"`，
`{0}`、`{name}` 這類記號要保留）。自動測試會檢查每一句都有翻譯、記號一致。

## 常見問題

**沒有偵測到 Claude／Claude 顯示「找不到用量紀錄」**
開啟 Claude 桌面版就會開始記錄。Linux 沒有 Claude 桌面版：請開啟「連接 Claude Code 狀態列」（見下一題）。

**「連接 Claude Code 狀態列」是什麼？**
Claude Code 每次回覆後，會把官方的用量與重置時間交給它的「狀態列指令」。開啟這個選項後，SentriPet 會在
`~/.claude/settings.json` 把自己設成狀態列指令（修改前會備份成 `settings.json.sentripet-backup`），記下官方數字：

- Linux 上靠它才讀得到 Claude 用量；Windows／macOS 上重置時間會變成官方的（不再是推算值）。
- 你原本的狀態列照常顯示（SentriPet 會代為執行原本的指令）；沒有的話，狀態列會顯示剩餘額度。關掉選項就會原封不動還原。
- 數字只在使用 Claude Code 時更新；只在 claude.ai 網頁、手機或桌面版聊天的用量，要等下次用 Claude Code 才會反映出來。
- 需要 Pro 或 Max 訂閱（用 API 金鑰時 Claude Code 不會提供這些數字）。

**Claude 的重置時間和官網差一點**
Claude 桌面版的紀錄裡沒有重置時間，是用歷史紀錄推算的。開啟「連接 Claude Code 狀態列」就會用官方的時間；
或到 設定 → AI 服務 → Claude 每週重置時間，照 Claude「設定 → 用量」頁面寫的時間填一次（例如 `週四 23:00`）。

**在 claude.ai 網頁或手機上聊天，數字沒有馬上變**
那部分只能等 Claude 桌面版下一次更新紀錄（約 15 分鐘）。

**桌寵擋到要點的東西**
右鍵選單開「滑鼠穿透」，點擊會直接穿過桌寵；要關掉請在系統匣圖示按右鍵。也可以調小一點或移到別的螢幕。

**全螢幕時桌寵不見了**
這是「全螢幕時自動躲起來」（Windows、Linux），離開全螢幕就會回來；不想要可以在 設定 → 外觀 關掉。
macOS 的全螢幕 App 會在自己的桌面空間裡，桌寵本來就不會出現在那裡。

**不小心藏起來了**
點系統匣圖示，或再開一次程式（已經在執行時會把桌寵叫回來）。

**macOS 說「無法打開，因為無法確認開發者」**
見上面 macOS 安裝第 2 步，或改用 Homebrew 安裝。

**Windows 出現「Windows 已保護您的電腦」**
程式沒有數位簽章，按「其他資訊」→「仍要執行」。用 Scoop 安裝不會出現這個畫面。

## 資料從哪裡來（全部在本機，不讀、不傳任何登入憑證）

| AI | 來源 | 更新頻率 |
|---|---|---|
| Claude | Claude 桌面版自己記錄的 `plan-usage-history.json`（和「設定 → 用量」同一份數字），加上 Claude Code 本機對話紀錄裡的 token 數（只讀數字，不讀內容）；開啟「連接 Claude Code 狀態列」時，另外使用 Claude Code 交給狀態列的官方用量與重置時間 | 桌面版約每 15 分鐘寫一次；兩次之間用 Claude Code 的 token 用量即時推算（比例會用你自己的歷史紀錄自動校準，實測誤差約 1 個百分點）；狀態列在 Claude Code 每次回覆後更新 |
| Codex | 官方 `codex app-server` 的 `account/rateLimits/read`，加上 `~/.codex/sessions` 對話紀錄裡的 rate_limits | 預設每 5 分鐘即時查詢一次；用 Codex 時本機紀錄會即時更新 |
| Copilot | Copilot CLI 的額度快取 | 用 Copilot CLI 時才會更新 |
| Ollama | 本機 `http://127.0.0.1:11434`（沒有額度，只顯示載入中的模型） | 30 秒 |
| 其他 | Gemini CLI、Cursor、Windsurf、LM Studio… 會被偵測到並列在設定裡；可以用外掛接上用量 | — |

## 接上其他 AI（外掛）

在設定資料夾的 `providers` 裡放一個 JSON 設定檔就能新增一隻桌寵，
支援「執行一支程式印出 JSON」、「呼叫 HTTP API」、「讀取 JSON 檔」三種方式。
說明與範例：[`examples/providers/README.md`](examples/providers/README.md)（設定 → AI 服務 → 開啟外掛資料夾，會自動複製範例過去）。

## 開發

SentriPet 用 C#（.NET 10）與 [Avalonia](https://avaloniaui.net/) 寫成，一份程式碼在 Windows、macOS、Linux 上執行。
2.0 以前的 Windows 專用版本（WPF）在 git 標籤 `v1.4.0`。

```
src/Core、src/Providers   用量模型、各 AI 的資料來源、提醒、翻譯（不含任何畫面程式）
src/Lang                   翻譯檔
src/Tests                  核心測試
xplat/SentriPet.Desktop    桌寵本體：8 種造型、詳情卡、設定頁、系統匣、各系統整合（Integration.cs）
xplat/SentriPet.Tests      核心測試的執行程式
```

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)：

```
dotnet build SentriPet.slnx -c Release
dotnet run -c Release --project xplat/SentriPet.Desktop            執行
dotnet run -c Release --project xplat/SentriPet.Desktop -- --dev   獨立的設定檔，不碰開機啟動
xplat/package.sh win-x64                                           打包（osx-arm64、osx-x64、linux-x64 也可以）
```

- 新增造型：在 `xplat/SentriPet.Desktop/Themes` 新增一個繼承 `Theme` 的類別，並加到 `ThemeCatalog.All`。
- 新增內建 AI：在 `src/Providers` 新增一個繼承 `Provider` 的類別，並加到 `ProviderRegistry`。
- `--lang en`：指定語言（`zh-TW`、`zh-CN`、`en`、`ja`、`ko`）；`--dev --show-detail claude`：強制顯示某隻的詳情卡 45 秒。
- `--snapshot 資料夾`：用範例資料把所有造型、詳情卡、泡泡、設定頁畫成 PNG（不需要螢幕）。
- `--demo 檔案.gif --lang zh-TW`：產生 README 上的示範動畫（`docs/images/demo*.gif`）。
- `--probe 檔案`：輸出偵測與用量報告。

### 自動測試、CI 與發佈

| 測試 | 內容 | 數量 |
|---|---|---|
| `xplat/SentriPet.Tests` | 核心、每個資料來源（範例檔與本機假伺服器，不碰真實資料）、提醒、翻譯、Claude Code 狀態列 | 396 項 |
| `SentriPet --selftest 報告.txt` | 8 種造型、果凍的表情、詳情卡與擺放位置、泡泡、選單、設定頁、語言、系統匣圖示、開機啟動、通知、單一執行、示範動畫 | 86 項 |
| `SentriPet --dev --smoke-test 報告.txt` | 真的開啟程式 15 秒：視窗有出來、在螢幕內、第一次在右下角、有在動畫、系統匣、沒有錯誤 | 7 項 |

每次推送，[CI](https://github.com/daven55663/SentriPet-AI/actions/workflows/ci.yml) 會在 Windows、macOS、Linux 上編譯、跑全部測試
（Linux 用虛擬螢幕真的開一次程式）、畫出所有造型的截圖，並打包各系統的安裝檔（在該次執行的 Artifacts）。
推送 `v*` 標籤時，[Release](.github/workflows/release.yml) 流程會在各系統打包、讓每個安裝檔先跑一次自我測試，全部通過才發佈；
發佈後自動更新 Scoop、Homebrew、winget 的安裝設定（`bucket/`、`Casks/`、`packaging/winget/`），
再用 Scoop 和 Homebrew 實際安裝一次、跑自我測試（[Package managers](.github/workflows/packages.yml)）。
開發紀錄見 [docs/DEVLOG.md](docs/DEVLOG.md)。

## 檔案位置

| | Windows | macOS | Linux |
|---|---|---|---|
| 程式 | `%LOCALAPPDATA%\Programs\SentriPet\` | `/Applications/SentriPet.app` | `~/.local/share/sentripet/` |
| 設定與記錄檔 | `%APPDATA%\SentriPet\` | `~/Library/Application Support/SentriPet/` | `~/.config/SentriPet/` |
| 外掛 | `%APPDATA%\SentriPet\providers\` | `~/Library/Application Support/SentriPet/providers/` | `~/.config/SentriPet/providers/` |
| 開機啟動 | 登錄檔 `HKCU\…\Run` 的 `SentriPet` | `~/Library/LaunchAgents/com.sentripet.app.plist` | `~/.config/autostart/sentripet.desktop` |
| 選單捷徑 | 開始選單的 `SentriPet` | — | `~/.local/share/applications/sentripet.desktop` |

設定檔是 `settings.json`，記錄檔是 `logs/app.log`。1.0 版叫「AI 用量桌寵」，資料在 `%APPDATA%\AIUsagePet`；升級時會自動把設定搬過來。

## 授權

[MIT](LICENSE) © 2026 歐育典
