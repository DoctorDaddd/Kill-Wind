using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;

[DataContract]
public sealed class Request
{
    [DataMember] public string id;
    [DataMember] public string op;
    [DataMember] public int pid;
    [DataMember] public string address;
    [DataMember] public string data;
    [DataMember] public int size;
    [DataMember] public bool includeExecutable;
    [DataMember] public bool includeMapped;
    [DataMember] public bool taskbarOnly;
}

[DataContract]
public sealed class Response
{
    [DataMember] public string id;
    [DataMember] public bool ok;
    [DataMember] public string error;
    [DataMember] public string data;
}

[DataContract]
public sealed class ProcessInfoDto
{
    [DataMember] public int pid;
    [DataMember] public string name;
    [DataMember] public string path;
    [DataMember] public string architecture;
    [DataMember] public long memoryBytes;
    [DataMember] public string startTime;
}

[DataContract]
public sealed class RegionInfoDto
{
    [DataMember] public string baseAddress;
    [DataMember] public long size;
    [DataMember] public string state;
    [DataMember] public string protection;
    [DataMember] public string type;
    [DataMember] public bool readable;
    [DataMember] public bool writable;
    [DataMember] public bool executable;
}

[DataContract]
public sealed class ModuleInfoDto
{
    [DataMember] public string name;
    [DataMember] public string path;
    [DataMember] public string baseAddress;
    [DataMember] public long size;
}

[DataContract]
public sealed class ScreenPointDto
{
    [DataMember] public int x;
    [DataMember] public int y;
    [DataMember] public string status;
}

public static class NativeMethods
{
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_VM_OPERATION = 0x0008;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_VM_WRITE = 0x0020;
    public const uint MEM_COMMIT = 0x1000;
    public const uint PAGE_NOACCESS = 0x01;
    public const uint PAGE_GUARD = 0x100;
    public const uint PAGE_READONLY = 0x02;
    public const uint PAGE_READWRITE = 0x04;
    public const uint PAGE_WRITECOPY = 0x08;
    public const uint PAGE_EXECUTE = 0x10;
    public const uint PAGE_EXECUTE_READ = 0x20;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;
    public const uint PAGE_EXECUTE_WRITECOPY = 0x80;
    public const uint MEM_PRIVATE = 0x20000;
    public const uint MEM_MAPPED = 0x40000;
    public const uint MEM_IMAGE = 0x1000000;
    public const uint GW_OWNER = 4;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_APPWINDOW = 0x00040000L;

    public delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MEMORY_BASIC_INFORMATION info, UIntPtr length);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong32(IntPtr window, int index);
    public static long GetWindowExStyle(IntPtr window) { return IntPtr.Size == 8 ? GetWindowLongPtr64(window, GWL_EXSTYLE).ToInt64() : GetWindowLong32(window, GWL_EXSTYLE); }
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
}

public static class Program
{
    private static readonly DataContractJsonSerializer RequestSerializer = new DataContractJsonSerializer(typeof(Request));
    private static readonly DataContractJsonSerializer ResponseSerializer = new DataContractJsonSerializer(typeof(Response));
    private static readonly JavaScriptSerializer PayloadSerializer = new JavaScriptSerializer();
    private static readonly object WriteLock = new object();

