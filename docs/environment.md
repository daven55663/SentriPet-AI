# Development and test environment

**English** | [繁體中文](environment.zh-TW.md)

The machine SentriPet is developed and tested on. The CPU figures in the README and the release notes (for example
the ones in [#8](https://github.com/daven55663/SentriPet-AI/issues/8)) were measured here with `SentriPet --dev --perf-test`;
on a slower CPU the numbers are higher, the differences between versions similar.

Last updated: 2026-09-29 (SentriPet 2.2.1)

## Hardware

| | |
|---|---|
| CPU | Intel Core Ultra 7 265KF (20 cores / 20 threads) |
| Memory | 64 GB |
| Graphics | NVIDIA GeForce RTX 5070 Ti (SentriPet draws on the CPU by default and does not use it) |
| Screens | 3 × 1920×1080, scaling 100% |

## Software

| | |
|---|---|
| Operating system | Windows 11 Pro 25H2 (build 26200.9457) |
| .NET | SDK 10.0.401 (runtime 10.0.12) |
| UI framework | Avalonia 12.1.3 (Skia, HarfBuzz; Fluent theme) |
| Tools | Git 2.55, GitHub CLI 2.101, Python 3.12 (translation scripts) |
| AI tools (also the usage sources SentriPet reads) | Claude desktop app 2.9939.4 (Claude Code 2.1.284), Codex 26.924 |
| Development | With Claude Code (Claude Opus 5.5) |

## Automated tests

| | |
|---|---|
| This machine | Core checks (480), the app's self-test (103), a smoke test of the real app — before every push |
| GitHub Actions | The same on Windows Server 2025, macOS 26 (Apple silicon) and Ubuntu 24.04 (a virtual display), plus the four packages; each package runs its own self-test before a release is published, and SentriPet is installed through Scoop, Homebrew and winget and tested again |

The macOS and Linux versions pass these automated tests but have not been used for long on real Macs or Linux PCs yet —
please [open an issue](https://github.com/daven55663/SentriPet-AI/issues/new) if something looks wrong there.
