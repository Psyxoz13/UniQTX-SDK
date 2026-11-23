#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AOT;
using UnityEngine;

public static class QtxBrowserTranscoder
{
    private delegate int TranscodedCallback(int requestId, int dataLength, int transcodeMicroseconds);
    private delegate void CopiedCallback(int requestId, int errorPointer, int copyMicroseconds);

    private class PendingTranscode
    {
        public TaskCompletionSource<QtxTranscodeTimings> Completion;
        public Func<int, IntPtr> AllocateTexture;
        public Action UploadTexture;
        public double TranscodeMilliseconds;
        public double AllocateMilliseconds;
        public double ReceivedAt;
    }

    private static readonly Dictionary<int, PendingTranscode> PendingTranscodes = new();
    private static int lastRequestId;

    [DllImport("__Internal")]
    private static extern void QtxTranscodeInWorker(byte[] bytes, int length, int target, int requestId, TranscodedCallback onTranscoded, CopiedCallback onCopied);

    [DllImport("__Internal")]
    private static extern void QtxStartTranscodeWorkers();

    [DllImport("__Internal")]
    private static extern int QtxTranscodeWorkerCount();

    public static int WorkerCount => QtxTranscodeWorkerCount();

    public const int BuildMipsFlag = 256;

    public static void StartWorkers()
    {
        QtxStartTranscodeWorkers();
    }

    public static Task<QtxTranscodeTimings> TranscodeAsync(byte[] bytes, int target, Func<int, IntPtr> allocateTexture, Action uploadTexture)
    {
        var requestId = ++lastRequestId;
        var completion = new TaskCompletionSource<QtxTranscodeTimings>(TaskCreationOptions.RunContinuationsAsynchronously);
        PendingTranscodes.Add(requestId, new PendingTranscode { Completion = completion, AllocateTexture = allocateTexture, UploadTexture = uploadTexture });
        QtxTranscodeInWorker(bytes, bytes.Length, target, requestId, OnTranscoded, OnCopied);
        return completion.Task;
    }

    [MonoPInvokeCallback(typeof(TranscodedCallback))]
    private static int OnTranscoded(int requestId, int dataLength, int transcodeMicroseconds)
    {
        var pending = PendingTranscodes[requestId];
        pending.ReceivedAt = Time.realtimeSinceStartupAsDouble;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var destination = pending.AllocateTexture(dataLength);
            pending.AllocateMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            pending.TranscodeMilliseconds = transcodeMicroseconds / 1000.0;
            return (int)destination;
        }
        catch (Exception exception)
        {
            PendingTranscodes.Remove(requestId);
            pending.Completion.SetException(exception);
            return 0;
        }
    }

    [MonoPInvokeCallback(typeof(CopiedCallback))]
    private static void OnCopied(int requestId, int errorPointer, int copyMicroseconds)
    {
        if (!PendingTranscodes.Remove(requestId, out var pending))
            return;

        if (errorPointer != 0)
        {
            pending.Completion.SetException(new Exception("Browser failed to transcode QTX: " + Marshal.PtrToStringUTF8((IntPtr)errorPointer)));
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            pending.UploadTexture();
            pending.Completion.SetResult(new QtxTranscodeTimings(pending.TranscodeMilliseconds, pending.AllocateMilliseconds, copyMicroseconds / 1000.0, stopwatch.Elapsed.TotalMilliseconds, pending.ReceivedAt));
        }
        catch (Exception exception)
        {
            pending.Completion.SetException(exception);
        }
    }
}
#endif
