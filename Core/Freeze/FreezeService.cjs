const { assertContract } = require('../Interfaces.cjs');

class FreezeService {
  constructor(memoryAccess, onEvent = () => {}) {
    this.memory = assertContract('IInt32Writer', memoryAccess);
    this.onEvent = onEvent;
    this.entries = new Map();
  }

  add(entry, intervalMs = 100) {
    if (!entry?.address) throw new Error('冻结项缺少地址。');
    this.remove(entry.address);
    const item = { address: entry.address, value: entry.value, intervalMs: normalizeInterval(intervalMs), timer: null };
    item.timer = setInterval(() => this.#apply(item), item.intervalMs);
    this.entries.set(item.address, item);
    this.#apply(item);
    this.onEvent({ type: 'freeze-started', address: item.address, value: item.value, intervalMs: item.intervalMs });
    return item;
  }

  remove(address) {
    const item = this.entries.get(address);
    if (!item) return false;
    clearInterval(item.timer);
    this.entries.delete(address);
    this.onEvent({ type: 'freeze-stopped', address });
    return true;
  }

  clear() {
    for (const address of [...this.entries.keys()]) this.remove(address);
  }

  list() { return [...this.entries.values()].map(({ timer, ...entry }) => entry); }

  async #apply(item) {
    try {
      await this.memory.writeInt32(item.address, Number(item.value));
      this.onEvent({ type: 'freeze-write', address: item.address, value: item.value });
    } catch (error) {
      this.onEvent({ type: 'freeze-error', address: item.address, message: error.message });
    }
  }
}

function normalizeInterval(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return 100;
  return Math.max(10, Math.min(1000, Math.round(number)));
}

module.exports = { FreezeService, normalizeInterval };
