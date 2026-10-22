namespace Toolbox.Core.Engines;

/// <summary>
/// An external program or model that is downloaded on first use and verified against a pinned SHA-256.
/// </summary>
/// <param name="Files">Files to keep. For a .zip download they are matched by file name anywhere in the archive; the first is the main file.</param>
public sealed record EngineSpec(
    string Id,
    string Name,
    string Version,
    Uri Url,
    string Sha256,
    long SizeBytes,
    string License,
    IReadOnlyList<string> Files)
{
    public string MainFile => Files[0];

    public bool IsZip => Url.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public string SizeText => $"{SizeBytes / 1_000_000.0:0} MB";
}

public static class EngineCatalog
{
    public static readonly EngineSpec FFmpeg = new(
        "ffmpeg",
        "FFmpeg",
        "9.0.2",
        new("https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip"),
        "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba",
        114_768_076,
        "GPL-3.0 (gyan.dev essentials build)",
        ["ffmpeg.exe", "ffprobe.exe"]);

    public static readonly EngineSpec YtDlp = new(
        "yt-dlp",
        "yt-dlp",
        "2026.08.19",
        new("https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe"),
        "66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a",
        17_840_399,
        "Unlicense",
        ["yt-dlp.exe"]);

    public static readonly EngineSpec WhisperBase = new(
        "whisper-base",
        "Whisper base model",
        "ggml-base",
        new("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-base.bin"),
        "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe",
        147_951_465,
        "MIT",
        ["ggml-base.bin"]);

    public static readonly EngineSpec WhisperTiny = new(
        "whisper-tiny",
        "Whisper tiny model",
        "ggml-tiny",
        new("https://huggingface.co/ggerganov/whisper.cpp/resolve/5359861c739e955e79d9a303bcbc70fb988958b1/ggml-tiny.bin"),
        "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21",
        77_691_713,
        "MIT",
        ["ggml-tiny.bin"]);

    public static readonly EngineSpec IsNet = new(
        "isnet-general-use",
        "IS-Net background model",
        "isnet-general-use",
        new("https://github.com/danielgatis/rembg/releases/download/v0.0.0/isnet-general-use.onnx"),
        "60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a",
        178_648_008,
        "Apache-2.0",
        ["isnet-general-use.onnx"]);

    public static readonly EngineSpec U2NetP = new(
        "u2netp",
        "U²-Net small background model",
        "u2netp",
        new("https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx"),
        "309c8469258dda742793dce0ebea8e6dd393174f89934733ecc8b14c76f4ddd8",
        4_574_861,
        "Apache-2.0",
        ["u2netp.onnx"]);

    public static IReadOnlyList<EngineSpec> All { get; } = [FFmpeg, YtDlp, WhisperBase, WhisperTiny, IsNet, U2NetP];
}
