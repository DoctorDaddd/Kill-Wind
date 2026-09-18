const { app, BrowserWindow, ipcMain, dialog } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { NativeMemoryBridge } = require('./Core/Memory/NativeMemoryBridge.cjs');
const { ProcessService } = require('./Core/Processes/ProcessService.cjs');
const { MemoryAccess } = require('./Core/Memory/MemoryAccess.cjs');
const { Int32Scanner } = require('./Core/Scanner/Int32Scanner.cjs');
const { FreezeService } = require('./Core/Freeze/FreezeService.cjs');
const { ProfileStore } = require('./Profiles/ProfileStore.cjs');
const { Logger } = require('./Infrastructure/Logger.cjs');

app.setName('killwind');
app.setAppUserModelId('com.killwind.editor');
const icon = path.join(__dirname, 'assets', 'killwind.ico');
const bridge = new NativeMemoryBridge(path.join(__dirname, 'native', 'MemoryBridge.exe'));
const processService = new ProcessService(bridge);
const memory = new MemoryAccess(bridge, processService);
const scanner = new Int32Scanner(memory, processService);
let windowRef;
let scanSession = null;
let scanController = null;
let addressEntries = [];
let screenPoint = null;
let processWatcher;
let testGameProcess = null;
let logger;
let profiles;

function send(channel, payload) {
  if (!windowRef?.isDestroyed()) windowRef.webContents.send(channel, payload);
}

function createWindow() {
  windowRef = new BrowserWindow({
    width: 1380, height: 900, minWidth: 1060, minHeight: 700,
    backgroundColor: '#070b12', icon,
    webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false },
  });
  windowRef.removeMenu();
  windowRef.loadFile(path.join(__dirname, 'ui', 'index.html'));
  windowRef.on('closed', () => { windowRef = null; });
}

