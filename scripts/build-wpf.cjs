const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const root = path.resolve(__dirname, '..');
const outputDir = path.join(root, 'Wpf', 'bin');
const output = path.join(outputDir, 'KillWind.Wpf.exe');
const compilerCandidates = [
  'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe',
  'C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe',
];
const compiler = compilerCandidates.find(fs.existsSync);

if (!compiler) throw new Error('未找到 Windows C# 编译器，无法构建 WPF 版本。');

const framework = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319';
const wpf = path.join(framework, 'WPF');
const references = [
  path.join(framework, 'System.dll'),
  path.join(framework, 'System.Core.dll'),
  path.join(framework, 'System.Web.Extensions.dll'),
  path.join(framework, 'System.Xaml.dll'),
  path.join(wpf, 'PresentationCore.dll'),
  path.join(wpf, 'PresentationFramework.dll'),
  path.join(wpf, 'WindowsBase.dll'),
];
const sources = [
  path.join(root, 'Wpf', 'Models.cs'),
  path.join(root, 'Wpf', 'NativeBridgeClient.cs'),
  path.join(root, 'Wpf', 'ProfileStore.cs'),
  path.join(root, 'Wpf', 'PointerResolver.cs'),
  path.join(root, 'Wpf', 'AdvancedTools.cs'),
  path.join(root, 'Wpf', 'ToolWindows.cs'),
  path.join(root, 'Wpf', 'GlobalHotkeyService.cs'),
  path.join(root, 'Wpf', 'MemoryScanner.cs'),
  path.join(root, 'Wpf', 'MainWindow.cs'),
  path.join(root, 'Wpf', 'ValueDialog.cs'),
  path.join(root, 'Wpf', 'Program.cs'),
];

for (const file of references.concat(sources, path.join(root, 'assets', 'killwind.ico'))) {
  if (!fs.existsSync(file)) throw new Error(`WPF 构建输入不存在：${file}`);
}

fs.mkdirSync(outputDir, { recursive: true });
const args = [
  '/nologo', '/optimize+', '/debug-', '/platform:x64', '/target:winexe',
  `/out:${output}`, `/win32icon:${path.join(root, 'assets', 'killwind.ico')}`,
  ...references.map(file => `/r:${file}`),
  ...sources,
];
const result = spawnSync(compiler, args, { encoding: 'utf8', windowsHide: true });
if (result.status !== 0) {
  throw new Error(`WPF 编译失败：\n${result.stdout || ''}${result.stderr || ''}`);
}

console.log(`WPF EXE created: ${output}`);
