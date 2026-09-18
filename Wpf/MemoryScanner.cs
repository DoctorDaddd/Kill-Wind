using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
                    catch (Exception error) { Debug.WriteLine("扫描区域读取失败：" + error.Message); scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total)); continue; }
                    int scanLimit = (int)Math.Min((long)ChunkSize, region.size - offset);
                    for (int index = 0; index + width <= bytes.Length && index < scanLimit; index++)
                    {
                        DataValue value = Decode(bytes, index, type);
                        if (BytesEqual(value.Raw, wanted.Raw)) results.Add(CreateResult(region.Base, region.Base + (ulong)offset + (ulong)index, type, value));
                    }
                    scanned += Math.Min((long)ChunkSize, region.size - offset); progress.Report(Percent(scanned, total));
                }
            }
            return results;
        }

        public async Task<List<ScanResult>> FilterAsync(ProcessInfo process, IList<ScanResult> previous, ScanDataType type, ScanCondition condition, string input, IProgress<int> progress, CancellationToken token)
        {
            DataValue wanted = condition == ScanCondition.Exact ? ParseValue(type, input) : null;
            int width = Width(type);
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
                    DataValue current = Decode(bytes, offset, type);
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
        private static int Width(ScanDataType type) { return type == ScanDataType.Double ? 8 : 4; }

        private sealed class DataValue
        {
            public double Number;
            public string Display;
            public byte[] Raw;
        }

        private static DataValue ParseValue(ScanDataType type, string input)
        {
            NumberFormatInfo format = CultureInfo.InvariantCulture.NumberFormat;
            if (type == ScanDataType.Int32)
            {
                int value; if (!Int32.TryParse(input, NumberStyles.Integer, format, out value)) throw new ArgumentException("请输入有效的 Int32 数值。");
                return new DataValue { Number = value, Display = value.ToString(format), Raw = BitConverter.GetBytes(value) };
            }
            if (type == ScanDataType.Float)
            {
                float value; if (!Single.TryParse(input, NumberStyles.Float, format, out value)) throw new ArgumentException("请输入有效的 Float 数值。");
                return new DataValue { Number = value, Display = value.ToString("R", format), Raw = BitConverter.GetBytes(value) };
            }
            double doubleValue; if (!Double.TryParse(input, NumberStyles.Float, format, out doubleValue)) throw new ArgumentException("请输入有效的 Double 数值。");
            return new DataValue { Number = doubleValue, Display = doubleValue.ToString("R", format), Raw = BitConverter.GetBytes(doubleValue) };
        }

        private static DataValue Decode(byte[] bytes, int offset, ScanDataType type)
        {
            if (type == ScanDataType.Int32) { int value = BitConverter.ToInt32(bytes, offset); return new DataValue { Number = value, Display = value.ToString(CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 4) }; }
            if (type == ScanDataType.Float) { float value = BitConverter.ToSingle(bytes, offset); return new DataValue { Number = value, Display = value.ToString("R", CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 4) }; }
            double doubleValue = BitConverter.ToDouble(bytes, offset); return new DataValue { Number = doubleValue, Display = doubleValue.ToString("R", CultureInfo.InvariantCulture), Raw = Slice(bytes, offset, 8) };
        }

        private static byte[] Slice(byte[] bytes, int offset, int length) { var result = new byte[length]; Buffer.BlockCopy(bytes, offset, result, 0, length); return result; }
        private static bool BytesEqual(byte[] left, byte[] right) { if (left == null || right == null || left.Length != right.Length) return false; for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false; return true; }
    }
}
