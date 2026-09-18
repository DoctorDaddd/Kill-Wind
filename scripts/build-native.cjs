const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const root = path.resolve(__dirname, '..');
const native = path.join(root, 'native');
const testGame = path.join(root, 'MemoryTestGame');
const compilerCandidates = [
  'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe',
  'C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe',
];
const compiler = compilerCandidates.find(fs.existsSync);

if (!compiler) {
  throw new Error('未找到 Windows C# 编译器，无法构建 native bridge。');
}

fs.mkdirSync(native, { recursive: true });
fs.mkdirSync(testGame, { recursive: true });

function compile(source, output, extra = []) {
  const args = [
    '/nologo', '/optimize+', '/debug-', '/platform:x64',
    `/out:${output}`, ...extra, source,
  ];
  const result = spawnSync(compiler, args, { encoding: 'utf8', windowsHide: true });
  if (result.status !== 0) {
    throw new Error(`编译失败：${path.basename(source)}\n${result.stdout || ''}${result.stderr || ''}`);
  }
}

compile(path.join(native, 'MemoryBridge.cs'), path.join(native, 'MemoryBridge.exe'), [
  '/target:exe', '/r:System.Runtime.Serialization.dll', '/r:System.Core.dll', '/r:System.Web.Extensions.dll',
]);
compile(path.join(testGame, 'MemoryTestGame.cs'), path.join(testGame, 'MemoryTestGame.exe'), [
  '/target:winexe', '/r:System.dll', '/r:System.Core.dll',
  '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
]);
require('./build-icon.cjs');
console.log('Native bridge and MemoryTestGame built.');
