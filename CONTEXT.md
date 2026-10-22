# Context

The words this codebase uses, so code, issues and docs mean the same thing.

- **Tool**: one feature with its own screen, such as Compress Video. A `Tool` subclass declares its input, its options and the engines it needs, and does the work in `RunAsync`. All tools are listed in `ToolRegistry`.
- **Input**: what a tool takes. A `FileInput` takes files, or folders whose top-level matching files are used. A `TextInput` takes text, such as links or QR content.
- **Combine**: a file input where all files form one job (Merge PDFs, Read QR Code) instead of one job per file.
- **Option**: one setting on a tool's screen (choice, number, toggle or text). `OptionValues` holds the validated values for a run. `VisibleWhen` hides an option unless another choice has a given value.
- **Run**: one press of Start. A run creates one **run folder** named `yyyy-MM-dd HH.mm.ss <Tool name>` under the **output root** (`Downloads\Pocketknife` by default).
- **Job**: one unit of work inside a run, usually one input file. A failing job is recorded as a failure and the run continues with the next job.
- **Engine**: an external program or model (FFmpeg, yt-dlp, Whisper and background models) that is downloaded on first use, verified against a pinned SHA-256 and stored under `%LOCALAPPDATA%\Pocketknife\engines`. A **custom path** set in Settings replaces the managed copy.
- **Launcher**: the small search window opened with the global hotkey (default `Ctrl+Alt+Space`).
- **Redaction**: removing content for good. Redact PDF rasterizes every page, paints matches black and builds a new PDF from the images, so no text layer or metadata remains.
