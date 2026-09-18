using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KillWind.Wpf
{
    internal static class PointerParser
    {
        public static ulong ParseHex(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) throw new FormatException("偏移不能为空。");
            string value = text.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value.Substring(2);
            ulong result;
            if (!UInt64.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result)) throw new FormatException("偏移格式无效：" + text);
            return result;
        }

        public static ulong[] ParseOffsets(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return new ulong[0];
            string normalized = text.Replace("->", " ").Replace(",", " ").Replace(";", " ");
            string[] parts = normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Select(ParseHex).ToArray();
        }

        public static string Format(ulong value)
        {
            return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class PointerResolver
    {
        private readonly NativeBridgeClient bridge;

        public PointerResolver(NativeBridgeClient bridge)
        {
            this.bridge = bridge;
        }

        public async System.Threading.Tasks.Task<ulong> ResolveAsync(ProcessInfo process, ModuleInfo module, ulong moduleOffset, IList<ulong> offsets)
        {
            if (process == null) throw new InvalidOperationException("未连接目标进程。");
            if (module == null) throw new InvalidOperationException("请选择模块。");
            ulong current = checked(module.Base + moduleOffset);
            int pointerSize = String.Equals(process.architecture, "x86", StringComparison.OrdinalIgnoreCase) ? 4 : 8;
            foreach (ulong offset in offsets ?? new ulong[0])
            {
                byte[] pointerBytes = await bridge.ReadAsync(process.pid, current, pointerSize);
                if (pointerBytes == null || pointerBytes.Length != pointerSize) throw new InvalidOperationException("指针地址不可读：" + PointerParser.Format(current));
                ulong pointer = pointerSize == 4 ? BitConverter.ToUInt32(pointerBytes, 0) : BitConverter.ToUInt64(pointerBytes, 0);
                if (pointer == 0) throw new InvalidOperationException("指针链在 " + PointerParser.Format(current) + " 处为空。");
                current = checked(pointer + offset);
            }
            return current;
        }
    }
}
