/**
 * Runtime-checked contracts keep the Core layer independent from Electron.
 * A future CLI, WPF shell, or plugin can provide another implementation as
 * long as it satisfies these method shapes.
 */
const CONTRACTS = {
  IProcessBridge: ['listProcesses', 'listRegions', 'listModules', 'read', 'write'],
  IProcessService: ['list', 'attach', 'detach', 'ensureAttached', 'regions'],
  IScanProcessService: ['ensureAttached', 'regions'],
  IMemoryReader: ['read'],
  IMemoryWriter: ['write'],
  IInt32Writer: ['writeInt32'],
  IMemoryScanner: ['firstExact', 'nextExact'],
  IPointerResolver: ['resolve'],
  ISignatureScanner: ['scan'],
  IFreezeService: ['add', 'remove', 'clear', 'list'],
};

function assertContract(name, value) {
  const methods = CONTRACTS[name];
  if (!methods) throw new Error(`未知接口契约：${name}`);
  if (!value || methods.some((method) => typeof value[method] !== 'function')) {
    throw new TypeError(`${name} 实现不完整，需要：${methods.join(', ')}`);
  }
  return value;
}

module.exports = { CONTRACTS, assertContract };
