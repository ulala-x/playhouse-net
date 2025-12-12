using System;

namespace PlayHouse.Connector.Infrastructure.Utils;

public class XBitConverter
{
    public static ushort ToNetworkOrder(ushort value)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(value);
        }

        return value;
    }

    public static int ToNetworkOrder(int value)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(value);
        }

        return value;
    }

    public static ulong ToNetworkOrder(ulong value)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(value);
        }

        return value;
    }

    public static long ToNetworkOrder(long value)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(value);
        }

        return value;
    }

    public static void ToByteArray(ushort value, byte[] buffer, int offset, int size)
    {
        if (size < 2)
        {
            throw new ArgumentException($"buffer size is too short : {size}");
        }

        var networkOrderValue = ToNetworkOrder(value);
        buffer[offset] = (byte)((networkOrderValue & 0xFF00) >> 8); // 상위 바이트 (하이 바이트)
        buffer[offset + 1] = (byte)(networkOrderValue & 0x00FF); // 하위 바이트 (로우 바이트)
    }

    public static short ByteArrayToShort(byte[] buffer, int offset, int size)
    {
        if (size != 2)
        {
            throw new ArgumentException("Byte array must have a length of 2.");
        }

        var result = (short)((buffer[offset] << 8) | buffer[offset + 1]);
        return result;
    }

    public static int ByteArrayToInt(byte[] buffer, int offset, int size)
    {
        if (size != 4)
        {
            throw new ArgumentException("Byte array must have a length of 4.");
        }

        var result = (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) |
                     buffer[offset + 3];
        return result;
    }

    public static ushort ToHostOrder(ushort networkOrderValue)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(networkOrderValue);
        }

        return networkOrderValue;
    }

    public static int ToHostOrder(int networkOrderValue)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(networkOrderValue);
        }

        return networkOrderValue;
    }

    public static ulong ToHostOrder(ulong networkOrderValue)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(networkOrderValue);
        }

        return networkOrderValue;
    }

    public static long ToHostOrder(long networkOrderValue)
    {
        if (BitConverter.IsLittleEndian)
        {
            return SwapBytes(networkOrderValue);
        }

        return networkOrderValue;
    }

    private static ushort SwapBytes(ushort value)
    {
        return (ushort)((value >> 8) | (value << 8));
    }

    private static int SwapBytes(int value)
    {
        return (SwapBytes((ushort)value) << 16) |
               SwapBytes((ushort)(value >> 16));
    }

    private static ulong SwapBytes(ulong value)
    {
        return ((ulong)SwapBytes((uint)value) << 32) |
               SwapBytes((uint)(value >> 32));
    }

    private static long SwapBytes(long value)
    {
        return (long)SwapBytes((ulong)value);
    }

    private static uint SwapBytes(uint value)
    {
        return ((value >> 24) & 0x000000FF) |
               ((value >> 8) & 0x0000FF00) |
               ((value << 8) & 0x00FF0000) |
               ((value << 24) & 0xFF000000);
    }
}
