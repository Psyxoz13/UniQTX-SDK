var QTX_WORKER_READY_TIMEOUT_MS = 10000;
var QTX_JOB_TIMEOUT_MS = 20000;
var QTX_WORKER_FAILURES_BEFORE_MAIN_THREAD = 3;
var QTX_WORKER_ATTEMPTS_PER_JOB = 2;
var QTX_JOBS_PER_WORKER = 2;
var QTX_WARM_UP_ROUNDS = 24;
var QTX_WARM_UP_IMAGE_BASE64 = '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';

function qtxDecoderBase64() {
  var simdProbe = new Uint8Array([0, 97, 115, 109, 1, 0, 0, 0, 1, 5, 1, 96, 0, 1, 123, 3, 2, 1, 0, 10, 10, 1, 8, 0, 65, 0, 253, 15, 253, 98, 11]);
  return typeof qtxDecoderSimdWasmBase64 === 'string' && WebAssembly.validate(simdProbe) ? qtxDecoderSimdWasmBase64 : qtxDecoderWasmBase64;
}

function qtxDecoderBytes(wasmBase64) {
  return Uint8Array.from(atob(wasmBase64), function (character) { return character.charCodeAt(0); });
}

function qtxCreateTranscoder(wasmSource) {
  var instance = typeof wasmSource === 'string'
    ? WebAssembly.instantiate(Uint8Array.from(atob(wasmSource), function (character) { return character.charCodeAt(0); }), {}).then(function (result) { return result['instance']; })
    : WebAssembly.instantiate(wasmSource, {});
  var ready = instance.then(function (result) { return result['exports']; });
  var spareOutputs = [], spareOutputLimit = 2, spareReleaseDelayMs = 1000, spareReleaseTimer = 0;

  function copyOutput(exports, at, length) {
    var view = new Uint8Array(exports['memory'].buffer, at, length);
    for (var index = 0; index < spareOutputs.length; index++) {
      if (spareOutputs[index].byteLength < length) continue;
      var output = new Uint8Array(spareOutputs.splice(index, 1)[0], 0, length);
      output.set(view);
      return output;
    }
    return view.slice();
  }
  function keepSpareOutput(buffer) {
    if (spareOutputs.length < spareOutputLimit) spareOutputs.push(buffer);
    clearTimeout(spareReleaseTimer);
    spareReleaseTimer = setTimeout(function () { spareOutputs.length = 0; }, spareReleaseDelayMs);
  }
  function allocate(exports, length) {
    if (!Number.isSafeInteger(length) || length < 1 || length > 0x7FFFFFFF)
      throw new Error('QTX buffer exceeds the WebAssembly size limit');
    var pointer = exports['allocate'](length);
    if (!pointer) throw new Error('WebAssembly memory exhausted');
    return pointer;
  }

  function levelsOf(bytes) {
    if (bytes.byteLength < 8 || bytes[0] !== 81 || bytes[1] !== 84 || bytes[2] !== 88 || bytes[3] !== 77)
      return [{ 'offset': 0, 'length': bytes.byteLength }];
    var count = bytes[4], offset = 8 + count * 4, levels = [];
    if (count < 1 || count > 16 || bytes.byteLength < offset) throw new Error('QTX mip container is corrupt');
    var view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    for (var level = 0; level < count; level++) {
      var length = view.getUint32(8 + level * 4, true);
      if (length > bytes.byteLength - offset) throw new Error('QTX mip container is truncated');
      levels.push({ 'offset': offset, 'length': length });
      offset += length;
    }
    return levels;
  }
  function decode(exports, bytes, target) {
    var buildMips = (target & 256) !== 0;
    target &= 255;
    var levels = levelsOf(bytes);
    function loadHeader(level) {
      if (level['length'] < 24) throw new Error('not an QTX file');
      new Uint8Array(exports['memory'].buffer).set(bytes.subarray(level['offset'], level['offset'] + 24), exports['qtx_header_buffer']());
    }
    loadHeader(levels[0]);
    if (!exports['qtx_prepare'](target)) throw new Error('QTX header or target ' + target + ' is not supported');
    if (buildMips && levels.length === 1) {
      var firstLength = exports['qtx_output_length'](target), chainLength = exports['qtx_mips_output_length'](target), workLength = exports['qtx_mips_work_length']();
      if (firstLength < 0 || chainLength < 0 || workLength < 0) throw new Error('QTX mip levels cannot be built for target ' + target);
      var chain = allocate(exports, chainLength), source = allocate(exports, bytes.byteLength), work = allocate(exports, workLength);
      new Uint8Array(exports['memory'].buffer).set(bytes, source);
      var stripeCount = exports['qtx_decode_begin'](source + levels[0]['offset'], levels[0]['length'], 0);
      if (!stripeCount) throw new Error('QTX data is corrupt or truncated');
      exports['qtx_set_mip_side'](work);
      if (!exports['qtx_decode_stripes'](target, chain, 0, stripeCount)) throw new Error('QTX data is corrupt or truncated');
      if (!exports['qtx_build_mips'](target, chain + firstLength)) throw new Error('QTX mip levels could not be built');
      return copyOutput(exports, chain, chainLength);
    }
    var outputLengths = levels.map(function (level) {
      loadHeader(level);
      var length = exports['qtx_output_length'](target);
      if (length < 0) throw new Error('QTX mip level header is not supported');
      return length;
    });
    var outputLength = outputLengths.reduce(function (sum, length) { return sum + length; }, 0);
    var output = allocate(exports, outputLength), file = allocate(exports, bytes.byteLength);
    new Uint8Array(exports['memory'].buffer).set(bytes, file);
    var at = output;
    for (var index = 0; index < levels.length; index++) {
      var stripes = exports['qtx_decode_begin'](file + levels[index]['offset'], levels[index]['length'], index > 0 ? 1 : 0);
      if (!stripes) throw new Error('QTX data is corrupt or truncated');
      if (!exports['qtx_decode_stripes'](target, at, 0, stripes)) throw new Error('QTX data is corrupt or truncated');
      at += outputLengths[index];
    }
    return copyOutput(exports, output, outputLength);
  }
  function transcode(exports, job) {
    var startedAt = performance.now();
    return { 'data': decode(exports, job['bytes'], job['format']), 'transcodeMs': performance.now() - startedAt };
  }

  return {
    'ready': ready,
    'transcode': function (job) {
      return ready.then(function (exports) { return transcode(exports, job); });
    },
    'keepSpareOutput': keepSpareOutput
  };
}

