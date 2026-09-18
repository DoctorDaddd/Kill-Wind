const { assertContract } = require('../Interfaces.cjs');

class MemoryAccess {
  constructor(bridge, processService) {
    this.bridge = assertContract('IMemoryReader', assertContract('IMemoryWriter', bridge));
    this.processService = assertContract('IProcessService', processService);
  }

  async read(pid, address, size) {
    return this.bridge.read(pid, address, size);
  }

  async write(pid, address, bytes) {
    const process = await this.processService.ensureAttached({ verify: false });
    if (process.pid !== pid) throw new Error('写入目标与当前连接进程不一致。');
    return this.bridge.write(pid, address, bytes);
  }

  async readInt32(address) {
    const process = await this.processService.ensureAttached({ verify: false });
    const bytes = await this.read(process.pid, address, 4);
    if (bytes.length !== 4) throw new Error('读取 Int32 时返回长度不完整。');
    return bytes.readInt32LE(0);
  }

  async writeInt32(address, value) {
    const process = await this.processService.ensureAttached({ verify: false });
    if (!Number.isInteger(value) || value < -2147483648 || value > 2147483647) throw new Error('Int32 数值超出范围。');
    const bytes = Buffer.allocUnsafe(4);
    bytes.writeInt32LE(value, 0);
    await this.write(process.pid, address, bytes);
    return value;
  }
}

module.exports = { MemoryAccess };