function setupServices() {
  const root = app.getPath('userData');
  logger = new Logger(path.join(root, 'Logs'));
  profiles = new ProfileStore(path.join(root, 'Profiles'));
  bridge.on('diagnostic', (message) => logger.warning(message));
  bridge.on('exit', (details) => logger.warning('Process bridge exited', details));
  const freezer = new FreezeService(memory, (event) => {
    if (event.type === 'freeze-error') logger.error('Freeze write failed', event);
    send('freeze:event', event);
  });
  ipcMain.handle('process:list', async () => processService.list({ taskbarOnly: true }));
  ipcMain.handle('app:quit', () => { app.quit(); return true; });
  ipcMain.handle('process:attach', async (_event, processInfo) => {
    const attached = await processService.attach(processInfo);
    scanSession = null;
    addressEntries = [];
    logger.info('Process Attached', attached);
    send('process:state', { status: 'Connected', process: attached });
    return { process: attached, regions: await processService.regions() };
  });
  ipcMain.handle('process:detach', () => {
    freezer.clear();
    const detached = processService.detach();
    scanSession = null;
    addressEntries = [];
    logger.info('Process Detached', detached);
    send('process:state', { status: 'Disconnected', process: null });
    return { detached };
  });
  ipcMain.handle('process:regions', (_event, options) => processService.regions(options || {}));
  ipcMain.handle('scan:first', async (event, rawValue) => {
    if (scanController) throw new Error('已有扫描正在进行。');
    const value = parseInt32(rawValue);
    scanController = new AbortController();
    try {
      const regions = await processService.regions();
      logger.info('Scan Started', { kind: 'First', type: 'Int32', value });
      scanSession = await scanner.firstExact(value, { regions }, (progress) => event.sender.send('scan:progress', progress), scanController.signal);
      logger.info('Scan Finished', { kind: 'First', count: scanSession.results.length, elapsedMs: scanSession.elapsedMs });
      return scanSession;
    } finally { scanController = null; }
  });
  ipcMain.handle('scan:next', async (event, rawValue) => {
    if (!scanSession) throw new Error('请先完成 First Scan。');
    if (scanController) throw new Error('已有扫描正在进行。');
    const value = parseInt32(rawValue);
    scanController = new AbortController();
    try {
      logger.info('Scan Started', { kind: 'Next', type: 'Int32', value, previousCount: scanSession.results.length });
      scanSession = await scanner.nextExact(scanSession, value, (progress) => event.sender.send('scan:progress', progress), scanController.signal);
      logger.info('Scan Finished', { kind: 'Next', count: scanSession.results.length, elapsedMs: scanSession.elapsedMs });
      return scanSession;
    } finally { scanController = null; }
  });
  ipcMain.handle('scan:cancel', () => { scanController?.abort(); return { cancelled: true }; });
  ipcMain.handle('scan:clear', () => { scanSession = null; return {}; });
  ipcMain.handle('screen:pick', async () => {
    const process = await processService.ensureAttached();
    logger.info('Screen edit waiting for click', { pid: process.pid });
    const point = await bridge.pickScreenPoint(process.pid);
    if (point.status !== 'picked') throw new Error(point.status === 'timeout' ? '取点超时。' : '取点已取消。');
    screenPoint = point;
    logger.info('Screen point picked', point);
    send('screen:picked', point);
    return point;
  });
  ipcMain.handle('screen:apply', async (event, { currentValue, newValue }) => {
    if (scanController) throw new Error('已有扫描正在进行。');
    const current = parseInt32(currentValue);
    const next = parseInt32(newValue);
    scanController = new AbortController();
    try {
      const process = await processService.ensureAttached();
      const regions = await processService.regions();
      scanSession = await scanner.firstExact(current, { regions }, (progress) => event.sender.send('scan:progress', progress), scanController.signal);
      if (scanSession.results.length === 0) return { status: 'not-found', point: screenPoint, results: [] };
      if (scanSession.results.length === 1) {
        const result = scanSession.results[0];
        await memory.writeInt32(result.address, next);
        logger.info('Screen edit applied', { point: screenPoint, address: result.address, current, next });
        return { status: 'applied', point: screenPoint, address: result.address, current, next, results: [result] };
      }
      logger.warning('Screen edit has multiple matches', { point: screenPoint, count: scanSession.results.length });
      return { status: 'ambiguous', point: screenPoint, current, next, totalResults: scanSession.results.length, results: scanSession.results.slice(0, 2000) };
    } finally { scanController = null; }
  });
  ipcMain.handle('screen:apply-candidate', async (_event, { address, newValue }) => {
    const next = parseInt32(newValue);
    if (!scanSession?.results.some((item) => item.address.toUpperCase() === String(address).toUpperCase())) throw new Error('候选地址不属于当前屏幕编辑扫描。');
    await memory.writeInt32(address, next);
    logger.info('Screen edit candidate applied', { point: screenPoint, address, next });
    return { status: 'applied', point: screenPoint, address, next };
  });
  ipcMain.handle('memory:read-int32', (_event, address) => memory.readInt32(address));
  ipcMain.handle('memory:write-int32', async (_event, { address, value }) => {
    const result = await memory.writeInt32(address, parseInt32(value));
    logger.info('Write Success', { address, value: result });
    return result;
  });
  ipcMain.handle('address:list', () => addressEntries);
  ipcMain.handle('address:add', async (_event, result) => {
    if (!result?.address) throw new Error('地址不能为空。');
    const exists = addressEntries.find((item) => item.address.toUpperCase() === result.address.toUpperCase());
    if (exists) return exists;
    const originalValue = Number.isInteger(result.value) ? result.value : await memory.readInt32(result.address);
    const entry = { id: `${Date.now()}-${Math.random().toString(16).slice(2)}`, description: result.description || '未命名',
      address: result.address, type: 'Int32', value: originalValue, originalValue, frozen: false, freezeInterval: 100 };
    addressEntries.push(entry);
    logger.info('Address Added', entry);
    return entry;
  });
  ipcMain.handle('address:update', async (_event, { id, description, value }) => {
    const entry = addressEntries.find((item) => item.id === id);
    if (!entry) throw new Error('地址项不存在。');
    if (description !== undefined) entry.description = String(description).slice(0, 120);
    if (value !== undefined) { entry.value = parseInt32(value); await memory.writeInt32(entry.address, entry.value); }
    return entry;
  });
  ipcMain.handle('address:remove', (_event, id) => {
    const entry = addressEntries.find((item) => item.id === id);
    if (entry) freezer.remove(entry.address);
    addressEntries = addressEntries.filter((item) => item.id !== id);
    return addressEntries;
  });
  ipcMain.handle('address:read-all', async () => {
    for (const entry of addressEntries) {
      try { entry.value = await memory.readInt32(entry.address); entry.status = 'Resolved'; }
      catch (error) { entry.status = 'Invalid'; entry.error = error.message; }
    }
    return addressEntries;
  });
  ipcMain.handle('address:freeze', (_event, { id, frozen, intervalMs }) => {
    const entry = addressEntries.find((item) => item.id === id);
    if (!entry) throw new Error('地址项不存在。');
    entry.frozen = Boolean(frozen);
    entry.freezeInterval = Number(intervalMs) || entry.freezeInterval || 100;
    if (entry.frozen) freezer.add(entry, entry.freezeInterval);
    else freezer.remove(entry.address);
    return entry;
  });
  ipcMain.handle('profile:list', () => profiles.list());
  ipcMain.handle('profile:save', (_event, data) => {
    const process = processService.attached;
    const profile = profiles.save({ ...data, process, addresses: addressEntries });
    logger.info('Profile Saved', { gameName: profile.gameName });
    return profile;
  });
  ipcMain.handle('profile:load', (_event, name) => {
    const profile = profiles.load(name);
    addressEntries = profile.addresses || [];
    logger.info('Profile Loaded', { gameName: profile.gameName });
    return { ...profile, addresses: addressEntries };
  });
  ipcMain.handle('test-game:launch', () => {
    const gamePath = path.join(__dirname, 'MemoryTestGame', 'MemoryTestGame.exe');
    if (!fs.existsSync(gamePath)) throw new Error('MemoryTestGame.exe 尚未构建，请先运行 npm run build:native。');
    if (!testGameProcess || testGameProcess.exitCode !== null) testGameProcess = spawn(gamePath, [], { windowsHide: false });
    return { started: true };
  });
  processWatcher = setInterval(async () => {
    if (!processService.attached) return;
    try {
      const current = await processService.ensureAttached();
      send('process:state', { status: 'Connected', process: current });
    } catch (error) {
      freezer.clear();
      logger.warning('Process Closed', { message: error.message });
      send('process:state', { status: 'Disconnected', process: null, message: error.message });
    }
  }, 2000);
}

