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
- 還沒做、需要實機才能確認的：Mac／Linux 實體電腦上的長時間試用（拖曳、系統匣、通知、開機啟動、滑鼠穿透、macOS 的眼睛）；
  Linux 上讀不到 Claude 方案用量（#9）。