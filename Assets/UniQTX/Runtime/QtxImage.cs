using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public readonly struct QtxTranscodeTimings
{
    public readonly double TranscodeMilliseconds;
    public readonly double AllocateMilliseconds;
    public readonly double CopyMilliseconds;
    public readonly double UploadMilliseconds;
    public readonly double ReceivedAt;

    public QtxTranscodeTimings(double transcodeMilliseconds, double allocateMilliseconds, double copyMilliseconds, double uploadMilliseconds, double receivedAt)
    {
        TranscodeMilliseconds = transcodeMilliseconds;
        AllocateMilliseconds = allocateMilliseconds;
        CopyMilliseconds = copyMilliseconds;
        UploadMilliseconds = uploadMilliseconds;
        ReceivedAt = receivedAt;
    }
}

public readonly struct QtxTranscodeReport
{
    public readonly string ImageName;
    public readonly QtxTranscodeTimings Timings;
    public readonly double StartedAt;

    public QtxTranscodeReport(string imageName, QtxTranscodeTimings timings, double startedAt)
    {
        ImageName = imageName;
        Timings = timings;
        StartedAt = startedAt;
    }
}

[PreferBinarySerialization]
public class QtxImage : ScriptableObject
{
    [SerializeField] private byte[] bytes;
    [SerializeField] private Texture2D texture;
    [SerializeField] private int width;
    [SerializeField] private int height;
    [SerializeField] private bool compressAfterLoad;
    [SerializeField] private int mipCount = 1;
    [SerializeField] private bool mipsAtLoad;

    private Task transcodeTask;

    public static event Action<QtxTranscodeReport> Transcoded;

    public static TextureFormat? PreferredCompressedFormat { get; set; }

#if !UNITY_WEBGL || UNITY_EDITOR
    private static readonly SemaphoreSlim NativeDecodeSlots = new(Math.Max(1, Math.Min(8, Environment.ProcessorCount - 1)));
#endif

    public static QtxImage Create(byte[] bytes, Texture2D texture, int width, int height, int mipCount, bool compressAfterLoad, bool mipsAtLoad)
    {
        var image = CreateInstance<QtxImage>();
        image.bytes = bytes;
        image.texture = texture;
        image.width = width;
        image.height = height;
        image.compressAfterLoad = compressAfterLoad;
        image.mipCount = mipCount;
        image.mipsAtLoad = mipsAtLoad;
        return image;
    }

    public static int TranscodeWorkerCount
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return QtxBrowserTranscoder.WorkerCount;
#else
            return 0;
#endif
        }
    }

    public static void WarmUpTranscoders()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        QtxBrowserTranscoder.StartWorkers();
#endif
    }

    public Task TranscodeIntoTextureAsync()
    {
        if (transcodeTask != null && !transcodeTask.IsFaulted && !transcodeTask.IsCanceled)
            return transcodeTask;

        var retry = transcodeTask != null;
        var completion = new TaskCompletionSource<object>();
        // Publish before starting so synchronous callbacks share this attempt.
        transcodeTask = completion.Task;
        TranscodeIntoTexture(completion, retry);
        return completion.Task;
    }

    private async void TranscodeIntoTexture(TaskCompletionSource<object> completion, bool retry)
    {
        try
        {
            if (!retry && texture.width == width && texture.height == height)
            {
                completion.SetResult(null);
                return;
            }

            var startedAt = Time.realtimeSinceStartupAsDouble;
            var header = new QtxHeader(bytes);
            var target = compressAfterLoad ? QtxTargetFormat.SupportedBy(SystemInfo.SupportsTextureFormat, header, PreferredCompressedFormat) : QtxTargetFormat.Of(TextureFormat.RGBA32);
#if UNITY_WEBGL && !UNITY_EDITOR
            var targetWithFlags = (int)target.Target | (mipsAtLoad ? QtxBrowserTranscoder.BuildMipsFlag : 0);
            var timings = await QtxBrowserTranscoder.TranscodeAsync(bytes, targetWithFlags, length => AllocateTextureMemory(target.TextureFormat, length), UploadTexture);
#else
            await NativeDecodeSlots.WaitAsync();
            QtxTranscodeTimings timings;
            try
            {
                var length = mipsAtLoad ? header.OutputLengthWithBuiltMips(target.Target) : header.OutputLength(target.Target);
                var stopwatch = Stopwatch.StartNew();
                var destination = AllocateTextureMemory(target.TextureFormat, length);
                var allocateMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                var transcodeMilliseconds = await Task.Run(() => DecodeInto(header, target.Target, destination, length));
                var receivedAt = Time.realtimeSinceStartupAsDouble;
                stopwatch.Restart();
                UploadTexture();
                timings = new QtxTranscodeTimings(transcodeMilliseconds, allocateMilliseconds, 0, stopwatch.Elapsed.TotalMilliseconds, receivedAt);
            }
            finally
            {
                NativeDecodeSlots.Release();
            }
#endif
            Transcoded?.Invoke(new QtxTranscodeReport(name, timings, startedAt));
            completion.SetResult(null);
        }
        catch (OperationCanceledException)
        {
            completion.SetCanceled();
        }
        catch (Exception exception)
        {
            completion.SetException(exception);
        }
    }

    private unsafe IntPtr AllocateTextureMemory(TextureFormat format, int length)
    {
        texture.Reinitialize(width, height, format, mipCount > 1 || mipsAtLoad);
        var memory = texture.GetRawTextureData<byte>();
        if (memory.Length != length)
            throw new InvalidOperationException($"QTX '{name}' decodes to {length} bytes, {format} {width}x{height} with {mipCount} mip levels needs {memory.Length}");
        return (IntPtr)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(memory);
    }

    private void UploadTexture()
    {
        texture.Apply(false, true);
    }


#if !UNITY_WEBGL || UNITY_EDITOR
    private unsafe double DecodeInto(QtxHeader header, QtxTarget target, IntPtr destination, int length)
    {
        var stopwatch = Stopwatch.StartNew();
        QtxDecoder.Decode(bytes, header, target, new Span<byte>((void*)destination, length), mipsAtLoad);
        return stopwatch.Elapsed.TotalMilliseconds;
    }
#endif
}
