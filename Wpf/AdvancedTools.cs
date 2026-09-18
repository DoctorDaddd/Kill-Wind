using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KillWind.Wpf
{
    internal sealed class AobPattern
    {
        public string Text { get; private set; }
        public byte?[] Bytes { get; private set; }

        private AobPattern() { }

        public static AobPattern Parse(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new FormatException("AOB 特征不能为空。");
            string[] parts = text.Trim().Split(new[] { ' ', '\t', '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte?>();
            foreach (string part in parts)
            {
                if (part == "?" || part == "??") bytes.Add(null);
                else
                {
                    byte value;
                    if (part.Length != 2 || !Byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out value)) throw new FormatException("AOB 特征包含无效字节：" + part);
                    bytes.Add(value);
                }
            }
            if (bytes.Count == 0 || bytes.All(item => !item.HasValue)) throw new FormatException("AOB 特征至少需要一个确定字节。");
            return new AobPattern { Text = String.Join(" ", parts), Bytes = bytes.ToArray() };
        }
    }

    internal sealed class AobMatch
    {
        public string Address { get { return "0x" + AddressValue.ToString("X"); } }
        public ulong AddressValue { get; set; }
        public string Module { get; set; }
        public string Pattern { get; set; }
    }

    internal sealed class AobScanner
    {
        private const int ChunkSize = 1024 * 1024;
        private readonly NativeBridgeClient bridge;

        public AobScanner(NativeBridgeClient bridge) { this.bridge = bridge; }

        public async Task<List<AobMatch>> ScanAsync(ProcessInfo process, IList<MemoryRegion> regions, AobPattern pattern, ModuleInfo module, IProgress<int> progress, CancellationToken token)
        {
            var matches = new List<AobMatch>();
            long total = regions.Sum(item => Math.Max(0, item.size));
            long scanned = 0;
            foreach (MemoryRegion region in regions)
            {
                if (module != null && !Overlaps(region.Base, (ulong)Math.Max(0, region.size), module.Base, (ulong)Math.Max(0, module.size))) { scanned += Math.Max(0, region.size); progress.Report(Percent(scanned, total)); continue; }
                for (long offset = 0; offset < region.size; offset += ChunkSize)
                {
                    token.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min((long)ChunkSize + pattern.Bytes.Length - 1, region.size - offset);
                    byte[] bytes;
                    try { bytes = await bridge.ReadAsync(process.pid, region.Base + (ulong)offset, requested); }
                    catch { scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total)); continue; }
                    int scanLimit = (int)Math.Min((long)ChunkSize, region.size - offset);
                    for (int index = 0; index + pattern.Bytes.Length <= bytes.Length && index < scanLimit; index++)
                    {
                        if (!Matches(bytes, index, pattern.Bytes)) continue;
                        ulong address = region.Base + (ulong)offset + (ulong)index;
                        matches.Add(new AobMatch { AddressValue = address, Module = module == null ? "" : module.name, Pattern = pattern.Text });
                    }
                    scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total));
                }
            }
            return matches;
        }

        private static bool Matches(byte[] bytes, int offset, byte?[] pattern)
        {
            for (int index = 0; index < pattern.Length; index++) if (pattern[index].HasValue && bytes[offset + index] != pattern[index].Value) return false;
            return true;
        }

        private static bool Overlaps(ulong leftBase, ulong leftSize, ulong rightBase, ulong rightSize)
        {
            return leftBase < rightBase + rightSize && rightBase < leftBase + leftSize;
        }

        private static int Percent(long value, long total) { return total <= 0 ? 100 : (int)Math.Min(100, value * 100 / total); }
    }

    internal sealed class PointerPath
    {
        public string Module { get; set; }
        public ulong ModuleOffset { get; set; }
        public ulong[] Offsets { get; set; }
        public ulong TargetAddress { get; set; }
        public string Display
        {
            get
            {
                string suffix = Offsets == null || Offsets.Length == 0 ? "" : " -> " + String.Join(" -> ", Offsets.Select(PointerParser.Format).ToArray());
                return Module + "+" + PointerParser.Format(ModuleOffset) + suffix;
            }
        }
    }

    internal sealed class PointerScanner
    {
        private const int ChunkSize = 1024 * 1024;
        private const int MaxCandidates = 2500;
        private readonly NativeBridgeClient bridge;

        public PointerScanner(NativeBridgeClient bridge) { this.bridge = bridge; }

        public async Task<List<PointerPath>> ScanAsync(ProcessInfo process, ModuleInfo[] modules, IList<MemoryRegion> regions, ulong target, int depth, ulong maxOffset, int alignment, IProgress<int> progress, CancellationToken token)
        {
            if (depth < 1 || depth > 5) throw new ArgumentException("Pointer Scan 深度必须在 1 到 5 之间。");
            if (alignment < 1) throw new ArgumentException("Pointer Scan 对齐必须大于 0。");
            int pointerSize = String.Equals(process.architecture, "x86", StringComparison.OrdinalIgnoreCase) ? 4 : 8;
            var current = new List<ReverseCandidate> { new ReverseCandidate { TargetAddress = target, Offsets = new List<ulong>() } };
            var result = new List<PointerPath>();
            for (int level = 0; level < depth; level++)
            {
                token.ThrowIfCancellationRequested();
                current = await FindParentsAsync(process, regions, current, pointerSize, maxOffset, alignment, progress, level, depth, token);
                foreach (ReverseCandidate candidate in current)
                {
                    ModuleInfo module = FindModule(candidate.TargetAddress, modules);
                    if (module == null) continue;
                    result.Add(new PointerPath { Module = module.name, ModuleOffset = candidate.TargetAddress - module.Base, Offsets = candidate.Offsets.ToArray(), TargetAddress = target });
                    if (result.Count >= MaxCandidates) return result;
                }
                if (current.Count == 0) break;
            }
            return result;
        }

        private async Task<List<ReverseCandidate>> FindParentsAsync(ProcessInfo process, IList<MemoryRegion> regions, IList<ReverseCandidate> targets, int pointerSize, ulong maxOffset, int alignment, IProgress<int> progress, int level, int depth, CancellationToken token)
        {
            var found = new List<ReverseCandidate>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = regions.Sum(item => Math.Max(0, item.size));
            long scanned = 0;
            foreach (MemoryRegion region in regions)
            {
                for (long offset = 0; offset < region.size; offset += ChunkSize)
                {
                    token.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min((long)ChunkSize + pointerSize - 1, region.size - offset);
                    byte[] bytes;
                    try { bytes = await bridge.ReadAsync(process.pid, region.Base + (ulong)offset, requested); }
                    catch { scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report((level * 100 + Percent(scanned, total)) / depth); continue; }
                    int scanLimit = (int)Math.Min((long)ChunkSize, region.size - offset);
                    for (int index = 0; index + pointerSize <= bytes.Length && index < scanLimit; index += alignment)
                    {
                        ulong pointer = pointerSize == 4 ? BitConverter.ToUInt32(bytes, index) : BitConverter.ToUInt64(bytes, index);
                        ulong address = region.Base + (ulong)offset + (ulong)index;
                        foreach (ReverseCandidate target in targets)
                        {
                            if (target.TargetAddress < pointer) continue;
                            ulong difference = target.TargetAddress - pointer;
                            if (difference > maxOffset || difference % (ulong)alignment != 0) continue;
                            var offsets = new List<ulong> { difference };
                            offsets.AddRange(target.Offsets);
                            string key = address.ToString("X") + ":" + String.Join(",", offsets.Select(item => item.ToString("X")).ToArray());
                            if (unique.Add(key)) found.Add(new ReverseCandidate { TargetAddress = address, Offsets = offsets });
                            if (found.Count >= MaxCandidates) return found;
                        }
                    }
                    scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report((level * 100 + Percent(scanned, total)) / depth);
                }
            }
            return found;
        }

        private static ModuleInfo FindModule(ulong address, ModuleInfo[] modules)
        {
            return (modules ?? new ModuleInfo[0]).FirstOrDefault(item => address >= item.Base && address < item.Base + (ulong)Math.Max(0, item.size));
        }

        private sealed class ReverseCandidate
        {
            public ulong TargetAddress;
            public List<ulong> Offsets;
        }

        private static int Percent(long value, long total) { return total <= 0 ? 100 : (int)Math.Min(100, value * 100 / total); }
    }

    internal sealed class SaveDocument
    {
        public string Path { get; set; }
        public bool IsBinary { get; set; }
        public string Text { get; set; }
        public byte[] Bytes { get; set; }
    }

    internal sealed class SaveDiff
    {
        public string Offset { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
    }

    internal sealed class SaveEditorService
    {
        public SaveDocument Open(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            bool binary = IsBinary(path, bytes);
            return new SaveDocument { Path = path, IsBinary = binary, Bytes = bytes, Text = binary ? FormatBytes(bytes) : Encoding.UTF8.GetString(bytes) };
        }

        public string Save(SaveDocument document, string text, string backupDirectory)
        {
            if (document == null || String.IsNullOrWhiteSpace(document.Path)) throw new InvalidOperationException("没有打开存档文件。");
            Directory.CreateDirectory(backupDirectory);
            string backup = Path.Combine(backupDirectory, Path.GetFileNameWithoutExtension(document.Path) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(document.Path));
            File.Copy(document.Path, backup, true);
            byte[] bytes = document.IsBinary ? ParseBytes(text) : Encoding.UTF8.GetBytes(text ?? "");
            File.WriteAllBytes(document.Path, bytes);
            document.Bytes = bytes; document.Text = document.IsBinary ? FormatBytes(bytes) : text;
            return backup;
        }

        public List<SaveDiff> Diff(string leftPath, string rightPath)
        {
            byte[] left = File.ReadAllBytes(leftPath); byte[] right = File.ReadAllBytes(rightPath);
            int length = Math.Max(left.Length, right.Length); var result = new List<SaveDiff>();
            for (int index = 0; index < length; index++)
            {
                byte oldValue = index < left.Length ? left[index] : (byte)0;
                byte newValue = index < right.Length ? right[index] : (byte)0;
                if (index >= left.Length || index >= right.Length || oldValue != newValue) result.Add(new SaveDiff { Offset = "0x" + index.ToString("X"), OldValue = index < left.Length ? oldValue.ToString("X2") : "<EOF>", NewValue = index < right.Length ? newValue.ToString("X2") : "<EOF>" });
            }
            return result;
        }

        public static string FormatBytes(byte[] bytes) { return String.Join(" ", (bytes ?? new byte[0]).Select(item => item.ToString("X2")).ToArray()); }

        public static byte[] ParseBytes(string text)
        {
            string[] parts = (text ?? "").Replace(",", " ").Replace("-", " ").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>();
            foreach (string part in parts) { byte value; if (!Byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out value)) throw new FormatException("十六进制内容无效：" + part); bytes.Add(value); }
            return bytes.ToArray();
        }

        private static bool IsBinary(string path, byte[] bytes)
        {
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".json" || extension == ".xml" || extension == ".ini" || extension == ".cfg" || extension == ".txt") return false;
            int control = bytes.Count(item => item == 0 || (item < 9) || (item > 13 && item < 32));
            return bytes.Length > 0 && control > bytes.Length / 20;
        }
    }
}
