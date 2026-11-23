using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.ResourceProviders;

public class QtxBundledAssetProvider : BundledAssetProvider
{
    private static readonly Dictionary<AssetBundle, Task> FillingBundles = new();
    private readonly List<object> dependencies = new();

    public override async void Provide(ProvideHandle provideHandle)
    {
        provideHandle.SetWaitForCompletionCallback(ThrowSynchronousLoadNotSupported);
        try
        {
            await FillTexturesOfDependencies(provideHandle);
        }
        catch (Exception exception)
        {
            provideHandle.Complete<object>(null, false, exception);
            return;
        }

        base.Provide(provideHandle);
    }

    private static bool ThrowSynchronousLoadNotSupported() =>
        throw new NotSupportedException("QTX images are still transcoding, load them asynchronously instead of WaitForCompletion");

    private Task FillTexturesOfDependencies(ProvideHandle provideHandle)
    {
        ForgetUnloadedBundles();
        dependencies.Clear();
        provideHandle.GetDependencies(dependencies);
        return Task.WhenAll(dependencies.OfType<IAssetBundleResource>().Select(resource => FillTexturesOf(resource.GetAssetBundle())));
    }

    private Task FillTexturesOf(AssetBundle bundle)
    {
        if (!bundle || bundle.isStreamedSceneAssetBundle)
            return Task.CompletedTask;

        if (!FillingBundles.TryGetValue(bundle, out var filling))
            FillingBundles[bundle] = filling = Task.WhenAll(TranscodeImagesIn(bundle));
        return filling;
    }

    protected virtual IEnumerable<Task> TranscodeImagesIn(AssetBundle bundle)
    {
        return bundle.LoadAllAssets<QtxImage>().Select(TranscodeOrLogFailure);
    }

    private static async Task TranscodeOrLogFailure(QtxImage image)
    {
        try
        {
            await image.TranscodeIntoTextureAsync();
        }
        catch (Exception exception)
        {
            Debug.LogError($"QTX image '{image.name}' failed to transcode and stays empty: {exception.Message}", image);
        }
    }

    private static void ForgetUnloadedBundles()
    {
        foreach (var bundle in FillingBundles.Keys.Where(bundle => !bundle).ToArray())
            FillingBundles.Remove(bundle);
    }
}
