const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { Int32Scanner, groupResults } = require('../Core/Scanner/Int32Scanner.cjs');
const { FreezeService, normalizeInterval } = require('../Core/Freeze/FreezeService.cjs');
const { ProfileStore } = require('../Profiles/ProfileStore.cjs');

test('Int32 first and next scans work through a chunked reader', async () => {
  const regions = [{ baseAddress: '0x1000', size: 32, type: 'Private' }];
  let memory = Buffer.alloc(32);
  memory.writeInt32LE(12345, 4);
  memory.writeInt32LE(12345, 16);
  const access = { read: async (_pid, address, size) => memory.subarray(Number(BigInt(address) - 0x1000n), Number(BigInt(address) - 0x1000n) + size) };
  const service = { attached: { pid: 1 }, ensureAttached: async () => service.attached, regions: async () => regions };
  const scanner = new Int32Scanner(access, service);
  const first = await scanner.firstExact(12345, { regions, chunkSize: 12 });
  assert.deepEqual(first.results.map((item) => item.address), ['0x1004', '0x1010']);
  memory.writeInt32LE(500, 4);
  const next = await scanner.nextExact(first, 500);
  assert.deepEqual(next.results.map((item) => item.address), ['0x1004']);
  assert.equal(next.previousCount, 2);
});

test('candidate grouping avoids one read per result', () => {
  const groups = groupResults([{ address: '0x1000' }, { address: '0x1004' }, { address: '0x1800' }], 0x100);
  assert.equal(groups.length, 2);
  assert.equal(groups[0].size, 8);
  assert.equal(groups[1].size, 4);
});

test('freeze service starts, writes, and stops at a bounded interval', async () => {
  const writes = [];
  const access = { writeInt32: async (address, value) => writes.push({ address, value }) };
  const events = [];
  const freezer = new FreezeService(access, (event) => events.push(event));
  assert.equal(normalizeInterval(3), 10);
  freezer.add({ address: '0x1000', value: 100 }, 10);
  await new Promise((resolve) => setTimeout(resolve, 35));
  assert.ok(writes.length >= 1);
  assert.equal(freezer.remove('0x1000'), true);
  assert.equal(freezer.list().length, 0);
  assert.ok(events.some((event) => event.type === 'freeze-started'));
});

test('profile persistence writes schemaVersion and round-trips address entries', () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'killwind-profile-'));
  try {
    const store = new ProfileStore(directory);
    const saved = store.save({ gameName: 'MemoryTestGame', executable: 'MemoryTestGame.exe', addresses: [{ address: '0x1000', type: 'Int32' }] });
    assert.equal(saved.schemaVersion, 1);
    assert.deepEqual(store.list(), ['MemoryTestGame']);
    assert.equal(store.load('MemoryTestGame').addresses[0].address, '0x1000');
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
