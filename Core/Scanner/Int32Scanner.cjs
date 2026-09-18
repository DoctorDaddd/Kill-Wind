const DEFAULT_CHUNK_SIZE = 1024 * 1024;
const { assertContract } = require('../Interfaces.cjs');

class Int32Scanner {
  constructor(memoryAccess, processService) {
    this.memory = assertContract('IMemoryReader', memoryAccess);
    this.processService = assertContract('IScanProcessService', processService);
  }

  async firstExact(value, options = {}, onProgress = () => {}, signal) {
    const process = await this.processService.ensureAttached();
    const regions = options.regions || await this.processService.regions(options);
    const candidates = [];
    const started = Date.now();
    const totalBytes = regions.reduce((sum, region) => sum + Number(region.size || 0), 0);
    let scannedBytes = 0;
    for (const region of regions) {
      this.#throwIfAborted(signal);
      const base = parseAddress(region.baseAddress);
      const size = Number(region.size);
      for (let offset = 0; offset < size; offset += (options.chunkSize || DEFAULT_CHUNK_SIZE)) {
        this.#throwIfAborted(signal);
        const length = Math.min(options.chunkSize || DEFAULT_CHUNK_SIZE, size - offset);
        try {
          const bytes = await this.memory.read(process.pid, toAddress(base + BigInt(offset)), length);
          for (let index = 0; index + 4 <= bytes.length; index += 4) {
            if (bytes.readInt32LE(index) === value) candidates.push({
              address: toAddress(base + BigInt(offset + index)),
              value,
              type: 'Int32',
              module: region.type,
            });
          }
          scannedBytes += length;
        } catch (error) {
          // A committed region can disappear while a game allocates or unloads data.
          // Skip that region and keep the session alive; the UI receives the warning.
          onProgress({ phase: 'warning', message: `跳过不可读区域 ${region.baseAddress}：${error.message}` });
          scannedBytes += length;
        }
        onProgress({ phase: 'scanning', scannedBytes, totalBytes, resultCount: candidates.length,
          elapsedMs: Date.now() - started });
        await new Promise((resolve) => setImmediate(resolve));
      }
    }
    return this.#session({ process, regions, candidates, value, mode: 'Exact Value', started, scannedBytes, totalBytes });
  }

  async nextExact(session, value, onProgress = () => {}, signal) {
    const process = await this.processService.ensureAttached();
    if (process.pid !== session.process.pid) throw new Error('扫描会话与当前进程不一致。');
    const previous = session.results || [];
    const candidates = [];
    const started = Date.now();
    const groups = groupResults(previous, session.options?.chunkSize || DEFAULT_CHUNK_SIZE);
    let scannedBytes = 0;
    for (const group of groups) {
      this.#throwIfAborted(signal);
      try {
        const bytes = await this.memory.read(process.pid, group.address, group.size);
        for (const item of group.items) {
          const offset = Number(parseAddress(item.address) - parseAddress(group.address));
          if (offset >= 0 && offset + 4 <= bytes.length && bytes.readInt32LE(offset) === value) {
            candidates.push({ ...item, value });
          }
        }
        scannedBytes += group.size;
      } catch (error) {
        onProgress({ phase: 'warning', message: `跳过候选区域 ${group.address}：${error.message}` });
      }
      onProgress({ phase: 'scanning', scannedBytes, totalBytes: groups.reduce((sum, item) => sum + item.size, 0),
        resultCount: candidates.length, elapsedMs: Date.now() - started });
      await new Promise((resolve) => setImmediate(resolve));
    }
    return this.#session({ process, regions: session.regions, candidates, value, mode: 'Exact Value', started, scannedBytes,
      totalBytes: scannedBytes, previousCount: previous.length, options: session.options });
  }

  #session({ process, regions, candidates, value, mode, started, scannedBytes, totalBytes, previousCount = 0, options = {} }) {
    return { process, regions, results: candidates, value, mode, options, previousCount,
      scannedBytes, totalBytes, elapsedMs: Date.now() - started, createdAt: new Date().toISOString() };
  }

  #throwIfAborted(signal) {
    if (signal?.aborted) throw new Error('扫描已取消。');
  }
}

function parseAddress(value) {
  const text = String(value).replace(/^0x/i, '');
  return BigInt(`0x${text}`);
}

function toAddress(value) { return `0x${value.toString(16).toUpperCase()}`; }

function groupResults(results, chunkSize) {
  if (!results.length) return [];
  const sorted = [...results].sort((a, b) => parseAddress(a.address) < parseAddress(b.address) ? -1 : 1);
  const groups = [];
  let current = null;
  for (const item of sorted) {
    const address = parseAddress(item.address);
    if (!current || address - parseAddress(current.address) + 4n > BigInt(chunkSize)) {
      current = { address: item.address, end: address + 4n, items: [item] };
      groups.push(current);
    } else {
      current.end = address + 4n > current.end ? address + 4n : current.end;
      current.items.push(item);
    }
  }
  return groups.map((group) => ({ address: group.address, size: Number(group.end - parseAddress(group.address)), items: group.items }));
}

module.exports = { Int32Scanner, parseAddress, toAddress, groupResults };
