# 開發與測試環境

[English](environment.md) | **繁體中文**

SentriPet 是在這台電腦上開發與測試的。README 與版本說明裡的 CPU 數字（例如 [#8](https://github.com/daven55663/SentriPet-AI/issues/8) 的量測）
都是在這裡用 `SentriPet --dev --perf-test` 量的；較慢的 CPU 數字會高一些，但版本之間的差異相近。

最後更新：2026-09-29（SentriPet 2.3.1）

## 硬體

| | |
|---|---|
| CPU | Intel Core Ultra 7 265KF（20 核／20 執行緒） |
| 記憶體 | 64 GB |
| 顯示卡 | NVIDIA GeForce RTX 5070 Ti（SentriPet 預設用 CPU 繪製，不使用顯示卡） |
| 螢幕 | 3 台 1920×1080，縮放 100% |

## 軟體

| | |
|---|---|
| 作業系統 | Windows 11 專業版 25H2（組建 26200.9457） |
| .NET | SDK 10.0.401（執行階段 10.0.12） |
| 介面框架 | Avalonia 12.1.3（Skia、HarfBuzz、Fluent 主題） |
| 工具 | Git 2.55、GitHub CLI 2.101、Python 3.12（翻譯腳本） |
| AI 工具（也是 SentriPet 讀取用量的來源） | Claude 桌面版 2.9939.4（Claude Code 2.1.284）、Codex 26.924 |
| 開發方式 | 使用 Claude Code（Claude Opus 5.5）開發 |

## 自動測試

| | |
|---|---|
| 這台電腦 | 核心測試（480 項）、桌寵自我測試（103 項）、實機煙霧測試——每次推送前都跑 |
| GitHub Actions | 在 Windows Server 2025、macOS 26（Apple 晶片）、Ubuntu 24.04（虛擬螢幕）上跑同樣的測試並打包四個安裝檔；發佈前每個安裝檔都先跑自我測試，發佈後再用 Scoop、Homebrew、winget 實際安裝測一次 |

macOS 與 Linux 版通過了這些自動測試，但還沒有在實體 Mac 或 Linux 電腦上長時間使用；
遇到問題歡迎[開 issue](https://github.com/daven55663/SentriPet-AI/issues/new)。
