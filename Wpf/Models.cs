using System;

namespace KillWind.Wpf
{
    public sealed class ProcessInfo
    {
        public int pid { get; set; }
        public string name { get; set; }
        public string path { get; set; }
        public string architecture { get; set; }
        public long memoryBytes { get; set; }
        public string startTime { get; set; }
        public string DisplayName { get { return String.Format("{0}  ·  PID {1}  ·  {2}", name, pid, architecture); } }
    }

    public sealed class MemoryRegion
    {
        public string baseAddress { get; set; }
        public long size { get; set; }
        public string state { get; set; }
        public string protection { get; set; }
        public string type { get; set; }
        public bool readable { get; set; }
        public bool writable { get; set; }
        public bool executable { get; set; }
        public ulong Base { get { return Convert.ToUInt64(baseAddress.Substring(2), 16); } }
    }

    public enum ScanDataType { Int32, Float, Double }
    public enum ScanCondition { Exact, Changed, Unchanged, Increased, Decreased }

    public sealed class ScanResult
    {
        public string Address { get { return String.Format("0x{0:X}", AddressValue); } }
        public string Value { get; set; }
        public string Type { get; set; }
        public ulong AddressValue { get; set; }
        public ulong RegionBase { get; set; }
        public double NumericValue { get; set; }
        public byte[] RawValue { get; set; }
    }

    public sealed class AddressEntry
    {
        public string Description { get; set; }
        public string Address { get; set; }
        public string CurrentValue { get; set; }
        public string NewValue { get; set; }
        public string Type { get; set; }
        public bool Frozen { get; set; }
        public ulong AddressValue { get { return Convert.ToUInt64(Address.Substring(2), 16); } }
    }

    public sealed class ScreenPoint
    {
        public int x { get; set; }
        public int y { get; set; }
        public string status { get; set; }
    }
}
