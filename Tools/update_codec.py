import argparse
import base64
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

PROJECT = Path(__file__).resolve().parent.parent
PACKAGE = PROJECT / "Assets/UniQTX"
WARM_UP_SOURCE = PROJECT / "Tools/warm_up.png"
TRANSCODE_WORKER = PACKAGE / "Runtime/QtxTranscodeWorker.jspre"

MONO_MATH = """
internal static class QtxMath
{
    private const double InverseLn2 = 1.4426950408889634;

    public static double Log2(double value) => Math.Log(value) * InverseLn2;

    public static int PopCount(uint value)
    {
        value -= (value >> 1) & 0x55555555u;
        value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
        return (int)((((value + (value >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
    }

    public static int LeadingZeroCount(uint value)
    {
        if (value == 0) return 32;
        var count = 0;
        while ((value & 0x80000000u) == 0) { value <<= 1; count++; }
        return count;
    }
}
"""

MONO_REPLACEMENTS = [
    ("\nnamespace Qtx;\n", "\n"),
    ("Math.Log2(", "QtxMath.Log2("),
    ("System.Numerics.BitOperations.PopCount(", "QtxMath.PopCount("),
    ("System.Numerics.BitOperations.LeadingZeroCount(", "QtxMath.LeadingZeroCount("),
    ("        if (System.Runtime.Intrinsics.X86.Bmi2.IsSupported)\n            return System.Runtime.Intrinsics.X86.Bmi2.ParallelBitExtract(value, 0x55555555u);\n", ""),
    ("using System.Runtime.Intrinsics;\nusing System.Runtime.Intrinsics.X86;\n", ""),
    ("        if (Avx2.IsSupported)\n            ScanCodebookByFirstRow(ref choice, stamp, codebookBase, lambda);\n        else\n            ScanCodebook(", "        ScanCodebook("),
    ("        if (Vector256.IsHardwareAccelerated)\n        {\n            return VectorPairError(first, second);\n        }\n\n", ""),
]

GATHER_SEARCH = re.compile(r"    private const int GatherCeiling = .*?(?=    private long SmallestRowError)", re.S)
VECTOR_PAIR_ERROR = re.compile(r"    private long VectorPairError\(.*?(?=    private void ConsiderPairs)", re.S)

PLUGINS = [
    ("windows/x86_64/qtx_decoder.dll", "Plugins/x86_64/qtx_decoder.dll"),
    ("linux/x86_64/libqtx_decoder.so", "Plugins/Linux/x86_64/libqtx_decoder.so"),
    ("macos/libqtx_decoder.dylib", "Plugins/macOS/libqtx_decoder.dylib"),
    ("ios/libqtx_decoder.a", "Plugins/iOS/libqtx_decoder.a"),
    ("android/arm64-v8a/libqtx_decoder.so", "Plugins/Android/libs/arm64-v8a/libqtx_decoder.so"),
    ("android/armeabi-v7a/libqtx_decoder.so", "Plugins/Android/libs/armeabi-v7a/libqtx_decoder.so"),
    ("android/x86_64/libqtx_decoder.so", "Plugins/Android/libs/x86_64/libqtx_decoder.so"),
    ("encoder/win-x64/qtx_encoder.dll", "Plugins/x86_64/qtx_encoder.dll"),
    ("encoder/win-x64/qtx_encoder_avx2.dll", "Plugins/x86_64/qtx_encoder_avx2.dll"),
    ("qtx_decoder.c", "Wasm~/QtxDecoder.c"),
]


def ported_for_mono(text):
    text = "\n" + text.replace("\r\n", "\n")
    for old, new in MONO_REPLACEMENTS:
        text = text.replace(old, new)
    text = GATHER_SEARCH.sub("", text)
    text = VECTOR_PAIR_ERROR.sub("", text)
    for forbidden in ("namespace Qtx", "Math.Log2(", "BitOperations", "Intrinsics", "CollectionsMarshal", "Vector256"):
        assert forbidden not in text.replace("QtxMath.Log2(", ""), forbidden
    return text


