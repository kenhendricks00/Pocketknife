## Unreleased

#### **The launcher does math**
- Type a calculation such as `12*(3+4)`, `200 + 10%`, `15% of 80`, `sqrt(2)`, `round(pi, 3)` or `255 to hex`, and the answer is the top result as you type. Enter copies it.
- Supports `+ - * / ^`, parentheses, decimals, `%`, `x% of y`, `sqrt abs round floor ceil min max sin cos tan log ln`, `pi` and `e`, and hex, binary and octal (`0xff`, `0b1010`, `to hex`, `to bin`, `to oct`, `to dec`).
- Results use your region's number format, and copies leave out the thousands separators.
- Plain words, a lone number, dividing by zero and other mistakes show no answer instead of an error. Calculations run in a small built-in parser, never as code. Turn it off in Settings.

#### **The launcher searches the web, with bangs**
- Text that isn't a tool ends the list with "Search DuckDuckGo for '…'" (matching tools stay above it). Enter opens the search in your browser.
- DuckDuckGo-style bangs work anywhere in the text: `!g`, `!ddg`, `!b`, `!yt`, `!gh`, `!w`, `!r`, `!a`, `!maps`. A bang on its own opens the site's front page, and an unknown bang searches the default engine with your text as typed.
- Settings: pick Google, Bing, DuckDuckGo, Brave, Startpage or your own search address with `{query}`, add, replace or remove bangs, restore the defaults, or turn web search off.
- Pocketknife never contacts a search engine itself and fetches no suggestions. The query is percent-encoded, only http and https addresses open, and a custom address can't put your text into the site's name.

#### **Choose your own launcher shortcut**
- Settings → Quick launcher → Change… records the next key combination you press. Ctrl+Alt+Space stays the default, and Reset brings it back.
- Shortcuts without Ctrl, Alt or Win, and ones Windows keeps for itself (Alt+Tab, Win+L, Ctrl+Shift+Esc and others), are refused with the reason.
- Alt+Space, Win+Space, Ctrl+Space and other common shortcuts ask first. Alt+Space opens the window menu in every app and is PowerToys Run's default.
- If another app already holds the shortcut you pick, the old one stays and Settings says why. If the saved shortcut is taken when Pocketknife starts, a notification says so, and the launcher is still in the notification-area menu.

#### **Hand cursor on everything clickable**
- Tool cards, sidebar items, launcher results, buttons, check boxes, drop-downs and Settings rows show the hand. Disabled controls keep the arrow, and text boxes keep the text cursor.

#### **Fixes**
- The sidebar's All tools and Settings rows now match the tool rows: same icon size, column and text color in light and dark, the same hover, pressed and keyboard-focus look, and they show when their page is open.
- Starting with Windows (`--background`) no longer opens the main window: building the tool list used to select and open the first tool, which showed the window.
- A copy of Pocketknife started with its own data folder (as tests do) is now a separate instance, so it never hands its command line to the copy you're using.

## 0.1.0 (2026-10-22)

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
