# Working in this repository

## Commands
- Build: `dotnet build Pocketknife.slnx` (warnings are errors).
- Test: `dotnet test --project tests/Pocketknife.Tests` (core and tools) and `dotnet test --project tests/Pocketknife.App.Tests` (screens, launcher and settings, laid out and rendered off-screen). The first run downloads the real engines into `%LOCALAPPDATA%\Pocketknife-tests\engines`. Set `POCKETKNIFE_TEST_ENGINES` to use another folder.
- Format: `dotnet format Pocketknife.slnx --verify-no-changes`.
- Package: `./scripts/package.ps1` writes the portable zip, installer and checksums to `release/`.
- UI check: `./scripts/ui-smoke.ps1` checks a published build end to end in hidden mode. Screenshots come from the app tests with `POCKETKNIFE_SCREENSHOTS=<folder>`.
- Logo: `./scripts/make-icon.ps1` rebuilds `app.ico`, the banner, the social preview and the installer images from `assets/logo.png`. Don't edit those by hand.

## Layout
- `src/Pocketknife.Core`: tools, the runner, engines, PDF/image/media helpers. No UI code beyond WPF imaging.
- `src/Pocketknife.App`: the WPF app. Tool screens are generated from each tool's option schema.
- `tests/Pocketknife.Tests`: behavior tests. Tool tests run the real engines on the small fixtures in `Fixtures/`. `scripts/make-fixtures.ps1` regenerates them.
- `tests/Pocketknife.App.Tests`: the real launcher, settings and tool screens on a hidden STA thread, rendered with RenderTargetBitmap. Never shown.
- `docs/decisions`: why things are the way they are. Read these before changing the stack, engines or licensing.

## Rules
- Testing must be invisible to whoever is using the PC: no visible windows or consoles, no focus changes, no simulated keyboard or mouse input, no tray icons, notifications or sounds, and nothing left running. App tests never construct `App` (its startup takes the single-instance lock, adds a tray icon and registers the hotkey); they use a plain `Application` with `AppResources.Apply`. Hotkey tests use F20 to F24 shortcuts, which no normal keyboard can press, and post `WM_HOTKEY` instead of pressing keys. Clipboard and browser access in the launcher are injected so tests capture them.
- Shared styles live in `src/Pocketknife.App/Styles.xaml`. Clickable controls use styles that set the hand cursor (and the arrow when disabled); keep new ones based on those.
- Tools never modify inputs and only write through `ToolJob.OutputPath`/`OutputPathFor` into the run folder. `ToolHarness` checks this in every tool test.
- Errors meant for people are `ToolException` with a plain sentence.
- Every bundled dependency must be MIT, Apache-2.0 or similarly permissive. GPL programs (FFmpeg) are only downloaded at runtime and run as separate processes. PDFsharp 6.5+ is AGPL, so don't add it.
- Engines are pinned by version, URL and SHA-256 in `EngineCatalog`. Update all three together.
- Glyphs in C# use `\uXXXX` escapes, never raw private-use characters.
