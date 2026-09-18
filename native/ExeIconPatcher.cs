using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

internal static class ExeIconPatcher
{
    private const uint RtIcon = 3;
    private const uint RtGroupIcon = 14;
    private const ushort IconLanguage = 0x0409;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResource(string fileName, bool deleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateResource(
        IntPtr updateHandle,
        IntPtr type,
        IntPtr name,
        ushort language,
        [In] byte[] data,
        uint dataSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EndUpdateResource(IntPtr updateHandle, bool discard);

    private static IntPtr ResourceId(ushort id)
    {
        return new IntPtr(id);
    }

    private static void ThrowWin32(string message)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), message);
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        return (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

    public static int Main(string[] args)
    {
        IntPtr updateHandle = IntPtr.Zero;
        try
        {
            if (args.Length != 2) throw new ArgumentException("用法：ExeIconPatcher.exe <exe> <ico>");
            byte[] ico = File.ReadAllBytes(args[1]);
            if (ico.Length < 22 || ReadUInt16(ico, 0) != 0 || ReadUInt16(ico, 2) != 1 || ReadUInt16(ico, 4) < 1)
                throw new InvalidDataException("ICO 文件格式无效。");

            uint imageSize = ReadUInt32(ico, 14);
            uint imageOffset = ReadUInt32(ico, 18);
            if (imageSize == 0 || imageOffset + imageSize > ico.Length || imageSize > int.MaxValue)
                throw new InvalidDataException("ICO 图像数据无效。");

            byte[] image = new byte[(int)imageSize];
            Buffer.BlockCopy(ico, (int)imageOffset, image, 0, image.Length);

            // GRPICONDIR uses the ICO entry without the file offset and replaces it with a resource ID.
            byte[] group = new byte[20];
            Buffer.BlockCopy(ico, 6, group, 6, 12);
            WriteUInt16(group, 18, 1);

            updateHandle = BeginUpdateResource(args[0], false);
            if (updateHandle == IntPtr.Zero) ThrowWin32("无法打开 EXE 资源。");
            if (!UpdateResource(updateHandle, ResourceId((ushort)RtIcon), ResourceId(1), IconLanguage, image, (uint)image.Length))
                ThrowWin32("无法写入 RT_ICON 资源。");
            if (!UpdateResource(updateHandle, ResourceId((ushort)RtGroupIcon), ResourceId(1), IconLanguage, group, (uint)group.Length))
                ThrowWin32("无法写入 RT_GROUP_ICON 资源。");
            if (!EndUpdateResource(updateHandle, false)) ThrowWin32("无法保存 EXE 图标资源。");
            updateHandle = IntPtr.Zero;
            Console.WriteLine("EXE icon resource updated.");
            return 0;
        }
        catch (Exception error)
        {
            if (updateHandle != IntPtr.Zero) EndUpdateResource(updateHandle, true);
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