function qtxTranscodeWorkerMain(createTranscoder, warmUpBase64, warmUpRounds) {
  var transcoder = null;
  function warmUp() {
    var bytes = Uint8Array.from(atob(warmUpBase64), function (character) { return character.charCodeAt(0); });
    var targets = [1, 2, 4, 5, 257, 258, 260, 261];
    var chain = Promise.resolve();
    for (var round = 0; round < warmUpRounds; round++)
      chain = chain.then(transcoder['transcode'].bind(null, { 'bytes': bytes, 'format': targets[round % targets.length] }));
    return chain;
  }
  self.onmessage = function (event) {
    if (!transcoder) {
      transcoder = createTranscoder(event.data['decoder']);
      transcoder['ready'].then(warmUp).then(
        function () { self.postMessage({ 'ready': true }); },
        function (error) { self.postMessage({ 'ready': false, 'error': String(error) }); });
      return;
    }
    if (event.data['spareOutput']) {
      transcoder['keepSpareOutput'](event.data['spareOutput']);
      return;
    }
    var job = event.data;
    transcoder['transcode'](job).then(
      function (result) {
        self.postMessage({ 'id': job['id'], 'data': result['data'], 'transcodeMs': result['transcodeMs'] }, [result['data'].buffer]);
      },
      function (error) { self.postMessage({ 'id': job['id'], 'error': String(error) }); });
  };
}

