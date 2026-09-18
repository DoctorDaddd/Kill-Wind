const { assertContract } = require('../Interfaces.cjs');

/** @interface IProcessService */
class ProcessService {
  constructor(bridge) {
    this.bridge = assertContract('IProcessBridge', bridge);
    this.attached = null;
  }

  async list() {
    return this.bridge.listProcesses();
  }

  async attach(processInfo) {
    const processes = await this.list();
    const current = processes.find((item) => item.pid === processInfo.pid);
    if (!current) throw new Error('目标进程已退出，请刷新进程列表。');
    await this.bridge.listRegions(current.pid);
    this.attached = current;
    return current;
  }

  detach() {
    const previous = this.attached;
    this.attached = null;
    return previous;
  }

  async ensureAttached({ verify = true } = {}) {
    if (!this.attached) throw new Error('请先连接目标进程。');
    if (!verify) return this.attached;
    const processes = await this.list();
    const current = processes.find((item) => item.pid === this.attached.pid);
    if (!current) {
      this.attached = null;
      throw new Error('目标进程已退出。');
    }
    this.attached = current;
    return current;
  }

  async regions(options = {}) {
    const process = await this.ensureAttached();
    return this.bridge.listRegions(process.pid, options);
  }
}

module.exports = { ProcessService };
