# SentriPet-AI

AI-Powered Real-Time Monitoring Desktop Companion

**SentriPet** 是放在 Windows 桌面上的 AI 用量監控桌寵。它會自動偵測電腦上的 AI 工具（Claude、Codex、Copilot…），
即時顯示還剩多少額度、多久後重置，不用再一直點開設定 → 用量。

- 可以拖到任何一個螢幕，位置會記住；拔掉螢幕時會自動回到主螢幕
- 開機自動啟動（右鍵選單或設定裡可以關）
- 8 種造型，依心情切換，也可以設定每天隨機換
- 用量越過門檻時提醒，額度重置時會慶祝一下
- 看影片、玩遊戲等全螢幕畫面時會自動躲起來
- 可用 JSON 外掛接上任何 AI 服務
- 輕量：原生 WPF 程式，約 70 MB 記憶體、0.5% CPU

## 安裝

需求：Windows 10 / 11（內建的 .NET Framework 4.8 即可，不需要另外安裝任何東西）。

```
git clone https://github.com/daven55663/SentriPet-AI.git
cd SentriPet-AI
install.cmd
```

`install.cmd` 會用 Windows 內建的 C# 編譯器編譯，安裝到 `%LOCALAPPDATA%\Programs\SentriPet` 並啟動，
同時設定開機自動啟動。移除請執行 `uninstall.cmd`。

## 操作

| 動作 | 效果 |
|---|---|
| 拖曳 | 移動（靠近螢幕邊緣會吸附） |
| 點一下 | 桌寵會跳起來回話 |
| 滑鼠停在上面 | 顯示詳細用量、重置時間、資料來源 |
| 右鍵 | 選單：換造型、今天心情、大小、透明度、移到螢幕…… |
| 雙擊 | 開啟設定 |
| 系統匣圖示 | 左鍵顯示／隱藏，右鍵選單。圖示裡的果凍高度 = 最低的剩餘額度 |

## 造型

| 造型 | 心情 | 特色 |
|---|---|---|
| 果凍桌寵 | 元氣滿滿 | 肚子裡的果凍 = 剩餘額度；眼睛跟著滑鼠，額度少會冒汗，用完會睡覺 |
| 極簡玻璃 | 平靜專注 | 毛玻璃卡片＋進度圓環 |
| 像素勇者 | 想打電動 | RPG 狀態列，HP/MP 就是額度，AI 工作時顯示「戰鬥中」 |
| 駭客終端 | 進入心流 | 綠色磷光 CRT，點標題列可以換磷光顏色 |
| 賽車儀表 | 全速前進 | 油表指針、七段顯示器倒數，AI 工作時遠光燈會亮 |
| 魔法藥水 | 有點夢幻 | 每個額度一瓶藥水，5 小時是圓底燒瓶、每週是長瓶 |
| 霓虹夜城 | 深夜模式 | 賽博龐克霓虹燈管，偶爾故障閃爍 |
| 手寫便利貼 | 慢慢來 | 手寫字＋鉛筆斜線進度條 |

## 資料從哪裡來（全部在本機，不讀、不傳任何登入憑證）

| AI | 來源 | 更新頻率 |
|---|---|---|
| Claude | Claude 桌面版自己記錄的 `plan-usage-history.json`（和「設定 → 用量」同一份數字），加上 Claude Code 本機對話紀錄裡的 token 數（只讀數字，不讀內容） | 桌面版約每 15 分鐘寫一次；兩次之間用 Claude Code 的 token 用量即時推算（數字前標「≈」，比例會用你自己的歷史紀錄自動校準，實測誤差約 1 個百分點）。重置時間檔案裡沒有，用對話紀錄與歷史推算；也可以在設定裡填「週四 23:00」校正每週重置時間。在 claude.ai 網頁或手機上聊天的用量推算不到，要等桌面版下次更新 |
| Codex | 官方 `codex app-server` 的 `account/rateLimits/read`，加上 `~/.codex/sessions` 對話紀錄裡的 rate_limits | 預設每 5 分鐘即時查詢一次；用 Codex 時本機紀錄會即時更新 |
| Copilot | Copilot CLI 的額度快取 `%LOCALAPPDATA%\copilot\copilot-user-cache.json` | 用 Copilot CLI 時才會更新 |
| Ollama | 本機 `http://127.0.0.1:11434`（沒有額度，只顯示載入中的模型） | 30 秒 |
| 其他 | Gemini CLI、Cursor、Windsurf、LM Studio… 會被偵測到並列在設定裡；可以用外掛接上用量 | — |

## 接上其他 AI（外掛）

在 `%APPDATA%\SentriPet\providers` 放一個 JSON 設定檔就能新增一隻桌寵，
支援「執行一支程式印出 JSON」、「呼叫 HTTP API」、「讀取 JSON 檔」三種方式。
說明與範例：[`examples/providers/README.md`](examples/providers/README.md)（設定 → AI 服務 → 開啟外掛資料夾，會自動複製範例過去）。

## 開發

用的是 Windows 內建的 C# 5 編譯器（.NET Framework 4.8 + WPF），不需要 Visual Studio 或 .NET SDK。

```
build.cmd      編譯到 bin\SentriPet.exe
install.cmd    編譯並安裝到 %LOCALAPPDATA%\Programs\SentriPet，然後重新啟動
uninstall.cmd  關閉程式、移除開機啟動、刪除安裝資料夾
```

- 新增造型：在 `src\Themes` 新增一個繼承 `Theme` 的類別，並加到 `ThemeCatalog.All`。
- 新增內建 AI：在 `src\Providers` 新增一個繼承 `Provider` 的類別，並加到 `ProviderRegistry`。
- `SentriPet.exe --dev`：獨立設定檔、不碰開機啟動。
- `--snapshot 資料夾 --mock`：把所有造型畫成 PNG；`--snapshot-ui 資料夾`：畫出選單、設定頁與詳情卡。
- `--probe 檔案`：輸出偵測與用量報告；`--selftest 檔案`：檢查詳情卡在各種螢幕位置的擺放。
- `--dev --show-detail claude`：強制顯示某隻的詳情卡 45 秒。

## 檔案位置

- 程式：`%LOCALAPPDATA%\Programs\SentriPet\`
- 設定與記錄檔：`%APPDATA%\SentriPet\`（`settings.json`、`logs\app.log`）
- 外掛：`%APPDATA%\SentriPet\providers\`
- 開機啟動：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 裡的 `SentriPet`

1.0 版叫「AI 用量桌寵」，資料在 `%APPDATA%\AIUsagePet`；升級到 SentriPet 時會自動把設定搬過來。

## 授權

[MIT](LICENSE) © 2026 歐育典