var qtxTranscodePool = {
  workers: [],
  waitingJobs: [],
  mainThreadJobs: [],
  mainThreadBusy: false,
  mainThreadTranscoder: null,
  callbacks: {},
  nextJob: 0,
  workerUrl: null,
  workerCap: null,
  decoderModule: null,
  workerFailures: 0,
  workersBroken: false
};

function qtxTranscodeWorkerLimit() {
  var spareCores = Math.max(1, (navigator.hardwareConcurrency || 2) - 1);
  var memoryLimit = navigator.deviceMemory ? Math.max(1, Math.floor(navigator.deviceMemory * 2)) : spareCores;
  return Math.min(spareCores, memoryLimit);
}

function qtxAddTranscodeWorker() {
  var pool = qtxTranscodePool;
  if (pool.workerCap === null) pool.workerCap = qtxTranscodeWorkerLimit();
  if (pool.workersBroken || pool.workers.length >= pool.workerCap) return false;
  try {
    pool.workerUrl = pool.workerUrl || URL.createObjectURL(new Blob(
      ['(' + qtxTranscodeWorkerMain + ')(' + qtxCreateTranscoder + ', ' + JSON.stringify(QTX_WARM_UP_IMAGE_BASE64) + ', ' + QTX_WARM_UP_ROUNDS + ');'],
      { 'type': 'text/javascript' }));
    var worker = new Worker(pool.workerUrl);
    worker.ready = false;
    worker.jobs = [];
    worker.onmessage = qtxOnWorkerMessage;
    worker.onerror = function (event) {
      event.preventDefault();
      qtxOnWorkerFailure(worker, event.message ? 'crashed: ' + event.message : 'failed to load, blocked by the page security policy or the browser');
    };
    pool.workers.push(worker);
    qtxArmWorkerTimeout(worker, QTX_WORKER_READY_TIMEOUT_MS, 'did not start');
    qtxSendDecoder(worker);
    return true;
  } catch (error) {
    console.warn('QTX transcode worker could not be created', error);
    if (pool.workers.length === 0) pool.workersBroken = true;
    else pool.workerCap = pool.workers.length;
    return false;
  }
}

function qtxSendDecoder(worker) {
  var pool = qtxTranscodePool;
  pool.decoderModule = pool.decoderModule || WebAssembly.compile(qtxDecoderBytes(qtxDecoderBase64()));
  pool.decoderModule.then(
    function (module) {
      try {
        worker.postMessage({ 'decoder': module });
      } catch (error) {
        worker.postMessage({ 'decoder': qtxDecoderBase64() });
      }
    },
    function () { worker.postMessage({ 'decoder': qtxDecoderBase64() }); });
}

function qtxStartTranscodeWorkers() {
  var added = true;
  while (added) added = qtxAddTranscodeWorker();
}

function qtxTranscodeInWorker(bytes, format, onDone) {
  var pool = qtxTranscodePool;
  var id = ++pool.nextJob;
  pool.callbacks[id] = onDone;
  pool.waitingJobs.push({ 'id': id, 'bytes': bytes, 'format': format, attempts: 0 });
  qtxDispatchJobs();
}

function qtxDispatchJobs() {
  var pool = qtxTranscodePool;
  var worker;
  while (!pool.workersBroken && pool.waitingJobs.length > 0 && (worker = qtxLeastBusyWorker())) {
    var job = pool.waitingJobs.shift();
    worker.jobs.push(job);
    qtxArmWorkerTimeout(worker, QTX_JOB_TIMEOUT_MS, 'did not finish a ' + job['bytes'].length + ' byte texture in time');
    worker.postMessage(job);
  }

  var startingWorkers = pool.workers.filter(function (candidate) { return !candidate.ready; }).length;
  while (pool.waitingJobs.length > startingWorkers && qtxAddTranscodeWorker()) startingWorkers++;

  if (pool.workersBroken || pool.workers.length === 0) {
    while (pool.waitingJobs.length > 0) pool.mainThreadJobs.push(pool.waitingJobs.shift());
  }
  qtxRunMainThreadJobs();
}

