using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class QtxWasmBuilder
{
    private const string SourcePath = "Wasm~/QtxDecoder.c";
    private const string OutputPath = "Runtime/QtxDecoderWasm.jspre";
    private const string ClangArguments = "--target=wasm32-unknown-emscripten -mcpu=generic -mbulk-memory -mnontrapping-fptoint -msign-ext -O3 -DNDEBUG -Wall -Wextra -nostdlib -Wl,--no-entry -Wl,-z,stack-size=65536";

    [MenuItem("Tools/UniQTX/Rebuild Wasm Decoder")]
    public static void Rebuild()
    {
        var emscripten = Path.Combine(BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.WebGL, BuildOptions.None), "BuildTools/Emscripten");
        var llvm = Environment.GetEnvironmentVariable("QTX_WASM_LLVM") ?? Path.Combine(emscripten, "llvm");
        var clang = Path.Combine(llvm, Application.platform == RuntimePlatform.WindowsEditor ? "clang.exe" : "clang");
        var wasmPath = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject() + ".wasm");
        try
        {
            var plain = Compile(clang, wasmPath, "");
            var simd = Compile(clang, wasmPath, "-msimd128");
            File.WriteAllText(QtxPackageFolder.GetFilePath(OutputPath), $"var qtxDecoderWasmBase64 = '{plain}';\nvar qtxDecoderSimdWasmBase64 = '{simd}';\n");
            AssetDatabase.ImportAsset(QtxPackageFolder.GetAssetPath(OutputPath));
        }
        finally
        {
            File.Delete(wasmPath);
        }
    }

    private static string Compile(string clang, string wasmPath, string features)
    {
        var startInfo = new ProcessStartInfo(clang, $"{ClangArguments} {features} -o \"{wasmPath}\" \"{Path.GetFullPath(QtxPackageFolder.GetFilePath(SourcePath))}\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        var compilerOutput = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"clang failed for {SourcePath}:\n{compilerOutput}");
        if (compilerOutput.Length > 0)
            UnityEngine.Debug.LogWarning($"clang warnings for {SourcePath}:\n{compilerOutput}");

        return Convert.ToBase64String(File.ReadAllBytes(wasmPath));
    }
}
