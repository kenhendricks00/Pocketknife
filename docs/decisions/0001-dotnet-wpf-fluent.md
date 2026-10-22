# 1. .NET 10 and WPF with the Fluent theme

Date: 2026-10-21

## Status
Accepted

## Context
The app should look and behave like a native Windows 11 app, follow light and dark mode, ship as a portable zip and an installer, and host native engines (whisper.cpp, ONNX Runtime) in-process.

## Decision
Use .NET 10 (LTS) and WPF with the built-in Fluent theme (`ThemeMode="System"`).

- The Fluent theme in WPF gives Windows 11 styling and follows the system theme with no third-party UI library.
- WPF apps publish as a self-contained, unpackaged app. WinUI 3 needs the Windows App SDK runtime and its packaging model, which makes a portable zip harder.
- Tauri or Electron would put a web view between the user and the shell, and Tauri needs a Rust toolchain. A .NET app calls Win32 (`RegisterHotKey`, known folders) and WinRT (`Windows.Data.Pdf`) directly.
- Logic lives in `Toolbox.Core` so it is tested without UI. The app project only binds to it.

## Consequences
- The UI framework is Windows only, which matches the product.
- The publish is about 75 MB compressed because the .NET runtime is included. The managed code ships as one compressed exe, with the native DLLs (WPF, ONNX Runtime, whisper.cpp) beside it. Whisper.net can't load its native library when natives are self-extracted from a single file, so they are kept as files.
- The Fluent theme APIs are marked experimental (WPF0001). The warning is suppressed in the app project.
