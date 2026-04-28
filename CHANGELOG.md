# 📜 GHOSTWing Changelog

All notable changes to the **GHOSTWing** project will be documented in this file.

---

## [1.0.1] - 2026-04-29 (Performance & Stability Update)

### 🚀 Optimized

- **Background Performance**: Completely overhauled the recoil loop to eliminate lag when the application is minimized or hidden.
- **Thread Priority**: Increased process priority to "High" to ensure consistent mouse movement even during heavy CPU load.
- **UI Decoupling**: Implemented a caching system for settings, removing expensive UI thread calls from the background processing loop.
- **Modern Notification System**: Replaced dated Windows message boxes with professional, animated "Toast" popups for saving, deleting, and importing presets.

## [1.0.0] - 2026-04-28 (The Stealth Update)

### 🚀 Added

- **Unified Streamer Mode**: A new high-performance stealth toggle that combines visual and metadata protection.
- **Content Exclussion**: Application is now completely invisible to screen recording and sharing software (OBS, Discord, etc.) using `SetWindowDisplayAffinity`.
- **Taskbar Stealth**: Option to completely remove the application from the Windows Taskbar for total "ghost" operation.
- **Advanced System Status**: Added real-time detection of Windows version (including Windows 11 Build support) and .NET Runtime information.
- **Improved UI Layout**: Reorganized the Control Panel into a logical two-column grid for better usability.
- **Enhanced Readability**: Updated hint text colors for better contrast on high-resolution displays.

### 🛠️ Fixed

- **OS Detection**: Fixed an issue where Windows 11 was incorrectly reported as Windows 10.
- **XAML Stability**: Resolved a syntax error that occasionally prevented the app from launching after UI modifications.
- **UI Alignment**: Standardized the header layout for better visual balance.

---

## Initial Release

### 🚀 Added

- **Core Recoil Engine**: Sub-pixel movement logic for precision recoil compensation.
- **Dynamic Presets**: Ability to save, update, and delete weapon profiles.
- **Global Hotkey System**: Seamless switching between weapon profiles and macro toggling while in-game.
- **JSON Import/Export**: Share weapon configurations with other community members.
- **System Tray Support**: Ability to hide the app to the tray while maintaining functionality.
- **Auto-Update Check**: Built-in system to check for the latest versions from GitHub.
- **Portable Architecture**: Fully self-contained executable with no installation required.

---

*“Precision is the difference between a hit and a miss.”*
