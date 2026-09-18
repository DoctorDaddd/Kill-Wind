using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KillWind.Wpf
{
    internal sealed class NativeBridgeClient : IDisposable
    {
        private readonly Process process;
        private readonly StreamWriter writer;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        private readonly object writeLock = new object();
        private readonly object pendingLock = new object();
        private readonly Dictionary<string, TaskCompletionSource<string>> pending = new Dictionary<string, TaskCompletionSource<string>>();

        public NativeBridgeClient(string helperPath)
        {
            process = new Process { StartInfo = new ProcessStartInfo(helperPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            if (!process.Start()) throw new InvalidOperationException("无法启动原生内存桥接。");
            writer = process.StandardInput;
            Task.Run(new Action(ReadResponses));
        }

        public async Task<ProcessInfo[]> ListProcessesAsync(bool taskbarOnly)
        {
            var payload = new Dictionary<string, object>(); payload["taskbarOnly"] = taskbarOnly;
            return await CallDataAsync<ProcessInfo[]>("list", payload);
        }

        public async Task<MemoryRegion[]> ListRegionsAsync(int pid)
        {
            var payload = new Dictionary<string, object>(); payload["pid"] = pid;
            return await CallDataAsync<MemoryRegion[]>("regions", payload);
        }

        public async Task<ModuleInfo[]> ListModulesAsync(int pid)
        {
            var payload = new Dictionary<string, object>(); payload["pid"] = pid;
            return await CallDataAsync<ModuleInfo[]>("modules", payload);
        }

        public async Task<byte[]> ReadAsync(int pid, ulong address, int size)
        {
            var payload = new Dictionary<string, object>(); payload["pid"] = pid; payload["address"] = String.Format("0x{0:X}", address); payload["size"] = size;
            string encoded = await CallDataAsync<string>("read", payload);
            return Convert.FromBase64String(encoded);
        }

        public async Task<bool> WriteAsync(int pid, ulong address, byte[] bytes)
        {
            var payload = new Dictionary<string, object>(); payload["pid"] = pid; payload["address"] = String.Format("0x{0:X}", address); payload["data"] = Convert.ToBase64String(bytes);
            return await CallDataAsync<bool>("write", payload);
        }

        public async Task<ScreenPoint> PickScreenPointAsync(int pid, int timeoutMs)
        {
            var payload = new Dictionary<string, object>(); payload["pid"] = pid; payload["size"] = timeoutMs;
            return await CallDataAsync<ScreenPoint>("pick", payload);
        }

        private async Task<T> CallDataAsync<T>(string operation, Dictionary<string, object> payload)
        {
            string data = await CallAsync(operation, payload);
            return serializer.Deserialize<T>(data);
        }

        private Task<string> CallAsync(string operation, Dictionary<string, object> payload)
        {
            string id = Guid.NewGuid().ToString("N");
            var request = new Dictionary<string, object>(payload); request["id"] = id; request["op"] = operation;
            var completion = new TaskCompletionSource<string>();
            lock (pendingLock) pending[id] = completion;
            try
            {
                lock (writeLock) { writer.WriteLine(serializer.Serialize(request)); writer.Flush(); }
            }
            catch (Exception error)
            {
                lock (pendingLock) pending.Remove(id);
                completion.SetException(error);
            }
            return completion.Task;
        }

        private void ReadResponses()
        {
            try
            {
                string line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    Dictionary<string, object> response = serializer.Deserialize<Dictionary<string, object>>(line);
                    string id = Convert.ToString(response["id"]);
                    TaskCompletionSource<string> completion;
                    lock (pendingLock) { if (!pending.TryGetValue(id, out completion)) continue; pending.Remove(id); }
                    if (!Convert.ToBoolean(response["ok"])) completion.SetException(new InvalidOperationException(Convert.ToString(response["error"])));
                    else completion.SetResult(Convert.ToString(response["data"]));
                }
            }
            catch (Exception error)
            {
                lock (pendingLock) foreach (TaskCompletionSource<string> completion in pending.Values) completion.TrySetException(error);
                lock (pendingLock) pending.Clear();
            }
        }

        public void Dispose()
        {
            try { if (!process.HasExited) process.Kill(); } catch (Exception error) { Debug.WriteLine("关闭原生桥接失败：" + error.Message); }
            process.Dispose();
        }
    }
}
