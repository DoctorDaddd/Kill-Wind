using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace KillWind.Wpf
{
    internal sealed class MemoryScanner
    {
        private const int ChunkSize = 1024 * 1024;
        private readonly NativeBridgeClient bridge;

        public MemoryScanner(NativeBridgeClient bridge) { this.bridge = bridge; }

        public async Task<List<ScanResult>> FirstExactAsync(ProcessInfo process, IList<MemoryRegion> regions, ScanDataType type, string input, IProgress<int> progress, CancellationToken token)
        {
            DataValue wanted = ParseValue(type, input);
            var results = new List<ScanResult>();
            long total = regions.Sum(region => Math.Max(0, region.size));
            long scanned = 0;
            int width = wanted.Raw.Length;
            foreach (MemoryRegion region in regions)
            {
                token.ThrowIfCancellationRequested();
                for (long offset = 0; offset < region.size; offset += ChunkSize)
                {
                    token.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min((long)ChunkSize + width - 1, region.size - offset);
                    byte[] bytes;
                    try { bytes = await bridge.ReadAsync(process.pid, region.Base + (ulong)offset, requested); }
                    catch (Exception error) { Debug.WriteLine("扫描区域读取失败：" + error.Message); scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total)); continue; }
                    int scanLimit = (int)Math.Min((long)ChunkSize, region.size - offset);
                    for (int index = 0; index + width <= bytes.Length && index < scanLimit; index++)
                    {
                        DataValue value = Decode(bytes, index, type, width);
                        if (BytesEqual(value.Raw, wanted.Raw)) results.Add(CreateResult(region.Base, region.Base + (ulong)offset + (ulong)index, type, value));
                    }
                    scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total));
                }
            }
            return results;
        }

        public async Task<List<ScanResult>> FirstUnknownAsync(ProcessInfo process, IList<MemoryRegion> regions, ScanDataType type, IProgress<int> progress, CancellationToken token)
        {
            if (type == ScanDataType.String || type == ScanDataType.ByteArray) throw new ArgumentException("String 和 Byte Array 需要先输入精确内容，不能使用未知初始值。");
            var results = new List<ScanResult>();
            long total = regions.Sum(region => Math.Max(0, region.size));
            long scanned = 0;
            int width = Width(type);
            foreach (MemoryRegion region in regions)
            {
                token.ThrowIfCancellationRequested();
                for (long offset = 0; offset < region.size; offset += ChunkSize)
                {
                    token.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min((long)ChunkSize + width - 1, region.size - offset);
                    byte[] bytes;
                    try { bytes = await bridge.ReadAsync(process.pid, region.Base + (ulong)offset, requested); }
                    catch (Exception error) { Debug.WriteLine("未知扫描区域读取失败：" + error.Message); scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total)); continue; }
                    int scanLimit = (int)Math.Min((long)ChunkSize, region.size - offset);
                    for (int index = 0; index + width <= bytes.Length && index < scanLimit; index += width)
                    {
                        DataValue value = Decode(bytes, index, type, width);
                        results.Add(CreateResult(region.Base, region.Base + (ulong)offset + (ulong)index, type, value));
                    }
                    scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total));
                }
            }
            return results;
        }

        public async Task<List<ScanResult>> FilterAsync(ProcessInfo process, IList<ScanResult> previous, ScanDataType type, ScanCondition condition, string input, IProgress<int> progress, CancellationToken token)
        {
            DataValue wanted = condition == ScanCondition.Exact ? ParseValue(type, input) : null;
            int width = wanted == null ? (previous.Count == 0 ? Width(type) : previous[0].RawValue.Length) : wanted.Raw.Length;
            var batches = new Dictionary<string, List<ScanResult>>();
            foreach (ScanResult result in previous)
            {
                ulong chunkStart = result.RegionBase + ((result.AddressValue - result.RegionBase) / (ulong)ChunkSize) * (ulong)ChunkSize;
                string key = result.RegionBase.ToString("X") + ":" + chunkStart.ToString("X");
                if (!batches.ContainsKey(key)) batches[key] = new List<ScanResult>();
                batches[key].Add(result);
            }
            var filtered = new List<ScanResult>(); int done = 0;
            foreach (List<ScanResult> batch in batches.Values)
            {
                token.ThrowIfCancellationRequested();
                ulong start = batch[0].RegionBase + ((batch[0].AddressValue - batch[0].RegionBase) / (ulong)ChunkSize) * (ulong)ChunkSize;
                ulong end = batch.Max(item => item.AddressValue) + (ulong)width;
                byte[] bytes;
                try { bytes = await bridge.ReadAsync(process.pid, start, checked((int)(end - start))); }
                catch (Exception error) { Debug.WriteLine("候选地址读取失败：" + error.Message); done += batch.Count; progress.Report(Percent(done, previous.Count)); continue; }
                foreach (ScanResult old in batch)
                {
                    int offset = checked((int)(old.AddressValue - start));
                    DataValue current = Decode(bytes, offset, type, width);
                    bool keep = condition == ScanCondition.Exact ? BytesEqual(current.Raw, wanted.Raw) : Matches(condition, current.Number, old.NumericValue, current.Raw, old.RawValue);
                    if (keep) filtered.Add(CreateResult(old.RegionBase, old.AddressValue, type, current));
                    done++;
                }
                progress.Report(Percent(done, previous.Count));
            }
            return filtered;
        }

        private static bool Matches(ScanCondition condition, double current, double old, byte[] currentRaw, byte[] oldRaw)
        {
            switch (condition)
            {
                case ScanCondition.Changed: return !BytesEqual(currentRaw, oldRaw);
                case ScanCondition.Unchanged: return BytesEqual(currentRaw, oldRaw);
                case ScanCondition.Increased: return current > old;
                case ScanCondition.Decreased: return current < old;
                default: return false;
            }
        }

        private static ScanResult CreateResult(ulong regionBase, ulong address, ScanDataType type, DataValue value)
        {
            return new ScanResult { RegionBase = regionBase, AddressValue = address, Type = type.ToString(), Value = value.Display, NumericValue = value.Number, RawValue = value.Raw };
        }

        private static int Percent(long value, long total) { return total <= 0 ? 100 : (int)Math.Min(100, value * 100 / total); }
        private static int Percent(int value, int total) { return total <= 0 ? 100 : Math.Min(100, value * 100 / total); }
        private static int Width(ScanDataType type)
        {
            switch (type)
            {
                case ScanDataType.Byte: return 1;
                case ScanDataType.Int16:
                case ScanDataType.UInt16: return 2;
                case ScanDataType.Int64:
                case ScanDataType.UInt64:
                case ScanDataType.Double: return 8;
                case ScanDataType.String:
                case ScanDataType.ByteArray: return 0;
                default: return 4;
            }
        }

        private sealed class DataValue
        {
            public double Number;
            public string Display;
            public byte[] Raw;
        }

        private static DataValue ParseValue(ScanDataType type, string input)
        {
            NumberFormatInfo format = CultureInfo.InvariantCulture.NumberFormat;
            if (type == ScanDataType.Byte)
            {
                byte value; if (!Byte.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 Byte 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = new[] { value } };
            }
            if (type == ScanDataType.Int16)
            {
                short value; if (!Int16.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 Int16 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.UInt16)
            {
                ushort value; if (!UInt16.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 UInt16 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.Int32)
            {
                int value; if (!Int32.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 Int32 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.UInt32)
            {
                uint value; if (!UInt32.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 UInt32 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.Int64)
            {
                long value; if (!Int64.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 Int64 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.UInt64)
            {
                ulong value; if (!UInt64.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 UInt64 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.Float)
            {
                float value; if (!Single.TryParse(input, NumberStyles.Float, format, out value)) throw new ArgumentException("请输入有效的 Float 数值。");
                return new DataValue { Number = value, Display = value.ToString("R", format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.Double)
            {
                double doubleValue; if (!Double.TryParse(input, NumberStyles.Float, format, out doubleValue)) throw new ArgumentException("请输入有效的 Double 数值。");
                return new DataValue { Number = doubleValue, Display = doubleValue.ToString("R", format), Raw = BitConverter.GetBytes(doubleValue) };
            }
            if (type == ScanDataType.String)
            {
                if (String.IsNullOrEmpty(input)) throw new ArgumentException("请输入要扫描的字符串。");
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                return new DataValue { Display = input, Raw = bytes };
            }
            byte[] array = ParseByteArray(input);
            return new DataValue { Display = FormatByteArray(array), Raw = array };
        }

        private static DataValue Decode(byte[] bytes, int offset, ScanDataType type, int width)
        {
            if (type == ScanDataType.Byte) { byte value = bytes[offset]; return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 1) }; }
            if (type == ScanDataType.Int16) { short value = BitConverter.ToInt16(bytes, offset); return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 2) }; }
            if (type == ScanDataType.UInt16) { ushort value = BitConverter.ToUInt16(bytes, offset); return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 2) }; }
            if (type == ScanDataType.Int32) { int value = BitConverter.ToInt32(bytes, offset); return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 4) }; }
            if (type == ScanDataType.UInt32) { uint value = BitConverter.ToUInt32(bytes, offset); return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 4) }; }
            if (type == ScanDataType.Int64) { long value = BitConverter.ToInt64(bytes, offset); return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 8) }; }
            if (type == ScanDataType.UInt64) { ulong value = BitConverter.ToUInt64(bytes, offset); return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 8) }; }
            if (type == ScanDataType.Float) { float value = BitConverter.ToSingle(bytes, offset); return new DataValue { Number = value, Display = value.ToString("R", CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 4) }; }
            if (type == ScanDataType.Double) { double doubleValue = BitConverter.ToDouble(bytes, offset); return new DataValue { Number = doubleValue, Display = doubleValue.ToString("R", CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 8) }; }
            byte[] raw = Slice(bytes, offset, width);
            if (type == ScanDataType.String) return new DataValue { Display = Encoding.UTF8.GetString(raw), Raw = raw };
            return new DataValue { Display = FormatByteArray(raw), Raw = raw };
        }

        private static byte[] ParseByteArray(string input)
        {
            string normalized = (input ?? "").Replace(",", " ").Replace("-", " ").Trim();
            if (normalized.Length == 0) throw new ArgumentException("请输入十六进制字节，例如：48 8B 05。");
            string[] parts = normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>();
            if (parts.Length == 1 && parts[0].Length > 2)
            {
                if ((parts[0].Length & 1) != 0) throw new ArgumentException("十六进制字节长度必须是偶数。");
                for (int index = 0; index < parts[0].Length; index += 2) bytes.Add(Byte.Parse(parts[0].Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            else
            {
                foreach (string part in parts) bytes.Add(Byte.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
            if (bytes.Count == 0) throw new ArgumentException("请输入有效的十六进制字节。");
            return bytes.ToArray();
        }

        private static string FormatByteArray(byte[] bytes)
        {
            return String.Join(" ", bytes.Select(item => item.ToString("X2", CultureInfo.InvariantCulture)).ToArray());
        }

        private static byte[] Slice(byte[] bytes, int offset, int length) { var result = new byte[length]; Buffer.BlockCopy(bytes, offset, result, 0, length); return result; }
        private static bool BytesEqual(byte[] left, byte[] right) { if (left == null || right == null || left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false; return true; }
    }
}