function parseInt32(value) {
  const parsed = Number(String(value).trim());
  if (!Number.isInteger(parsed) || parsed < -2147483648 || parsed > 2147483647) throw new Error('请输入有效的 Int32 整数。');
  return parsed;
}

function writeCrashLog(error) {
  try {
    const directory = path.join(app.getPath('userData'), 'CrashLogs');
    fs.mkdirSync(directory, { recursive: true });
    fs.writeFileSync(path.join(directory, `${new Date().toISOString().replace(/[:.]/g, '-')}.json`), JSON.stringify({
      timestamp: new Date().toISOString(), exception: error.message, stackTrace: error.stack,
      applicationVersion: app.getVersion(), platform: process.platform, release: process.release,
    }, null, 2), 'utf8');
  } catch (writeError) { console.error('Crash log write failed:', writeError); }
}

process.on('uncaughtException', (error) => { writeCrashLog(error); if (logger) logger.error('Uncaught exception', { message: error.message }); });
process.on('unhandledRejection', (error) => { writeCrashLog(error); if (logger) logger.error('Unhandled rejection', { message: error?.message || String(error) }); });

app.whenReady().then(() => {
  setupServices();
  createWindow();
}).catch((error) => { writeCrashLog(error); dialog.showErrorBox('killwind 启动失败', error.message); });

app.on('before-quit', () => {
  clearInterval(processWatcher);
  bridge.stop();
});
