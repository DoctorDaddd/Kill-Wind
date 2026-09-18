const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { NativeMemoryBridge } = require('../Core/Memory/NativeMemoryBridge.cjs');
const { ProcessService } = require('../Core/Processes/ProcessService.cjs');
const { MemoryAccess } = require('../Core/Memory/MemoryAccess.cjs');
const { Int32Scanner } = require('../Core/Scanner/Int32Scanner.cjs');
const { FreezeService } = require('../Core/Freeze/FreezeService.cjs');

test('native bridge attaches, scans, reads, and writes MemoryTestGame', async (t) => {
  const gamePath = path.join(__dirname, '..', 'MemoryTestGame', 'MemoryTestGame.exe');
  const game = spawn(gamePath, [], { windowsHide: true });
  const bridge = new NativeMemoryBridge(path.join(__dirname, '..', 'native', 'MemoryBridge.exe'));
  const service = new ProcessService(bridge);
  const access = new MemoryAccess(bridge, service);
  let freezer;
  try {
    let processInfo;
    for (let attempt = 0; attempt < 20 && !processInfo; attempt += 1) {
      await new Promise((resolve) => setTimeout(resolve, 100));
      const processes = await bridge.listProcesses();
      processInfo = processes.find((item) => item.pid === game.pid || item.name.toLowerCase() === 'memorytestgame.exe');
    }
    assert.ok(processInfo, 'MemoryTestGame should appear in the process list');
    await service.attach(processInfo);
    const regions = await service.regions();
    assert.ok(regions.length > 0);
    const modules = await bridge.listModules(processInfo.pid);
    assert.ok(modules.some((item) => item.name.toLowerCase() === 'memorytestgame.exe'));
    assert.ok(modules.every((item) => /^0x[0-9a-f]+$/i.test(item.baseAddress)));
    const scanner = new Int32Scanner(access, service);
    const session = await scanner.firstExact(12345, { regions, chunkSize: 64 * 1024 });
    assert.ok(session.results.length > 0, 'Gold value should be found');
    const address = session.results[0].address;
    assert.equal(await access.readInt32(address), 12345);
    await access.writeInt32(address, 99999);
    assert.equal(await access.readInt32(address), 99999);
    freezer = new FreezeService(access);
    freezer.add({ address, value: 777 }, 10);
    await new Promise((resolve) => setTimeout(resolve, 35));
    assert.equal(await access.readInt32(address), 777);
  } finally {
    freezer?.clear();
    bridge.stop();
    if (!game.killed) game.kill();
  }
});
