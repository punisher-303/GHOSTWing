# 📜 GHOSTWing Changelog

All notable changes to the **GHOSTWing** project will be documented in this file.

---

## [1.0.4] - 2026-05-02 (Adaptive Intelligence & Tactical Peek Update)

### 🚀 Added

- **Tactical Peek Engine**: Integrated a high-performance background loop that automates the crouch-fire synchronization rhythm. Features configurable activation modes (Hold/Toggle) and synchronized auto-fire logic.
- **Adaptive Intelligence V2.0**: Overhauled the recoil engine with a full velocity-mapping system. The engine now calculates weapon physics in real-time, applying counter-force dynamically without the need for manual weapon presets.
- **Enhanced Intelligence UI**: Reorganized the Intelligence tab into a professional two-column grid. Features are now grouped on the left, with system insights and "How to Use" guides on the right.
- **Universal Security Architecture**: Implemented a dynamic tab-locking system. All tabs (Settings, Account, Recoil, etc.) are now automatically secured via SQL-driven "Teaser" overlays.
- **Dynamic VIP Marketplace**: Introduced a fully data-driven VIP Cart. Prices, features, and descriptions are now fetched live from Supabase SQL.
- **Lifetime Access Support**: Added logic for "Lifetime Access" licenses. If a user's expiry date is set to `NULL` in the database, they gain permanent elite status and the UI displays "LIFETIME ACCESS".
- **ADS Hide Feature**: New "Hide on ADS" toggle in the crosshair tab. Automatically hides the crosshair while holding the right mouse button.
- **Unique Device Identification (UUID)**: Implemented a persistent, hardware-locked UUID system for user licensing.

### 🛠️ Fixed

- **Resource Conflict Fix**: Resolved a critical "StaticResource" crash caused by duplicate animation keys and missing style definitions in the XAML.
- **Navigation Logic**: Resolved an issue where clicking "Upgrade" on a locked tab would incorrectly redirect to the Account page instead of the VIP Cart.
- **Zero-Warning Build**: Refactored the core entitlement engine to remove all C# nullable reference warnings.
- **XAML Stability**: Fixed a critical nesting error in the main dashboard that prevented successful compilation.
- **Accurate Branding**: Standardized sidebar badges and labels across all tabs for a unified premium look.

---

## [1.0.1] - 2026-04-29 (Performance & Stability Update)

### 🚀 Optimized

- **Background Performance**: Overhauled the recoil loop to eliminate lag when minimized.
- **Thread Priority**: Increased process priority to "High" for consistent mouse movement.
- **UI Decoupling**: Implemented a settings caching system to optimize performance.
- **Modern Notification System**: Replaced legacy message boxes with animated "Toast" popups.

## [1.0.0] - 2026-04-28 (The Stealth Update)

### 🚀 Added

- **Unified Streamer Mode**: New high-performance stealth toggle for visual and metadata protection.
- **Content Exclusion**: Application is now invisible to recording software (OBS, Discord) via `SetWindowDisplayAffinity`.
- **Taskbar Stealth**: Option to remove the application from the Windows Taskbar.
- **Advanced System Status**: Real-time detection of Windows version and .NET Runtime information.
- **Improved UI Layout**: Reorganized Control Panel into a logical two-column grid.

### 🛠️ Fixed

- **OS Detection**: Fixed Windows 11 being incorrectly reported as Windows 10.
- **XAML Stability**: Resolved syntax errors preventing app launch.

---

## Initial Release

### 🚀 Added

- **Core Recoil Engine**: Sub-pixel movement logic for precision recoil compensation.
- **Dynamic Presets**: Ability to save, update, and delete weapon profiles.
- **Global Hotkey System**: In-game weapon switching and macro toggling.
- **JSON Import/Export**: Share configurations with community members.
- **System Tray Support**: Hide the app to the tray while maintaining functionality.
- **Auto-Update Check**: Built-in update notification system.

---

*“Precision is the difference between a hit and a miss.”*