function qtxLeastBusyWorker() {
  var best = null;
  qtxTranscodePool.workers.forEach(function (worker) {
    if (worker.ready && worker.jobs.length < QTX_JOBS_PER_WORKER && (!best || worker.jobs.length < best.jobs.length)) best = worker;
  });
  return best;
}

function qtxArmWorkerTimeout(worker, milliseconds, reason) {
  clearTimeout(worker.timeout);
  worker.timeout = setTimeout(function () { qtxOnWorkerFailure(worker, reason); }, milliseconds);
}

function qtxOnWorkerMessage(event) {
  var worker = event.target;
  var message = event.data;
  var pool = qtxTranscodePool;
  if (pool.workers.indexOf(worker) < 0) return;
  clearTimeout(worker.timeout);
  if (message['ready'] === false) {
    qtxOnWorkerFailure(worker, 'could not start the QTX decoder: ' + message['error']);
    return;
  }

  if (message['ready'] === true) {
    worker.ready = true;
    qtxDispatchJobs();
    return;
  }

  var index = -1;
  worker.jobs.forEach(function (candidate, at) { if (candidate['id'] === message['id']) index = at; });
  if (index < 0) return;
  var job = worker.jobs.splice(index, 1)[0];
  if (worker.jobs.length > 0)
    qtxArmWorkerTimeout(worker, QTX_JOB_TIMEOUT_MS, 'did not finish a ' + worker.jobs[0]['bytes'].length + ' byte texture in time');
  else if (pool.workersBroken)
    qtxRemoveWorker(worker);
  qtxDispatchJobs();
  qtxFinishJob(job, message['error'] ? null : message, message['error']);
  if (message['data'])
    worker.postMessage({ 'spareOutput': message['data'].buffer }, [message['data'].buffer]);
}

function qtxRemoveWorker(worker) {
  var pool = qtxTranscodePool;
  clearTimeout(worker.timeout);
  worker.terminate();
  pool.workers.splice(pool.workers.indexOf(worker), 1);
}

function qtxOnWorkerFailure(worker, reason) {
  var pool = qtxTranscodePool;
  if (pool.workers.indexOf(worker) < 0) return;
  var wasBroken = pool.workersBroken;
  qtxRemoveWorker(worker);
  pool.workerFailures++;
  pool.workerCap = Math.max(1, pool.workers.length);
  if (!worker.ready || pool.workerFailures >= QTX_WORKER_FAILURES_BEFORE_MAIN_THREAD) {
    pool.workersBroken = true;
    pool.workers.filter(function (other) { return other.jobs.length === 0; }).forEach(qtxRemoveWorker);
  }
  if (!wasBroken) console.warn('QTX transcode worker ' + reason + (pool.workersBroken ? ', transcoding continues on the main thread' : ', retrying on another worker'));

  worker.jobs.slice().reverse().forEach(function (job) {
    job.attempts++;
    if (job.attempts >= QTX_WORKER_ATTEMPTS_PER_JOB) pool.mainThreadJobs.push(job);
    else pool.waitingJobs.unshift(job);
  });
  worker.jobs = [];
  qtxDispatchJobs();
}

function qtxRunMainThreadJobs() {
  var pool = qtxTranscodePool;
  if (pool.mainThreadBusy || pool.mainThreadJobs.length === 0) return;
  pool.mainThreadBusy = true;
  pool.mainThreadTranscoder = pool.mainThreadTranscoder || qtxCreateTranscoder(qtxDecoderBase64());
  var job = pool.mainThreadJobs.shift();
  function runNext() {
    pool.mainThreadBusy = false;
    qtxRunMainThreadJobs();
  }

  setTimeout(function () {
    pool.mainThreadTranscoder['transcode'](job)
      .then(
        function (result) { qtxFinishJob(job, result, null); },
        function (error) { qtxFinishJob(job, null, String(error)); })
      .then(runNext, runNext);
  }, 0);
}

function qtxFinishJob(job, result, error) {
  var callback = qtxTranscodePool.callbacks[job['id']];
  if (!callback) return;
  delete qtxTranscodePool.callbacks[job['id']];
  callback(result ? result['data'] : null, error, result ? result['transcodeMs'] : 0);
}