    public static void Main()
    {
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            Response response;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                {
                    var request = (Request)RequestSerializer.ReadObject(stream);
                    response = Handle(request);
                    response.id = request.id;
                }
            }
            catch (Exception exception)
            {
                response = new Response { ok = false, error = exception.Message };
            }
            WriteResponse(response);
        }
    }

    private static void WriteResponse(Response response)
    {
        using (var stream = new MemoryStream())
        {
            ResponseSerializer.WriteObject(stream, response);
            var json = Encoding.UTF8.GetString(stream.ToArray());
            lock (WriteLock)
            {
                Console.WriteLine(json);
                Console.Out.Flush();
            }
        }
    }

    private static Response Handle(Request request)
    {
        switch (request.op)
        {
            case "list": return Success(ListProcesses(request.taskbarOnly));
            case "regions": return Success(ListRegions(request.pid, request.includeExecutable, request.includeMapped));
            case "modules": return Success(ListModules(request.pid));
            case "read": return Success(Read(request.pid, request.address, request.size));
            case "write": return Success(Write(request.pid, request.address, request.data));
            case "pick": return Success(PickScreenPoint(request.pid, request.size));
            default: throw new InvalidOperationException("未知 native 操作：" + request.op);
        }
    }

    private static Response Success(object data) { return new Response { ok = true, data = PayloadSerializer.Serialize(data) }; }

    private static ProcessInfoDto[] ListProcesses(bool taskbarOnly)
    {
        var result = new List<ProcessInfoDto>();
        var taskbarPids = taskbarOnly ? GetTaskbarProcessIds() : null;
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (taskbarPids != null && !taskbarPids.Contains(process.Id)) continue;
                string filePath = "";
                try { filePath = process.MainModule.FileName; }
                catch (Exception exception) { Diagnostic("path unavailable for PID " + process.Id + ": " + exception.Message); }
                DateTime start = DateTime.MinValue;
                try { start = process.StartTime; }
                catch (Exception exception) { Diagnostic("start time unavailable for PID " + process.Id + ": " + exception.Message); }
                result.Add(new ProcessInfoDto
                {
                    pid = process.Id,
                    name = process.ProcessName + ".exe",
                    path = filePath,
                    architecture = GetArchitecture(process.Handle),
                    memoryBytes = process.WorkingSet64,
                    startTime = start == DateTime.MinValue ? "" : start.ToString("o"),
                });
            }
            catch (Exception exception) { Diagnostic("process enumeration skipped an entry: " + exception.Message); }
            finally { process.Dispose(); }
        }
        result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
        return result.ToArray();
    }

    private static HashSet<int> GetTaskbarProcessIds()
    {
        var processIds = new HashSet<int>();
        int ownPid = Process.GetCurrentProcess().Id;
        NativeMethods.EnumWindows((window, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(window)) return true;
            if (NativeMethods.GetWindow(window, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;
            if (NativeMethods.GetWindowTextLength(window) == 0) return true;
            long style = NativeMethods.GetWindowExStyle(window);
            if ((style & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (style & NativeMethods.WS_EX_APPWINDOW) == 0) return true;
            uint processId;
            NativeMethods.GetWindowThreadProcessId(window, out processId);
            if (processId != 0 && processId != (uint)ownPid) processIds.Add((int)processId);
            return true;
        }, IntPtr.Zero);
        return processIds;
    }

    private static string GetArchitecture(IntPtr handle)
    {
        try
        {
            ushort processMachine, nativeMachine;
            if (NativeMethods.IsWow64Process2(handle, out processMachine, out nativeMachine))
                return processMachine == 0 ? (nativeMachine == 0xAA64 ? "ARM64" : "x64") : "x86";
        }
        catch (Exception exception) { Diagnostic("architecture unavailable: " + exception.Message); }
        return Environment.Is64BitOperatingSystem ? "x64" : "x86";
    }

    private static ModuleInfoDto[] ListModules(int pid)
    {
        using (var process = Process.GetProcessById(pid))
        {
            var result = new List<ModuleInfoDto>();
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    result.Add(new ModuleInfoDto
                    {
                        name = module.ModuleName,
                        path = module.FileName,
                        baseAddress = Hex(module.BaseAddress),
                        size = module.ModuleMemorySize,
                    });
                }
                catch (Exception exception) { Diagnostic("module unavailable for PID " + pid + ": " + exception.Message); }
            }
            result.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name));
            return result.ToArray();
        }
    }

    private static void Diagnostic(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.Flush();
    }

    private static RegionInfoDto[] ListRegions(int pid, bool includeExecutable, bool includeMapped)
    {
        IntPtr process = Open(pid, NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ);
        var result = new List<RegionInfoDto>();
        try
        {
            var infoSize = (UIntPtr)Marshal.SizeOf(typeof(NativeMethods.MEMORY_BASIC_INFORMATION));
            ulong address = 0;
            ulong maxAddress = Environment.Is64BitOperatingSystem ? 0x00007FFFFFFFFFFFUL : 0x7FFFFFFFUL;
            while (address < maxAddress)
            {
                NativeMethods.MEMORY_BASIC_INFORMATION info;
                var queried = NativeMethods.VirtualQueryEx(process, new IntPtr(unchecked((long)address)), out info, infoSize);
                if (queried == UIntPtr.Zero) break;
                ulong size = info.RegionSize.ToUInt64();
                if (size == 0) break;
                bool readable = IsReadable(info.Protect) && info.State == NativeMethods.MEM_COMMIT;
                bool writable = IsWritable(info.Protect) && info.State == NativeMethods.MEM_COMMIT;
                bool executable = IsExecutable(info.Protect) && info.State == NativeMethods.MEM_COMMIT;
                bool mapped = info.Type == NativeMethods.MEM_MAPPED;
                if (readable && (!executable || includeExecutable) && (!mapped || includeMapped))
                {
                    result.Add(new RegionInfoDto
                    {
                        baseAddress = Hex(info.BaseAddress), size = (long)Math.Min(size, long.MaxValue),
                        state = info.State == NativeMethods.MEM_COMMIT ? "Commit" : "Other",
                        protection = ProtectionName(info.Protect), type = TypeName(info.Type),
                        readable = readable, writable = writable, executable = executable,
                    });
                }
                ulong next = address + size;
                if (next <= address) break;
                address = next;
            }
            return result.ToArray();
        }
        finally { NativeMethods.CloseHandle(process); }
    }

    private static string Read(int pid, string addressText, int size)
    {
        if (size < 1 || size > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException("size", "单次读取范围必须在 1B 到 4MB 之间。");
        IntPtr process = Open(pid, NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_READ);
        try
        {
            var buffer = new byte[size];
            UIntPtr read;
            if (!NativeMethods.ReadProcessMemory(process, new IntPtr(unchecked((long)ParseAddress(addressText))), buffer, (UIntPtr)size, out read))
                throw new InvalidOperationException("读取内存失败：" + Marshal.GetLastWin32Error());
            if (read.ToUInt64() != (ulong)size) Array.Resize(ref buffer, (int)read.ToUInt64());
            return Convert.ToBase64String(buffer);
        }
        finally { NativeMethods.CloseHandle(process); }
    }

    private static bool Write(int pid, string addressText, string data)
    {
        if (String.IsNullOrWhiteSpace(data)) throw new ArgumentException("写入数据不能为空。");
        var buffer = Convert.FromBase64String(data);
        if (buffer.Length > 4 * 1024 * 1024) throw new ArgumentOutOfRangeException("data", "单次写入范围不能超过 4MB。");
        IntPtr process = Open(pid, NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_WRITE);
        try
        {
            UIntPtr written;
            if (!NativeMethods.WriteProcessMemory(process, new IntPtr(unchecked((long)ParseAddress(addressText))), buffer, (UIntPtr)buffer.Length, out written))
                throw new InvalidOperationException("写入内存失败：" + Marshal.GetLastWin32Error());
            return written.ToUInt64() == (ulong)buffer.Length;
        }
        finally { NativeMethods.CloseHandle(process); }
    }

    private static ScreenPointDto PickScreenPoint(int pid, int timeoutMs)
    {
        int timeout = timeoutMs < 1000 ? 30000 : Math.Min(timeoutMs, 120000);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        bool released = false;
        while (DateTime.UtcNow < deadline)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            uint foregroundPid;
            NativeMethods.GetWindowThreadProcessId(foreground, out foregroundPid);
            bool pressed = (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (!pressed) released = true;
            if (foregroundPid == (uint)pid && released && pressed)
            {
                NativeMethods.POINT point;
                if (NativeMethods.GetCursorPos(out point)) return new ScreenPointDto { x = point.X, y = point.Y, status = "picked" };
            }
            if ((NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0) return new ScreenPointDto { status = "cancelled" };
            System.Threading.Thread.Sleep(25);
        }
        return new ScreenPointDto { status = "timeout" };
    }

    private static IntPtr Open(int pid, uint access)
    {
        IntPtr handle = NativeMethods.OpenProcess(access, false, pid);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("无法打开目标进程，可能是权限不足或进程已退出。");
        return handle;
    }

    private static ulong ParseAddress(string value)
    {
        if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("地址不能为空。");
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
        ulong address;
        if (!UInt64.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out address)) throw new ArgumentException("地址格式无效。");
        return address;
    }

    private static string Hex(IntPtr value) { return "0x" + unchecked((ulong)value.ToInt64()).ToString("X"); }
    private static bool IsReadable(uint protect) { return protect != 0 && (protect & NativeMethods.PAGE_GUARD) == 0 && protect != NativeMethods.PAGE_NOACCESS; }
    private static bool IsWritable(uint protect) { return protect == NativeMethods.PAGE_READWRITE || protect == NativeMethods.PAGE_WRITECOPY || protect == NativeMethods.PAGE_EXECUTE_READWRITE || protect == NativeMethods.PAGE_EXECUTE_WRITECOPY; }
    private static bool IsExecutable(uint protect) { return protect == NativeMethods.PAGE_EXECUTE || protect == NativeMethods.PAGE_EXECUTE_READ || protect == NativeMethods.PAGE_EXECUTE_READWRITE || protect == NativeMethods.PAGE_EXECUTE_WRITECOPY; }
    private static string ProtectionName(uint protect) { return "0x" + protect.ToString("X"); }
    private static string TypeName(uint type) { return type == NativeMethods.MEM_IMAGE ? "Image" : type == NativeMethods.MEM_MAPPED ? "Mapped" : type == NativeMethods.MEM_PRIVATE ? "Private" : "Unknown"; }
}
