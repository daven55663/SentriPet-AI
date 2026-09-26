# SentriPet 開發日誌

新的紀錄寫在最上面。每一項工作也會開一個 GitHub issue 追蹤。

---

## 2026-09-27　跨平台：讓 SentriPet 也能在 macOS 和 Linux 上跑

追蹤 issue：[#12](https://github.com/daven55663/SentriPet-AI/issues/12)

### 為什麼

到 1.2.2 為止，SentriPet 只能在 Windows 上執行：畫面用的是 WPF（Windows 專用），
還用到 Windows Forms（螢幕、系統匣）、Win32 API（透明視窗、滑鼠穿透、全螢幕偵測）、
登錄檔（開機啟動），編譯也靠 Windows 內建的 C# 編譯器。
Claude Code、Codex 在 macOS 和 Linux 上一樣很多人用，希望桌寵也能跟過去。

### 盤點：哪些地方綁死 Windows

| 部分 | 行數 | Windows 專用的東西 | 跨平台難度 |
|---|---|---|---|
| 核心（用量模型、JSON、設定、提醒判斷、背景更新） | 約 1,300 | 顏色型別借用 WPF 的 `Color`；路徑用 `%APPDATA%`；PATH 搜尋用 `;` 和 PATHEXT | 低：換成自己的顏色型別、依作業系統決定路徑 |
| 資料來源（Claude、Codex、Copilot、Ollama、外掛） | 約 2,200 | 各程式的資料夾位置、`cmd.exe`、`taskkill` | 中：每個系統的資料位置不同，部分要實機確認 |
| 畫面（8 種造型、詳情卡、泡泡、設定頁、選單） | 約 6,700 | 全部是 WPF | 高：要換成跨平台框架重寫這一層 |
| 系統整合（系統匣、開機啟動、透明置頂、滑鼠穿透、全螢幕偵測） | 約 400 | Windows Forms、Win32、登錄檔 | 高：三個系統各寫一套 |
| 自動測試 | 約 1,600 | UI 測試用 WPF；外掛測試用 `.cmd` | 核心部分可以直接沿用 |

### 決定：用 Avalonia 重寫畫面層，核心程式碼共用

考慮過的做法：

| 做法 | 優點 | 缺點 |
|---|---|---|
| **Avalonia（.NET 10，C#）** ✅ | 核心、資料來源、300 項測試大多直接沿用；語法和 WPF 很像，造型好移植；支援透明視窗、置頂、系統匣、多螢幕 | 需要安裝 .NET 10 SDK；發佈檔比現在大 |
| Electron | 畫面用網頁技術，最好改 | 全部改寫成 JavaScript；記憶體約 200 MB（之前已決定不要） |
| Tauri | 很輕 | 全部改寫（Rust + 網頁），工具鏈最複雜 |
| .NET MAUI | 微軟官方 | 不支援 Linux |

版本：Avalonia 12.1、.NET 10（長期支援版，支援到 2028 年 11 月）。
使用者同意在開發機安裝 .NET 10 SDK（winget）與從 NuGet 下載 Avalonia 套件。

### 程式碼怎麼放

```
src/                  現在的 Windows 版（WPF，build.cmd 編譯），新版完成前繼續使用
  Core/ Providers/    ← 兩個版本共用的核心：不能用任何 Windows 專用的東西，維持 C# 5 語法
xplat/                新的跨平台版（.NET 10）
  SentriPet.Core/     直接連結 src/Core、src/Providers 的原始碼（同一份檔案，兩邊一起編譯）
  SentriPet.Tests/    核心測試的跨平台版本，在 Windows、macOS、Linux 上跑同一套檢查
  SentriPet.Desktop/  Avalonia 桌寵（第 3 階段開始）
```

### 分階段計畫

- [x] **第 1 階段：核心去 Windows 化**
  顏色改用自己的型別；新增作業系統判斷；設定與記錄檔依系統放在 `%APPDATA%`（Windows）、
  `~/Library/Application Support`（macOS）、`~/.config`（Linux）；PATH 搜尋、萬用字元路徑、
  執行外部指令、結束 codex 程序改成跨平台寫法；各資料來源加上 macOS／Linux 的資料位置。
  Windows 版照常編譯，300 多項測試全部通過。
- [ ] **第 2 階段：跨平台核心專案＋三系統 CI**
  建立 `xplat/SentriPet.Core`、`xplat/SentriPet.Tests`；GitHub CI 在 Windows、macOS、Linux
  三台機器上跑核心測試。
- [ ] **第 3 階段：Avalonia 桌寵骨架**
  透明、置頂、可拖曳到任何螢幕的視窗；系統匣圖示與右鍵選單；懸停詳情卡；
  先移植果凍桌寵造型；無頭（headless）截圖讓 CI 在三個系統上都畫得出畫面。
- [ ] **第 4 階段：移植其餘 7 種造型、設定頁、泡泡**
- [ ] **第 5 階段：各系統整合**
  開機啟動（Windows 登錄檔／macOS LaunchAgent／Linux `~/.config/autostart`）、
  通知、只允許一個執行中的程式、滑鼠穿透、全螢幕時躲起來。
- [ ] **第 6 階段：打包發佈**
  Windows zip、macOS `.app`、Linux tar.gz／AppImage，放上 GitHub Releases（發佈前先跟使用者確認）。
- [ ] **第 7 階段：切換**
  新版在 Windows 上功能追平後改成預設，WPF 版退役。

### 已知風險與待確認

- **Linux 上的 Claude**：Claude 桌面版沒有官方 Linux 版，讀不到桌面版的用量紀錄。
  需要改用 Claude Code 狀態列帶出的官方數字（#9），否則只能顯示推算值。
- **macOS 上各程式的資料位置**：Claude 桌面版（`~/Library/Application Support/Claude`）、
  Copilot CLI 快取、Codex 桌面版的位置是依慣例推測，需要有 Mac 的人實機確認。
- **Linux 透明視窗**：需要有合成器（大部分桌面環境都有）；滑鼠穿透在 X11／Wayland 上做法不同，可能延後。
- **macOS 未簽章**：沒有 Apple 開發者簽章的 `.app` 第一次開啟會被 Gatekeeper 擋，要在說明裡教使用者怎麼開。
- **沒有 Mac、Linux 實機**：開發機是 Windows，macOS／Linux 靠 CI 自動測試與無頭截圖驗證；
  實際桌面行為（拖曳、系統匣、開機啟動）要請有這些系統的人試用回報。

### 進度紀錄

- 2026-09-27：完成盤點與架構決定；安裝 .NET 10 SDK；開始第 1 階段。
- 2026-09-27：**第 1 階段完成**。
  - 新增不依賴任何畫面框架的顏色型別 `Rgba`、作業系統判斷 `Os`、`AppInfo`；WPF 的 `Palette` 移到畫面層。
  - 設定與記錄檔位置：Windows `%APPDATA%\SentriPet`、macOS `~/Library/Application Support/SentriPet`、Linux `~/.config/SentriPet`。
    外掛裡的 `%APPDATA%`、`%LOCALAPPDATA%`、`%USERPROFILE%` 在 macOS／Linux 會對應到相同用途的資料夾。
  - 萬用字元路徑、PATH 搜尋、執行外部指令（macOS／Linux 走 `/bin/sh`，.NET 10 用精確參數清單）、結束 codex 程序都改成跨平台。
  - 各資料來源加上 macOS／Linux 位置：Claude 桌面版（各系統的設定資料夾）、Codex 桌面版（`/Applications/Codex.app`）與 VS Code 擴充裡對應系統的執行檔、Copilot CLI 快取、Ollama。
  - `Lines`（桌寵說的話）與模擬資料 `MockData` 移到核心，兩個版本共用。
  - 新增顏色與作業系統相關測試：Windows 版自我測試 305 → 327 項，全部通過。
- 2026-09-27：**第 2 階段完成（本機）**。建立 `SentriPet.slnx`、`xplat/SentriPet.Core`、`xplat/SentriPet.Tests`（.NET 10），
  核心原始碼直接連結、第一次編譯就過；287 項核心測試在 .NET 10（Windows）全部通過。CI 加入 Windows／macOS／Linux 三系統核心測試。
