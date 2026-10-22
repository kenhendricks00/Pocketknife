# 3. MIT license and dependency policy

Date: 2026-10-21

## Status
Accepted

## Context
The app should be MIT licensed if every dependency allows it.

## Decision
License the app under MIT and only bundle MIT or Apache-2.0 components:

| Bundled | License |
| --- | --- |
| .NET runtime, WPF, Windows Forms | MIT |
| PdfPig | Apache-2.0 |
| ZXing.Net | Apache-2.0 |
| ONNX Runtime | MIT |
| Whisper.net, whisper.cpp, ggml | MIT |

Apache-2.0 is compatible with distributing an MIT-licensed app. Its license text and notices are included in `THIRD-PARTY-NOTICES.md`, which ships next to the exe.

FFmpeg (GPL-3.0 build) and yt-dlp (Unlicense) are not bundled. They are downloaded by the user's copy of the app and run as separate processes, so the app is not a derivative work of them. The models are MIT (Whisper) and Apache-2.0 (U²-Net, IS-Net).

PDFsharp was considered for PDF writing, but versions 6.5 and later are AGPL-3.0, which is not compatible with an MIT app. PdfPig's writer covers merge, split and building image pages.

## Consequences
New dependencies need a license check before they are added. AGPL, GPL and non-commercial models (for example RMBG-1.4) are excluded from the app itself.
