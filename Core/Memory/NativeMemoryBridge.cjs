const { EventEmitter } = require('node:events');
const { spawn } = require('node:child_process');
const path = require('node:path');
const crypto = require('node:crypto');

/**
 * Small request/response transport around the x64 Windows native bridge.
 * The rest of the application only sees these narrow memory operations.
 */
class NativeMemoryBridge extends EventEmitter {
  constructor(helperPath = path.join(__dirname, '..', '..', 'native', 'MemoryBridge.exe')) {
    super();
    this.helperPath = helperPath;
    this.child = null;
    this.buffer = '';
    this.pending = new Map();
  }

  start() {
    if (this.child && !this.child.killed) return;
    this.child = spawn(this.helperPath, [], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
    this.child.stdout.setEncoding('utf8');
    this.child.stdout.on('data', (chunk) => this.#onStdout(chunk));
    this.child.stderr.on('data', (chunk) => this.emit('diagnostic', String(chunk).trim()));
    this.child.on('error', (error) => this.#failAll(error));
    this.child.on('exit', (code, signal) => {
      this.#failAll(new Error(`原生内存桥接已退出（${code ?? signal ?? 'unknown'}）。`));
      this.child = null;
      this.emit('exit', { code, signal });
    });
  }

  stop() {
    if (!this.child) return;
    this.child.kill();
    this.child = null;
  }

  async call(op, payload = {}) {
    this.start();
    if (!this.child?.stdin?.writable) throw new Error('原生内存桥接不可用。');
    const id = crypto.randomUUID();
    const request = JSON.stringify({ id, op, ...payload });
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.child.stdin.write(`${request}\n`, (error) => {
        if (!error) return;
        this.pending.delete(id);
        reject(error);
      });
    });
  }

  async listProcesses(options = {}) {
    return this.call('list', options);
  }

  async listRegions(pid, options = {}) {
    return this.call('regions', { pid, ...options });
  }

  async read(pid, address, size) {
    const base64 = await this.call('read', { pid, address, size });
    return Buffer.from(base64, 'base64');
  }

  async write(pid, address, bytes) {
    return this.call('write', { pid, address, data: Buffer.from(bytes).toString('base64') });
  }

  async pickScreenPoint(pid, timeoutMs = 30000) {
    return this.call('pick', { pid, size: timeoutMs });
  }

  #onStdout(chunk) {
    this.buffer += chunk;
    let end;
    while ((end = this.buffer.indexOf('\n')) >= 0) {
      const line = this.buffer.slice(0, end).trim();
      this.buffer = this.buffer.slice(end + 1);
      if (!line) continue;
      let response;
      try { response = JSON.parse(line); } catch (error) {
        this.emit('diagnostic', `桥接返回无效 JSON：${error.message}`);
        continue;
      }
      const pending = this.pending.get(response.id);
      if (!pending) continue;
      this.pending.delete(response.id);
      if (!response.ok) pending.reject(new Error(response.error || '原生操作失败。'));
      else {
        try { pending.resolve(response.data === null ? null : JSON.parse(response.data)); }
        catch (error) { pending.reject(new Error(`桥接响应解析失败：${error.message}`)); }
      }
    }
  }

  #failAll(error) {
    for (const { reject } of this.pending.values()) reject(error);
    this.pending.clear();
  }
}

module.exports = { NativeMemoryBridge };
