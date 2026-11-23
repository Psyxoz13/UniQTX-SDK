# UniQTX

Unity integration for [QTX](https://github.com/Psyxoz13/QTX-Codec). The importer stores images as compact QTX data, and the runtime chooses a supported GPU format. An Addressables provider transcodes the textures in a loaded bundle before returning assets. The WebGL path uses Web Workers.

## Install

In Unity 6, open Package Manager, select **Install package from git URL**, and enter:

    https://github.com/Psyxoz13/UniQTX-SDK.git?path=/Assets/UniQTX

Select **QTX Image** as the importer for a PNG or JPEG. Pick **After Load** to transcode on the target device or **At Import** to store the platform texture at import time. For Addressables, select the QTX content builder when building bundles.


## License

[PolyForm Noncommercial License 1.0.0](LICENSE.md).
