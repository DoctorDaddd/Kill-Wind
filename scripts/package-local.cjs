const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const projectRoot = path.resolve(__dirname, '..');
const outputRoot = path.resolve(process.env.KILLWIND_OUTPUT || path.join(projectRoot, 'release', 'killwind'));
const appRoot = path.join(outputRoot, 'resources', 'app');
const cachedRuntime = path.join(projectRoot, 'runtime', 'electron');
const electronRuntime = path.join(projectRoot, 'node_modules', 'electron', 'dist');
const runtimeRoot = fs.existsSync(path.join(cachedRuntime, 'killwind.exe')) ? cachedRuntime : electronRuntime;
const runtimeExe = fs.existsSync(path.join(runtimeRoot, 'killwind.exe')) ? 'killwind.exe' : 'electron.exe';

if (!fs.existsSync(path.join(runtimeRoot, runtimeExe))) {
  throw new Error('找不到 Electron 运行时，请先运行 npm install，或准备 runtime/electron/killwind.exe。');
}
if (!fs.existsSync(path.join(projectRoot, 'native', 'MemoryBridge.exe'))) {
  throw new Error('MemoryBridge.exe 不存在，请先构建原生桥接。');
}
if (!fs.existsSync(path.join(projectRoot, 'native', 'ExeIconPatcher.exe'))) {
  throw new Error('ExeIconPatcher.exe 不存在，请先构建原生工具。');
}

fs.rmSync(outputRoot, { recursive: true, force: true });
fs.mkdirSync(appRoot, { recursive: true });

// ponytail: keep packaging local and portable; the runtime is stored under this project.
fs.copyFileSync(path.join(runtimeRoot, runtimeExe), path.join(outputRoot, 'killwind.exe'));
for (const entry of fs.readdirSync(runtimeRoot, { withFileTypes: true })) {
  if (!entry.isFile() || entry.name === runtimeExe) continue;
  fs.copyFileSync(path.join(runtimeRoot, entry.name), path.join(outputRoot, entry.name));
}

const iconPatch = spawnSync(path.join(projectRoot, 'native', 'ExeIconPatcher.exe'), [
  path.join(outputRoot, 'killwind.exe'),
  path.join(projectRoot, 'assets', 'killwind.ico'),
], { encoding: 'utf8', windowsHide: true });
if (iconPatch.status !== 0) throw new Error(`写入 EXE 图标失败：${iconPatch.stdout || ''}${iconPatch.stderr || ''}`);

const copy = (relativePath) => fs.cpSync(path.join(projectRoot, relativePath), path.join(appRoot, relativePath), { recursive: true });
for (const relativePath of ['package.json', 'main.cjs', 'preload.cjs', 'Core', 'Infrastructure', 'Profiles', 'ui', 'assets']) copy(relativePath);
fs.mkdirSync(path.join(appRoot, 'native'), { recursive: true });
fs.copyFileSync(path.join(projectRoot, 'native', 'MemoryBridge.exe'), path.join(appRoot, 'native', 'MemoryBridge.exe'));
fs.mkdirSync(path.join(appRoot, 'MemoryTestGame'), { recursive: true });
fs.copyFileSync(path.join(projectRoot, 'MemoryTestGame', 'MemoryTestGame.exe'), path.join(appRoot, 'MemoryTestGame', 'MemoryTestGame.exe'));

console.log(`Portable EXE created: ${path.join(outputRoot, 'killwind.exe')}`);
