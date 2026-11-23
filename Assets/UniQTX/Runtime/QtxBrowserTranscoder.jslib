mergeInto(LibraryManager.library, {
  QtxTranscodeInWorker: function (bytesPtr, length, target, requestId, onTranscoded, onCopied) {
    function reportFailure(error) {
      var message = String(error);
      var size = lengthBytesUTF8(message) + 1;
      var messagePointer = _malloc(size);
      stringToUTF8(message, messagePointer, size);
      try {
        {{{ makeDynCall('viii', 'onCopied') }}}(requestId, messagePointer, 0);
      } finally {
        _free(messagePointer);
      }
    }

    try {
      var bytes = HEAPU8.slice(bytesPtr, bytesPtr + length);
      qtxTranscodeInWorker(bytes, target, function (data, error, transcodeMs) {
        if (!data) {
          reportFailure(error);
          return;
        }

        var destination = {{{ makeDynCall('iiii', 'onTranscoded') }}}(requestId, data.length, Math.round(transcodeMs * 1000));
        if (!destination)
          return;

        var copyStartedAt = performance.now();
        try {
          HEAPU8.set(data, destination);
        } catch (copyError) {
          reportFailure(copyError);
          return;
        }
        {{{ makeDynCall('viii', 'onCopied') }}}(requestId, 0, Math.round((performance.now() - copyStartedAt) * 1000));
      });
    } catch (error) {
      reportFailure(error);
    }
  },

  QtxStartTranscodeWorkers: function () {
    qtxStartTranscodeWorkers();
  },

  QtxTranscodeWorkerCount: function () {
    return qtxTranscodePool.workers.length;
  }
});
