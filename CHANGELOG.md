## Unreleased

#### **First release of Pocketknife, a PC multitool for Windows: 11 everyday tools that run on your PC**
- **Video & Audio:** Download Video, Compress Video, Convert Video & Audio, Video to GIF.
- **Images:** Remove Background.
- **PDF:** Merge PDFs, Split PDF, Redact PDF.
- **Text & Speech:** Transcribe (TXT and SRT).
- **Files:** Make QR Code, Read QR Code.
- Every tool takes one file or a whole folder, never changes the originals, and writes to a new dated folder in `Downloads\Pocketknife` (configurable).
- Redact PDF rebuilds every page as an image with the matches painted black, so the text underneath, fonts and document metadata are removed.

#### **Pocketknife logo**
- The red pocketknife is now the app, window, taskbar, tray, Start menu and installer icon, and the README banner.
- Small icon sizes (16 to 32 px) are sharpened and outlined slightly so the blade stays visible on light taskbars.

#### **Quick launcher and notification-area icon**
- `Ctrl+Alt+Space` opens a search box from any app. Type a few letters and press Enter to open a tool. The shortcut can be changed in Settings.
- Closing the window keeps the app in the notification area so the launcher stays available. It can also start with Windows.
- `--tool <id> [files…]`, `--launcher` and `--background` command-line switches. A second launch hands its command line to the running copy.

#### **Engines download on first use and are verified**
- FFmpeg 9.0.2, yt-dlp 2026.08.19, the Whisper base and tiny models, and the IS-Net and U²-Net small background models download from pinned URLs the first time a tool needs them.
- Each download is hashed while it streams and is thrown away unless it matches the pinned SHA-256. Nothing half-downloaded is ever used.
- Settings shows each engine's status, and lets you download it ahead of time, remove it, or point to your own copy.

#### **Follows Windows light and dark mode**
- Native Fluent design that follows the Windows setting, or Light or Dark chosen in Settings.
