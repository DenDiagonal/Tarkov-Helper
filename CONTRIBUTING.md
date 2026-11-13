# Contributing to Tarkov Helper

First off, thank you for considering contributing to **Tarkov Helper**! 🎉

This project is an open-source, community-driven companion tool created to make Escape from Tarkov raids smoother, clearer, and more tactical. Contributions of all kinds are welcome—from bug fixes and UI improvements to performance optimizations and new features.

---

## 🧭 Code of Conduct & Ground Rules

1. **Be Respectful:** Treat fellow community members and maintainers with kindness and respect.
2. **BattlEye Compliance:** We strictly prohibit any feature or contribution that reads EFT process memory, injects code/DLLs into the game, or tampers with network packets. Tarkov Helper operates exclusively via passive log reading and screenshot metadata to ensure 100% compliance with BSG rules.
3. **Data Integrity Principle (No Stale Git History Recovery):**
   - Escape from Tarkov evolves constantly. Trader tasks, level requirements, and item requirements change across wipes and minor game updates.
   - **Never overwrite or restore quest data from historical git commits or old database dumps.** The only valid sources of truth are the live APIs (`tarkov.dev`, `TarkovTracker`) and the active BSG server dumps.

---

## 🛠️ Getting Started

### Prerequisites
- Windows 10 or 11 (64-bit)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- An IDE such as Visual Studio 2022 (with *.NET desktop development* workload), JetBrains Rider, or VS Code with C# Dev Kit.

### Setup Your Local Repository
1. Fork the repository on GitHub: `https://github.com/DenDiagonal/Tarkov-Helper`
2. Clone your fork locally:
   ```powershell
   git clone https://github.com/<your-username>/Tarkov-Helper.git
   cd Tarkov-Helper
   ```
3. Create a descriptive branch for your feature or fix:
   ```powershell
   git checkout -b feat/my-new-feature
   # or
   git checkout -b fix/issue-description
   ```

---

## 🏗️ Architecture Overview

- **WPF & MVVM:** UI layers are built in XAML with strongly-typed ViewModels, reactive data bindings, and asynchronous command execution.
- **SQLite Database (`tarkov_data.db`):** Contains game definitions (quests, traders, requirements, maps, items). User progress is isolated in `user_data.db`.
- **Vector Graphics:** Maps and tactical markers use vector SVG rendered via `SharpVectors`.
- **Deployment:** The application is deployed self-contained for `win-x64` so players do not need an external .NET runtime installed.

---

## 🧪 Testing & Verification

Before submitting your changes:

1. **Compile cleanly:**
   ```powershell
   dotnet build TarkovHelper\TarkovHelper.csproj -c Release
   ```
   Ensure there are **0 compilation errors**.

2. **Verify self-contained publishing:**
   ```powershell
   dotnet publish TarkovHelper\TarkovHelper.csproj -c Release -r win-x64 --self-contained true -o publish
   ```
   Confirm that `publish\TarkovHelper.runtimeconfig.json` contains `"includedFrameworks"`.

3. **Database Integrity:**
   Ensure `publish\tarkov_data.db` is intact and user progress is never destroyed.

---

## 📬 Submitting a Pull Request (PR)

1. Commit your changes with clear, semantic commit messages (e.g. `feat(map): add floor elevation switch`, `fix(quest): resolve alternative prerequisite bug`).
2. Push your branch to your fork:
   ```powershell
   git push origin feat/my-new-feature
   ```
3. Open a Pull Request on GitHub against `main`.
4. Include a concise description of your changes, screenshots for UI modifications, and test results.

Thank you for helping build the best tactical companion for Escape from Tarkov! 🚀
