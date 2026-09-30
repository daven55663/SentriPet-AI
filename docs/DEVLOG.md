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
- [x] **第 2 階段：跨平台核心專案＋三系統 CI**
  建立 `xplat/SentriPet.Core`、`xplat/SentriPet.Tests`；GitHub CI 在 Windows、macOS、Linux
  三台機器上跑核心測試。
- [x] **第 3 階段：Avalonia 桌寵骨架**
  透明、置頂、可拖曳到任何螢幕的視窗；系統匣圖示與右鍵選單；懸停詳情卡；
  先移植果凍桌寵造型；無頭（headless）截圖讓 CI 在三個系統上都畫得出畫面。
- [x] **第 4 階段：移植其餘 7 種造型、設定頁、泡泡**
- [x] **第 5 階段：各系統整合**
  開機啟動（Windows 登錄檔／macOS LaunchAgent／Linux `~/.config/autostart`）、
  通知、只允許一個執行中的程式、滑鼠穿透、全螢幕時躲起來。
- [x] **第 6 階段：打包發佈**（[v1.4.0](https://github.com/daven55663/SentriPet-AI/releases/tag/v1.4.0)）
  Windows zip、macOS `.app`、Linux tar.gz／AppImage，放上 GitHub Releases（發佈前先跟使用者確認）。
- [x] **第 7 階段：切換**（2.0.0）
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
- 2026-09-27：**第 2 階段完成**。CI 結果：核心測試 macOS 287/287、Linux 287/287、Windows 287/287，
  整個資料層（Claude 推算、Codex 協定、Copilot、Ollama、外掛）在三個系統上都確認正確。
- 2026-09-27：**第 3 階段進行中：Avalonia 桌寵骨架**（`xplat/SentriPet.Desktop`，執行檔名 `SentriPet`）。
  - 查了 Avalonia 12 的重大變更：`SystemDecorations` 改名 `WindowDecorations`；12.0 預覽版的「透明視窗變黑」問題
    只發生在開啟 `ExtendClientAreaToDecorationsHint` 時，而且已修好，桌寵不用那個設定；手動指定 Skia 時要加 `UseHarfBuzz()`。
  - 完成：透明、無邊框、置頂、不出現在工作列的視窗；拖曳移動、靠近螢幕邊緣吸附、記住固定的角落（跨螢幕）；
    點一下互動、右鍵選單（立即更新、大小、移到螢幕、置頂、會說話、快用掉提醒、藏起來、結束）；
    系統匣圖示（顯示／隱藏、立即更新、結束）；用量提醒與「快用掉」提醒沿用核心的判斷。
  - **果凍桌寵造型移植完成**：WPF 與 Avalonia 的 API 很接近，用腳本做機械式轉換（`Visibility` → `IsVisible`、
    變形中心改用 `RenderTransformOrigin` 等）後一次編譯成功；無頭截圖和 WPF 版幾乎一模一樣（著急表情、鬧鐘、閃爍、睡覺、泡泡）。
  - 在 Windows 實際開啟：透明背景、位置、真實用量都正常。
  - `--snapshot 資料夾`：不需要螢幕的無頭截圖；CI 在 Windows／macOS／Linux 都畫一次（Linux 安裝 Noto CJK 字型）。
  - CI 結果：Avalonia 桌寵在 macOS、Linux、Windows 都編譯成功，無頭截圖三個系統都正確（macOS 用蘋方體、Linux 用 Noto CJK）。
- 2026-09-27：**懸停詳情卡移植完成**。卡片放在桌寵外側、箭頭指著被懸停的那隻、蓋在桌寵上面；
  Windows 用系統的全域游標判斷懸停（和 WPF 版一樣，也讓眼睛能跟著整個螢幕上的滑鼠），
  macOS／Linux 用桌寵與卡片視窗自己的滑鼠事件。`--show-detail <id>` 可強制打開卡片測試；CI 截圖也包含詳情卡。
- 2026-09-27：**多國語言（1.3.0）**：繁體中文、简体中文、English、日本語、한국어，兩個版本（WPF、Avalonia）共用。
  - 做法：程式裡的繁中原文就是鍵，`L.T` 翻譯、`L.F` 填入數值（原本用字串串接的句子全部改成完整句型），
    翻譯檔 `src/Lang/*.json` 內嵌在程式裡；缺翻譯時顯示原文。共 359 句。
  - 預設跟隨系統語言；右鍵選單「語言 · Language」與設定頁可以即時切換（重建造型、選單樣式與字型，重新讀取各 AI 的資料）。
  - 字型跟著語言換：Windows 用微軟正黑體／微軟雅黑／Yu Gothic UI／Malgun Gothic／Segoe UI；
    Avalonia 另外列出 macOS（蘋方、Hiragino、Apple SD Gothic Neo）與 Linux（Noto Sans CJK）的字型。
  - 每週重置時間的輸入也看得懂日文（`木曜日`）與韓文（`목요일`）。
  - 新增測試：每一句在每種語言都有翻譯、記號一致、系統語言對應、切換後桌寵說的話與 8 種造型的畫面在英文時沒有中文、
    各語言字型；WPF 自我測試 327 → 402 項，核心測試 287 → 347 項。截圖檢查後把日文、韓文桌寵名牌上的重置時間改短，避免被截斷。
- 2026-09-27：**韓文斷行修正**：CI 截圖確認三個系統的字型都正確（macOS 蘋方／Hiragino／Apple SD Gothic Neo、Linux Noto Sans CJK），
  但 WPF 與 Avalonia 都把韓文當成中文、在任兩個音節之間斷行（「초기화」被拆成「초／기화」）。
  改成在韓文音節與相鄰的非空白字元之間加上看不見的 U+2060（WORD JOINER），只在空格處斷行；
  數字加單位（「1일5시간」）、英文名字加助詞（「Claude의」）也連在一起。新增測試確認韓文畫面上每段文字都處理過。
- 2026-09-27：**第 4 階段進行中：其餘 7 種造型移植完成**（極簡玻璃、像素勇者、駭客終端、賽車儀表、魔法藥水、霓虹夜城、手寫便利貼）。
  - 用轉換腳本處理機械式差異，再手動修 37 個編譯錯誤：放射漸層改用相對座標、WPF 的旋轉／縮放以左上角為中心而 Avalonia 以元件中心為預設、
    `LayoutTransform` 改用 `LayoutTransformControl`、像素圖改用 `WriteableBitmap`、終端機的掃描線改成自己畫、陰影參數改名。
  - 和 WPF 版逐張比對截圖：8 種造型都一致；手寫便利貼的毛筆字在 Skia 下稍粗、行距稍大。
  - 沒有對話框的造型改用浮動泡泡（和 WPF 版一樣放在桌寵外側、尾巴指著說話的那隻，拖曳時跟著走）。
  - 右鍵選單加上「換造型」「今天心情如何？」。
  - Windows 上的 Avalonia 版：文字改成灰階反鋸齒（透明視窗不適合次像素）；微軟正黑體的「≈」經 Skia 在小字時會糊成一塊，
    Windows 上改用「~」表示推算值（macOS／Linux 照樣用「≈」）。
- 2026-09-27：**第 4 階段完成：設定頁**。Avalonia 版的設定頁和 WPF 版同樣的分區（造型（附即時預覽）、外觀、AI 服務、提醒、一般＋語言），
  用 Fluent 的開關、滑桿、按鈕；雙擊桌寵或選單「設定…」打開，改了立刻生效、稍後自動存檔。
- 2026-09-27：**第 5 階段：各系統整合**（`xplat/SentriPet.Desktop/Integration.cs`）。
  - 開機啟動：Windows 登錄檔 Run、macOS `~/Library/LaunchAgents/com.sentripet.app.plist`（`.app` 用 `open` 開）、
    Linux `~/.config/autostart/sentripet.desktop`；開發用設定檔（`--dev`）一律不動系統。
  - 通知：Windows 用 PowerShell 顯示 toast（不必另外裝執行環境）、macOS 用 `osascript`（文字當參數傳，不拼進指令）、Linux 用 `notify-send`。
  - 滑鼠穿透：Windows `WS_EX_TRANSPARENT`、macOS `NSWindow setIgnoresMouseEvents:`、X11（含 XWayland）用空的輸入區域；
    系統匣選單也有開關（穿透時唯一關掉的地方）。
  - 全螢幕時躲起來：Windows 沿用 WPF 版的判斷；macOS／Linux 沒有可靠的方法，設定頁不顯示這一項。
  - 只允許一個在執行：具名 Mutex，第二個開的會透過本機 named pipe 請第一個跳出來再自己結束（實測通過）。
  - 新增 `--selftest`：Avalonia 版自己的檢查（8 種造型、詳情卡、泡泡、設定頁、英文沒有中文、韓文斷行、開機啟動檔案格式、
    通知指令、單一執行），37 項；CI 在三個系統上都跑。
- 2026-09-27：**第 6 階段：打包**（`xplat/package.sh`）。不需要另外安裝 .NET 的單一執行檔：Windows zip、
  macOS `.app`（Apple 晶片與 Intel，ad hoc 簽章、不出現在 Dock）、Linux tar.gz（附 `install.sh`）。
  CI 在各自的系統上打包，再讓打包好的程式跑一次自己的 `--selftest`，通過才上傳成 Artifact（尚未公開發佈）。
  大小約 40 MB（macOS、Linux）。
  - **macOS 卡住的問題**：手寫便利貼要的楷體類字型（Kaiti、BiauKai、Klee、Nanum…）在 macOS 上是「用到才下載」的字型，
    無頭環境下文字排版會一直等，整個截圖步驟卡到被取消。改成 Mac 只用內建字型；自我測試加上看門狗，卡住時會報出卡在哪裡。
- 2026-09-27：**記憶體**：Avalonia 預設用 GPU 繪圖，工作集約 252 MB；改成和 WPF 版一樣用 CPU 繪圖（`--gpu` 可改回），
  Windows 上實測降到 **108 MB**、CPU 也少一半（0.56% → 0.26%）。GC 設定（非並行、ConserveMemory）沒有幫助。
- 2026-09-27：Linux 上桌寵的眼睛改用 X11 的全域游標位置（整個螢幕都跟著看）；macOS 仍只在滑鼠經過時跟著看
  （座標換算要在實機確認）。版本升為 **1.4.0**。
- 2026-09-27：**發佈 v1.4.0**（使用者同意）。新增 Release 流程：推送 `v*` 標籤 → 先建草稿 → 各系統打包並讓安裝檔跑自我測試
  → 全部通過才公開。這次 5 個檔案（WPF、win-x64、osx-arm64、osx-x64、linux-x64）全部通過。
  README 改版：安裝手冊（各系統步驟、第一次開啟、更新與移除）、使用方式、常見問題，並附上用範例資料畫的截圖（`docs/images`）。
- 2026-09-27：**第 7 階段試用開始**：這台電腦上的 Windows 版換成跨平台版（v1.4.0 的 win-x64 安裝檔，同一個安裝位置、
  同一份設定與開機啟動；WPF 版留在 `SentriPet-wpf.exe`，要換回來執行 `install.cmd`）。啟動正常、記錄檔沒有錯誤。
  - 發現安裝檔裡多了原生函式庫的除錯符號（`libSkiaSharp.pdb` 84 MB、`libHarfBuzzSharp.pdb` 20 MB），Windows 安裝檔因此特別大；
    打包時改成刪掉。
  - 實際使用的記憶體 149 MB，比開發時量的 108 MB 多：單一執行檔壓縮後，程式碼要解壓到記憶體裡；
    zip 本來就會壓縮，所以拿掉單一執行檔壓縮。改好後 Windows 安裝檔 66 → 41 MB，實際使用 **114 MB**、CPU 約 0.3%（WPF 版約 90 MB）。
    經使用者同意，v1.4.0 發佈頁上的 `SentriPet-1.4.0-win-x64.zip` 已換成這個新檔（程式碼相同，只差打包；上傳後重新下載比對 SHA-256 一致）。
- 2026-09-27：**功能追平 WPF 版**：系統匣／選單列圖示畫成果凍、高度 = 最低剩餘額度（AI 工作中多一個藍點）；
  Windows 通知改用開始選單捷徑的 AppUserModelID，顯示成 SentriPet 自己發的（原本顯示成 PowerShell）；
  右鍵選單補上透明度、每天隨機換造型；`--probe` 搬到共用核心。macOS 的眼睛改用 CoreGraphics 的全域游標
  （滑鼠經過桌寵時自動校正座標比例）；Linux 用 X11 的 EWMH 判斷全螢幕。WPF 版的介面測試（擺放位置、表情、詳情卡、說的話）
  移植過來，桌寵自我測試 37 → 70 項。
- 2026-09-27：**CI 在真的桌面上開程式**：新增 `--smoke-test`，實際開啟 15 秒後回報視窗、位置、動畫、系統匣、錯誤。
  三個系統都通過（Linux 用 Xvfb 虛擬螢幕，macOS 用 CI 本身的桌面）。順便抓到一個問題：沒有「主螢幕」的 X11 環境
  （虛擬螢幕、部分多螢幕設定）桌寵會停在左上角，改成改用第一個螢幕，煙霧測試也加上「第一次開在右下角」的檢查。
- 2026-09-27：**第 7 階段完成（2.0.0）**：使用者要求完成跨平台開發。跨平台版成為唯一版本，WPF 版退役：
  刪除 `src/App.cs`、`src/UI`、`src/Themes`、WPF 介面測試與 `build.cmd`（保留在 git 標籤 `v1.4.0` 與 v1.4.0 發佈頁）；
  共用核心拿掉 .NET Framework 的分支；`install.cmd` 改成用 .NET 10 SDK 發佈單一執行檔並安裝、`uninstall.cmd` 一併移除開始選單捷徑；
  CI 與 Release 流程拿掉 WPF 工作；README、CLAUDE.md 改寫；只有 WPF 版用到的 6 句翻譯移除。
  在這台電腦用新的 `install.cmd` 安裝 2.0.0：啟動正常、開始選單捷徑建立、記憶體約 128 MB。
- 2026-09-27：**發佈 [v2.0.0](https://github.com/daven55663/SentriPet-AI/releases/tag/v2.0.0)**（使用者同意）：
  win-x64 41 MB、osx-arm64 41 MB、osx-x64 43 MB、linux-x64 40 MB，四個安裝檔都先跑過自己的自我測試才發佈。
- 2026-09-27：**#9 Claude Code 狀態列橋接**（Linux 讀得到 Claude 用量、各系統的重置時間改用官方的）。
  - 查 Claude Code 文件確認：狀態列指令在每次回覆後收到 JSON，其中 `rate_limits.five_hour`／`seven_day` 有 `used_percentage`（0–100）
    與 `resets_at`（Unix 秒）；只有 Pro／Max 訂閱、而且第一次回覆之後才有。Windows 上透過 Git Bash（沒有就用 PowerShell）執行。
  - `SentriPet --statusline`：讀輸入、把官方數字存到 `~/.claude/sentripet-status.json`（放家目錄而不是 AppData：從 Claude 桌面 App
    啟動的程式在 AppData 新建的檔案會被 MSIX 轉到私人位置），再代為執行使用者原本的狀態列指令
    （用同樣的 shell、同樣的輸入），畫面照常；沒有原本的狀態列就顯示「5h 剩 76% · 週 剩 59%」。實測一次 0.1 秒。
  - 設定頁「連接 Claude Code 狀態列」：修改 `~/.claude/settings.json`（先備份一次原檔、保留其他設定與狀態列的 padding 等選項），
    關掉時原封不動還原；程式搬家時自動更新路徑，使用者自己在 Claude 拿掉時開關也跟著關。只有使用者自己按才會修改。
  - Claude 資料來源：狀態列比桌面版紀錄新時用官方的用量；重置時間一律用官方的（不再標 ≈）；沒有桌面版時（Linux）只用狀態列。
  - 測試：核心 +42 項（解析、代為執行原本的狀態列、開關與還原、搬家、壞掉的設定檔不修改、資料來源的各種組合），
    桌寵自我測試 +2 項（真的把程式當狀態列指令執行）。
- 2026-09-27：**右鍵選單整理**（使用者回報）：「換造型」和「今天心情如何？」是同樣 8 種造型的兩個選單，合併成一個「換造型」，
  每一項右邊用灰字標出心情，「交給命運吧（隨機）」和「每天隨機換一個」也放進來；選造型時桌寵照樣說「今天是『…』模式」。
  選單最上面的用量摘要改成每個 AI 一行（原本一行太長會被截斷）。選單改成可以離線建立：`--snapshot` 會畫出選單與子選單，
  自我測試檢查「只有一個換造型、沒有重複的心情選單、8 種造型都標出心情、目前的打勾」。README 截圖全部換成 2.x 的畫面。
- 2026-09-28：**發佈 [v2.1.0](https://github.com/daven55663/SentriPet-AI/releases/tag/v2.1.0)**（使用者同意）：狀態列橋接與選單整理；
  win-x64 41 MB、osx-arm64 41 MB、osx-x64 43 MB、linux-x64 40 MB。新增 `--connect-claude-statusline`／`--disconnect-claude-statusline`
  （開程式時直接開關橋接，給捷徑或腳本用）。依使用者要求在這台電腦開啟：用帶參數的捷徑經 explorer 啟動（在 Claude 桌面 App 的容器外），
  `settings.json` 加上狀態列指令、原檔備份為 `settings.json.sentripet-backup`；用範例資料實測已安裝的指令 0.1 秒。
- 2026-09-28：**推廣準備**（使用者要求：英文 README、示範動圖、GitHub 頁面、套件管理器）。
  - `README.md` 改成英文、原本的繁中搬到 `README.zh-TW.md`（兩份互相連結），英文版附英文介面的截圖（`docs/images/en/`）；
    修正 macOS 15 以後「右鍵 → 打開」已經不能略過未簽章 App 的說明、更新測試數量。
  - `--demo 檔案.gif`：用真的造型與範例資料逐格畫出 18 秒的示範動畫（平常的果凍 → 額度快過期還沒用完的著急表情 →
    其他 7 種造型），自己寫的 GIF 編碼器（每幕一組調色盤、有序抖色、每格只存變動的區域），約 2.4 MB；
    五種語言都能產生（日文、韓文版可用在各國社群）。自我測試會產生一個 GIF 再解碼、逐像素比對（+7 項）。
    `--social-card`：GitHub 分享連結時顯示的 1280×640 預覽圖。
  - 套件管理器：repo 本身當 Scoop bucket（`bucket/`）與 Homebrew tap（`Casks/`），`packaging/winget/` 是給 winget-pkgs 的設定
    （`winget validate` 通過）。Scoop 不用 shim（GUI 程式經過 shim 會跟著終端機一起被關），改成裝好直接啟動，
    開始選單捷徑由程式自己建立（通知需要）。Homebrew 安裝時移除隔離標記（沒有 Apple 開發者簽章）。
    新的 Package managers 流程在 Windows／macOS 上真的用 Scoop、Homebrew 安裝、跑自我測試、移除；
    發佈流程在發佈後自動更新這三種設定並觸發它。
- 2026-09-29：**開發路線圖**（使用者要求把討論過的功能全部排進時程）：建立里程碑 2.2（陪你用 Claude Code）、
  2.3（用量報告）、2.4（養成與自訂），13 個功能各開一個 issue（#14～#26），總覽在
  [#27](https://github.com/daven55663/SentriPet-AI/issues/27)。
- 2026-09-29：**#8 動畫的 CPU**（2.2）。新增 `--perf-test`：實際開桌寵，逐一量每種造型在每秒 30／15／1 格與隱藏時的 CPU。
  發現造型本身的計算幾乎不花 CPU（< 0.5%），成本幾乎全在繪製，而且跟幀數成正比：
  - 大卡片上的 `DropShadowEffect`：卡片裡任何一點變動（閃爍的圓環、倒數），整張卡片都要畫進圖層再模糊一次。
    改成 Border 自己的 `BoxShadow`（外觀相同）→ 極簡玻璃每秒 30 格時 17.5% → 4.7%、賽車儀表 24.6% → 7.5%、便利貼 13.7% → 1.6%。
  - 賽車儀表每格都重建三條發光弧線（彈簧動畫的數值永遠有微小變化）→ 指針真的動了才更新；
    駭客終端的掃描線每格捲動、外殼頻繁閃爍，都會讓整個發光螢幕重畫 → 掃描線固定、偶爾才閃。
  - 自動幀數：平常約每秒 16 格，滑鼠在上面、拖曳、說話、點擊、數字變化、換造型、AI 工作中時約 22 格；隱藏時停止。
  - 結果（平常狀態、130%）：賽車儀表 26.8% → 2.1%、極簡玻璃 15.3% → 2.3%、便利貼 14.7% → 1.8%、駭客終端 14.1% → 1.0%、
    果凍 7.8% → 2.9%、霓虹 6.9% → 4.2%、藥水 5.6% → 3.9%、像素 2.5% → 0.3%。
  - 順便發現：以較高 DPI 離線繪製時，Avalonia 畫帶 BoxShadow 的 Border 會漏掉 DPI 縮放（卡片 1 倍、內容 1.5 倍）；
    截圖、示範動畫、設定頁預覽改成 96 DPI 加縮放轉換（`Snapshots.Draw`），自我測試 +6 項檢查縮放繪製。
    真正的桌寵視窗不受影響（實際截圖確認）。
- 2026-09-29：**2.2 的功能**（使用者要求完成 #8 與整個 2.2）：
  - #17 勿擾時段：時段（跨午夜的時段算開始那天）與星期、右鍵選單暫停 1 小時；期間不跳通知、不主動說話，
    新的「快用掉」等級與週報總結等勿擾結束才說。核心 `Quiet`。
  - #15 用完預測、#16 額度利用率週報：核心 `UsageHistory` 記下每個額度的變化（數字變動時與每 5 分鐘一點），
    照最近一小時（每週額度六小時）的速度、而且最近 20 分鐘還在動時才預測；懸停卡片顯示，剩 45 分鐘時提醒一次。
    週／月額度重置時記下那一期的最後用量（存在設定資料夾的 report.json，關機時跨過重置也會用關機前的數字補記並標明），
    桌寵說一句總結、可跳通知，設定頁顯示最近 8 期長條圖與平均。
  - #14 AI 做完或在等你：查 Claude Code 文件確認 hooks 格式——Stop hook 印出 JSON 可能擋住 Claude 結束、
    UserPromptSubmit 的輸出會變成對話內容，所以 `SentriPet --hook` 什麼都不印、結束碼一律 0。
    Stop 每次回覆都會觸發，所以從對話紀錄找最後一次提問的時間，超過 30 秒的任務才提醒；Notification 只接要你確認與在等你輸入。
    Codex 用 config.toml 最上層的 `notify`（JSON 在最後一個參數），從 sessions 紀錄的 task_started 算時間；
    原本就有的 notify 程式會照常執行。事件寫在 `~/.claude`、`~/.codex` 底下（避開 MSIX 的 AppData 轉存），桌寵每秒讀新的行。
    實測 hook 執行約 60 毫秒。
  - 測試：核心 396 → 477 項、桌寵自我測試 86 → 99 項（含把程式當 hook 執行的端到端測試）。版本 2.2.0。
- 2026-09-29：發佈 v2.2.0（使用者同意）；版本說明附上開發與測試環境（使用者要求）：Intel Core Ultra 7 265KF（20 核）、64 GB、
  Windows 11 專業版 25H2（26200.9457）、3 台 1920×1080 螢幕（100%）、.NET SDK 10.0.401、Avalonia 12.1.3、
  Claude 桌面版 2.9939.4（Claude Code 2.1.284）、Codex 26.924。#8 的 CPU 數字都是在這台量的。
- 2026-09-29：依使用者要求在這台電腦更新到 2.2.0 並開啟「AI 做完或在等你時提醒」（新增 `--connect-agent-hooks`，經 explorer 啟動）。
  使用者的 Codex 設定原本有 Codex 桌面版 computer use 的 `notify`（`codex-computer-use.exe turn-ended`），已記下並由 SentriPet 照樣代為執行；
  確認 config.toml 只改了 notify 那一行、Claude 的 settings.json 只多了 hooks，備份都是原檔。新增這種格式的測試（核心 480 項）。
- 2026-09-29：**右鍵選單整理**（使用者回饋）：不常動的開關（永遠在最上層、滑鼠穿透、會說話、額度提醒通知、催我用完週額度、
  開機自動啟動）只留在設定頁（系統匣選單仍保留滑鼠穿透，關掉它的唯一途徑）；換造型下面新增「額度利用率（週報／月報）」子選單：
  每個週／月額度一行「這期用了 X%，上期 Y%」，以及「查看完整報告…」（打開設定頁並捲到長條圖）。自我測試 +3 項，README 截圖與說明更新。
  使用者接著問能不能不用跳到設定頁就看圖表：子選單直接顯示每個額度最近 8 期的長條圖＋這期（空心、中性藍色，還沒結束不算好壞）與
  「上期／平均／這期」，滑鼠停在長條上有日期與數值；長條圖抽成 `ReportChart`，設定頁也加上「這期」那根。
- 2026-09-29：發佈 v2.2.1（使用者要求）：右鍵選單整理、選單裡的額度利用率圖表、`--connect-agent-hooks`；版本說明同樣附上開發與測試環境。
- 2026-09-29：開發環境整理成固定的一頁（`docs/environment.md`、`docs/environment.zh-TW.md`，含 CI 的系統映像：
  Windows Server 2025、macOS 26 Apple 晶片、Ubuntu 24.04），README 最上方加上連結徽章（使用者要求）。
- 2026-09-29：**2.3 的功能**（使用者要求「開始執行 2.3 版本實作」）：
  - #18 用量報告：核心 `TokenLedger` 讀 Claude Code 對話紀錄（`~/.claude/projects`）與 Codex 的 sessions，只取 token 數、模型、資料夾，
    不碰對話內容。最近 31 天、每個檔案記住讀到哪裡（之後只讀新的行）；Claude 同一則回覆會寫好幾行，用訊息 id＋requestId 去重、留最大的；
    Codex 的 token_count 是累計值，取相鄰兩筆的差。報告視窗：額度利用率、每天 token（7／30 天，依 AI 堆疊）、專案排行、模型。
  - #19 API 等值費用：官方價格表（2026-09-29 查的 Anthropic、OpenAI 價格頁）存成內嵌的 `prices.json`，型號用最長前綴比對；
    設定資料夾放一份 `prices.json` 就能更新價格。價格表上沒有的模型（例如 codex-auto-review）列出來、不計入。
  - #20 週報圖：1200×675 PNG 存到「圖片／SentriPet」：這週 token、API 等值、最常用的模型、額度利用率、每天小圖表，加上桌寵。
  - #21 系統匣顯示數字：圖示畫成最低的剩餘 %（顏色綠／黃／紅、沒資料是「?」、AI 工作中有藍點），縮到 16 px 也看得清楚；
    「只顯示在系統匣」時桌寵收起來，點圖示再叫出來（沒有系統匣的 Linux 桌面忽略這個選項）。
  - #22 給其他程式用：`usage.json`（有版本號；數字變了才寫、至少每分鐘寫一次，先寫暫存檔再換掉；關掉就刪除）；
    本機網頁 `UsageServer` 只聽 127.0.0.1，只接受 Host 是 127.0.0.1／localhost／[::1] 的 GET（避免 DNS rebinding），不送 CORS 標頭；
    `/` 是給 OBS 瀏覽器來源的透明網頁（用量條或桌寵，網址參數調整），`/usage.json`，`/pet.png`（桌寵現在的樣子；桌寵隱藏時自己推進動畫）。
    格式與範例寫在 `docs/usage-json.md`。冒煙測試真的用 HTTP 取一次 JSON 與桌寵畫面。
  - 測試：核心 480 → 556 項、桌寵自我測試 99 → 113 項、冒煙測試 7 → 9 項。版本 2.3.0。
- 2026-09-29：發佈 v2.3.0（使用者要求）：四個安裝檔各自自我測試後發佈，Scoop／Homebrew／winget 安裝測試通過；
  版本說明附上開發與測試環境（版本與 2.2.1 相同）。winget 的分支 `daven55663.SentriPet-2.3.0` 已放在 fork，PR 由使用者送出。
- 2026-09-29：**懸停卡片換語言後留著中文**（使用者回報，日文與英文的截圖）：卡片只在版面改變時重建、平常就地更新數字，
  但額度名稱（5 小時、每週）與最下面的操作提示是建卡時寫進去的，判斷要不要重建的簽章又不含語言。簽章加上語言與額度名稱；
  自我測試 +2 項（換成英文後卡片會重建、上面沒有中文）。
- 2026-09-29：依使用者要求在這台電腦更新到 2.3.0（含上面的修正），並開啟系統匣顯示剩餘 %、輸出 usage.json、直播畫面（OBS）；
  「只顯示在系統匣」沒開（會把桌寵收起來）。修改前的設定備份成 `settings.json.before-2.3`。確認真實記錄檔無錯誤、
  usage.json 有三個 AI 的數字、`http://127.0.0.1:47291/` 的 JSON 與桌寵畫面都有回應。
- 2026-09-29：發佈 v2.3.1（使用者要求）：懸停卡片換語言的修正；版本說明同樣附上開發與測試環境。
- 2026-09-30：**2.4 開始**（使用者要求「開始 2.4 開發」），順序：#25 → #23 → #24 → #26（小的、互不相依的先做）。
  - #25 自訂台詞：設定資料夾的 `lines.json`，先依語言（或 `all`）再依事件分組，共 18 種事件，各有可以用的記號；
    `Lines` 的每一句都經過 `Choose`：有自訂的就用自訂的（`"mix": true` 和內建的混著說）。記號不屬於該事件的那句略過並列在設定頁，
    整個檔案讀不懂就全部用內建的。檔案改了幾秒內生效（每 2 秒最多看一次修改時間）；第一次「開啟台詞檔」寫入內嵌的範例。
    韓文照樣經過 `L.Finish`。說明 `docs/custom-lines.md`。核心測試 556 → 581 項、自我測試 115 → 116 項。
- 還沒做、需要實機才能確認的：Mac／Linux 實體電腦上的長時間試用（拖曳、系統匣、通知、開機啟動、滑鼠穿透、macOS 的眼睛）；
  Linux 上的 Claude 用量要開啟「連接 Claude Code 狀態列」。