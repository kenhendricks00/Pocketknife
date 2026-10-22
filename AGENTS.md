# Working in this repository

## Commands
- Build: `dotnet build WindowsToolbox.slnx` (warnings are errors).
- Test: `dotnet test --project tests/Toolbox.Tests`. The first run downloads the real engines into `%LOCALAPPDATA%\WindowsToolbox-tests\engines`. Set `TOOLBOX_TEST_ENGINES` to use another folder.
- Format: `dotnet format WindowsToolbox.slnx --verify-no-changes`.
- Package: `./scripts/package.ps1` writes the portable zip, installer and checksums to `release/`.
- UI check: `./scripts/ui-smoke.ps1` drives the published app with UI Automation and saves screenshots.

## Layout
- `src/Toolbox.Core`: tools, the runner, engines, PDF/image/media helpers. No UI code beyond WPF imaging.
- `src/Toolbox.App`: the WPF app. Tool screens are generated from each tool's option schema.
- `tests/Toolbox.Tests`: behavior tests. Tool tests run the real engines on the small fixtures in `Fixtures/`. `scripts/make-fixtures.ps1` regenerates them.
- `docs/decisions`: why things are the way they are. Read these before changing the stack, engines or licensing.

## Rules
- Tools never modify inputs and only write through `ToolJob.OutputPath`/`OutputPathFor` into the run folder. `ToolHarness` checks this in every tool test.
- Errors meant for people are `ToolException` with a plain sentence.
- Every bundled dependency must be MIT, Apache-2.0 or similarly permissive. GPL programs (FFmpeg) are only downloaded at runtime and run as separate processes. PDFsharp 6.5+ is AGPL, so don't add it.
- Engines are pinned by version, URL and SHA-256 in `EngineCatalog`. Update all three together.
- Glyphs in C# use `\uXXXX` escapes, never raw private-use characters.
