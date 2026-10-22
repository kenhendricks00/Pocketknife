# 2. Download engines on first use, pinned and verified

Date: 2026-10-21

## Status
Accepted

## Context
FFmpeg, yt-dlp and the AI models are large (5 MB to 179 MB each), and FFmpeg builds are GPL. Shipping them would make every download big and mix licenses into the app's distribution.

## Decision
- `EngineCatalog` pins each engine to an exact URL, version, size and SHA-256.
- `EngineManager` downloads into a staging folder, hashes while streaming, deletes the file on a mismatch, extracts only the listed files and moves the result into `engines\<id>\<version>` with a `.verified` stamp. Concurrent requests for the same engine share one download.
- A custom path set in Settings always wins. A missing custom copy is an error instead of a silent fallback to downloading.
- Tools declare the engines they need for the chosen options, so the runner can download them before work starts and the screen can say what will be downloaded.

## Consequences
- yt-dlp needs regular updates for site changes. Updating means changing the pinned version and hash, or pointing Settings at a newer `yt-dlp.exe`.
- The only network use is the Download Video tool and these pinned downloads.
