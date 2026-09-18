const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const outputRoot = path.resolve(process.env.KILLWIND_OUTPUT || path.join(root, 'release', 'killwind-wpf'));
const executable = path.join(root, 'Wpf', 'bin', 'KillWind.Wpf.exe');
const bridge = path.join(root, 'native', 'MemoryBridge.exe');
const testGame = path.join(root, 'MemoryTestGame', 'MemoryTestGame.exe');

for (const file of [executable, bridge, testGame]) {
  if (!fs.existsSync(file)) throw new Error(`找不到打包输入：${file}，请先执行构建。`);
}

if (fs.existsSync(outputRoot)) {
  throw new Error(`输出目录已存在，为避免覆盖用户文件请指定新的 KILLWIND_OUTPUT：${outputRoot}`);
}
fs.mkdirSync(path.join(outputRoot, 'native'), { recursive: true });
fs.mkdirSync(path.join(outputRoot, 'MemoryTestGame'), { recursive: true });
fs.copyFileSync(executable, path.join(outputRoot, 'KillWind.exe'));
fs.copyFileSync(bridge, path.join(outputRoot, 'native', 'MemoryBridge.exe'));
fs.copyFileSync(testGame, path.join(outputRoot, 'MemoryTestGame', 'MemoryTestGame.exe'));
fs.copyFileSync(path.join(root, 'assets', 'killwind.ico'), path.join(outputRoot, 'killwind.ico'));

console.log(`Portable WPF EXE created: ${path.join(outputRoot, 'KillWind.exe')}`);
