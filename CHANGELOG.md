# 📜 GHOSTWing Changelog

All notable changes to the **GHOSTWing** project will be documented in this file.

---

## [1.0.3] - 2026-05-01 (Universal Security & Marketplace Update)

### 🚀 Added

- **Universal Security Architecture**: Implemented a dynamic tab-locking system. All tabs (Settings, Account, Recoil, etc.) are now automatically secured via SQL-driven "Teaser" overlays.
- **Dynamic VIP Marketplace**: Introduced a fully data-driven VIP Cart. Prices, features, and descriptions are now fetched live from Supabase SQL.
- **Lifetime Access Support**: Added logic for "Lifetime Access" licenses. If a user's expiry date is set to `NULL` in the database, they gain permanent elite status and the UI displays "LIFETIME ACCESS".
- **Dynamic Plan Tracking**: Integrated `plan_id` (1=FREE, 2=GOLD, 3=DIAMOND) tracking. The Marketplace now automatically highlights the user's active plan and disables the corresponding checkout button.
- **Improved UI Scaling**: Refined the marketplace card designs with better spacing, readable descriptions, and updated text contrast for high-resolution displays.

### 🛠️ Fixed

- **Navigation Logic**: Resolved an issue where clicking "Upgrade" on a locked tab would incorrectly redirect to the Account page instead of the VIP Cart.
- **Zero-Warning Build**: Refactored the core entitlement engine to remove all C# nullable reference warnings, ensuring a clean production compile.
- **XAML Stability**: Fixed a critical nesting error in the main dashboard that prevented successful compilation during the UI refactor.
- **Accurate Branding**: Standardized sidebar badges and labels across all tabs for a more unified premium look.

 
 ### 🚀 Added
 
 - **ADS Hide Feature**: New "Hide on ADS" toggle in the crosshair tab. Automatically hides the crosshair while holding the right mouse button to keep your view clean when aiming down sights.
 - **Unique Device Identification (UUID)**: Implemented a persistent, hardware-locked UUID system (Motherboard + CPU + Disk) for user licensing and future premium features.
 - **Slim Modern Scrollbars**: Replaced bulky default Windows scrollbars with sleek, dark-themed 6px scrollbars that blend perfectly with the industrial design.
 - **Independent Tab Scrolling**: Every tab (Recoil, Crosshair, Settings) is now independently scrollable, ensuring all options are accessible on any screen size.
 
 ### 🛠️ Fixed
 
 - **Crosshair Default Color**: Set the default crosshair color to Red for better initial visibility.
 - **Preview Box Scaling**: Fixed the crosshair preview border to dynamically stretch and match the height of the customizer panel for a perfectly balanced UI.
 - **Sidebar Scrolling**: Added scroll support to the sidebar menu for future-proofing.
 
 ---
24: 

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
