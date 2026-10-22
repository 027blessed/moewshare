# Changelog — Moewshare Engine

All notable changes to this project are documented in this file.

## [0.1.0-events] - 2026-10-22

### Added
- **Global and Per-Object Event Graphs**:
  - `SceneObject.EventGraph`: scene objects can now have their own dedicated visual event graphs.
  - Added "⚡ Редактировать граф объекта" button in the Inspector for fast navigation.
  - Added header badge and switcher in the Event Graph toolbar (`[ 🌐 Глобальный ]` / `[ 📦 Граф объекта ]`).
  - Runtime event dispatching for both global scene and per-object levels in `EventRuntime.cs` and `moewshare-runtime.js`.
- **Clicker & Interactive Nodes**:
  - `event_ui_button_clicked`: triggers when a specified UI button is clicked.
  - `event_object_clicked`: triggers when a 3D/2D object is clicked via mouse/touch raycasting.
  - `event_timer`: triggers periodically (default: 1.0 second) for idle/passive income calculations.
- **Node Catalog Sidebar Tab**:
  - Added "Блоки" tab with real-time search, categorization, and single-click node spawning.
- **Templates**:
  - Built-in `Clicker Game` template with cookies mascot, UI counters, upgrade shop, and event graph logic.
- **Documentation**:
  - Comprehensive guide and tutorial in [`docs/EVENT_SYSTEM_AND_CLICKER_GUIDE.md`](file:///Users/nicklex/moode/Moewshare/docs/EVENT_SYSTEM_AND_CLICKER_GUIDE.md).

### Fixed
- **Crash on "+ Добавить блок"**:
  - Resolved Avalonia 11 `OverlayPopupHost` crash where `$parent[UserControl]` reflection inside Flyouts threw `ArgumentException: Unable to resolve type vm:EventGraphVM`. Replaced with explicit C# event handlers.
- **Data Type Casting in `var_set`**:
  - Handled automatic conversions from floating point numbers (`double`) to integer target variables.
- **Math Port Aliasing**:
  - Added port name aliases (`out_result`, `out_val`, `out_res`) to prevent unlinked outputs during graph compilation.

### Tests
- Added 3 new comprehensive unit and integration tests:
  - `Clicker_Mechanic_EndToEnd_Simulation`
  - `ObjectLevel_EventGraph_Executes_Independently`
  - `GlobalAndObject_EventGraphs_Coexist`
- Total tests passing: 57 / 57.