UNITY_PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
"""


def check_compiles_for_unity(ported):
    with tempfile.TemporaryDirectory() as folder:
        folder = Path(folder)
        (folder / "UnityCheck.csproj").write_text(UNITY_PROJECT, encoding="utf-8")
        for name, text in ported.items():
            (folder / name).write_text(text, encoding="utf-8")
        result = subprocess.run(["dotnet", "build", str(folder / "UnityCheck.csproj"), "-nologo", "-v", "quiet"], capture_output=True, text=True)
        if result.returncode != 0:
            raise SystemExit("the Mono copy of the encoder does not compile for .NET Standard 2.1 and C# 9, as Unity builds it:\n" + result.stdout + result.stderr)


def update_encoder(codec):
    sources = codec / "encoder/Qtx.Encoder"
    editor = PACKAGE / "Editor"
    image_encoder = ported_for_mono((sources / "QtxImageEncoder.cs").read_text(encoding="utf-8"))
    marker = "internal static class QtxBits"
    assert marker in image_encoder
    image_encoder = image_encoder.replace(marker, MONO_MATH.lstrip("\n") + "\n" + marker, 1)
    ported = {
        "QtxImageEncoder.cs": image_encoder,
        "QtxEncoder.cs": ported_for_mono((sources / "QtxEncoder.cs").read_text(encoding="utf-8")),
        "QtxRansEncoder.cs": ported_for_mono((sources / "QtxRansEncoder.cs").read_text(encoding="utf-8")),
        "QtxAdaptiveModel.cs": ported_for_mono((sources / "AdaptiveModel.cs").read_text(encoding="utf-8")),
        "QtxShapeCodebook.cs": ported_for_mono((sources / "ShapeCodebook.cs").read_text(encoding="utf-8")),
        "QtxShapeCodebookByFirstRow.cs": ported_for_mono((sources / "ShapeCodebookByFirstRow.cs").read_text(encoding="utf-8")),
    }
    check_compiles_for_unity(ported)
    for name, text in ported.items():
        (editor / name).write_text(text, encoding="utf-8")


def move_loaded_library_aside(path):
    aside = PROJECT / "Temp/replaced_plugins" / f"{path.name}.{time.time_ns()}"
    aside.parent.mkdir(parents=True, exist_ok=True)
    path.rename(aside)
    print(f"{path.relative_to(PROJECT).as_posix()} is loaded by the editor; moved it to {aside.relative_to(PROJECT).as_posix()}. Restart Unity to load the new library.")


def update_decoders(dist):
    for source, destination in PLUGINS:
        source_path = dist / source
        if not source_path.exists():
            raise SystemExit(f"{source_path} is missing: run build/build_decoder.py and build/build_encoder.py in the codec repository first")
        destination_path = PACKAGE / destination
        destination_path.parent.mkdir(parents=True, exist_ok=True)
        try:
            shutil.copy2(source_path, destination_path)
        except PermissionError:
            move_loaded_library_aside(destination_path)
            shutil.copy2(source_path, destination_path)
    plain = base64.b64encode((dist / "wasm/qtx_decoder.wasm").read_bytes()).decode("ascii")
    simd = base64.b64encode((dist / "wasm/qtx_decoder_simd.wasm").read_bytes()).decode("ascii")
    (PACKAGE / "Runtime/QtxDecoderWasm.jspre").write_text(f"var qtxDecoderWasmBase64 = '{plain}';\nvar qtxDecoderSimdWasmBase64 = '{simd}';\n", encoding="utf-8")


def update_warm_up_image(codec):
    command_line = next((codec / "tools/Qtx.Cli/bin/Release").glob("*/qtx.dll"), None)
    if command_line is None:
        raise SystemExit(f"{codec / 'tools/Qtx.Cli'} is not built: run dotnet build tools/Qtx.Cli -c Release in the codec repository first")
    with tempfile.TemporaryDirectory() as folder:
        encoded = Path(folder) / "warm_up.qtx"
        subprocess.run(["dotnet", str(command_line), "encode", str(WARM_UP_SOURCE), str(encoded)], check=True, stdout=subprocess.DEVNULL)
        image = base64.b64encode(encoded.read_bytes()).decode("ascii")
    worker = TRANSCODE_WORKER.read_text(encoding="utf-8")
    worker, replaced = re.subn(r"var QTX_WARM_UP_IMAGE_BASE64 = '[^']*';", f"var QTX_WARM_UP_IMAGE_BASE64 = '{image}';", worker)
    assert replaced == 1, TRANSCODE_WORKER
    with open(TRANSCODE_WORKER, "w", encoding="utf-8", newline="\n") as file:
        file.write(worker)


def main():
    parser = argparse.ArgumentParser(description="Copy decoder builds, the WebAssembly decoder and Mono-compatible encoder sources from the QTX codec repository into UniQTX")
    parser.add_argument("codec", type=Path, nargs="?", default=PROJECT.parent / "Qtx", help="path to the qtx repository (its dist/ must be built)")
    arguments = parser.parse_args()
    update_decoders(arguments.codec / "dist")
    update_encoder(arguments.codec)
    update_warm_up_image(arguments.codec)
    print(f"UniQTX updated from {arguments.codec}")


if __name__ == "__main__":
    sys.exit(main())
