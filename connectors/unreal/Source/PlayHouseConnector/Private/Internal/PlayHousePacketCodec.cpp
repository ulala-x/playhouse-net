#include "PlayHousePacketCodec.h"
#include "PlayHouseProtocol.h"

namespace
{
    void WriteUInt8(TArray<uint8>& Buffer, uint8 Value)
    {
        Buffer.Add(Value);
    }

    void WriteUInt16LE(TArray<uint8>& Buffer, uint16 Value)
    {
        Buffer.Add(static_cast<uint8>(Value & 0xFF));
        Buffer.Add(static_cast<uint8>((Value >> 8) & 0xFF));
    }

    void WriteUInt32LE(TArray<uint8>& Buffer, uint32 Value)
    {
        Buffer.Add(static_cast<uint8>(Value & 0xFF));
        Buffer.Add(static_cast<uint8>((Value >> 8) & 0xFF));
        Buffer.Add(static_cast<uint8>((Value >> 16) & 0xFF));
        Buffer.Add(static_cast<uint8>((Value >> 24) & 0xFF));
    }

    void WriteInt64LE(TArray<uint8>& Buffer, int64 Value)
    {
        uint64 UValue = static_cast<uint64>(Value);
        Buffer.Add(static_cast<uint8>(UValue & 0xFF));
        Buffer.Add(static_cast<uint8>((UValue >> 8) & 0xFF));
        Buffer.Add(static_cast<uint8>((UValue >> 16) & 0xFF));
        Buffer.Add(static_cast<uint8>((UValue >> 24) & 0xFF));
        Buffer.Add(static_cast<uint8>((UValue >> 32) & 0xFF));
        Buffer.Add(static_cast<uint8>((UValue >> 40) & 0xFF));
        Buffer.Add(static_cast<uint8>((UValue >> 48) & 0xFF));
        Buffer.Add(static_cast<uint8>((UValue >> 56) & 0xFF));
    }

    uint32 ReadUInt32LE(const uint8* Data)
    {
        return static_cast<uint32>(Data[0]) |
               (static_cast<uint32>(Data[1]) << 8) |
               (static_cast<uint32>(Data[2]) << 16) |
               (static_cast<uint32>(Data[3]) << 24);
    }

    uint16 ReadUInt16LE(const uint8* Data)
    {
        return static_cast<uint16>(Data[0]) |
               (static_cast<uint16>(Data[1]) << 8);
    }

    int16 ReadInt16LE(const uint8* Data)
    {
        return static_cast<int16>(ReadUInt16LE(Data));
    }

    int64 ReadInt64LE(const uint8* Data)
    {
        uint64 UValue =
            static_cast<uint64>(Data[0]) |
            (static_cast<uint64>(Data[1]) << 8) |
            (static_cast<uint64>(Data[2]) << 16) |
            (static_cast<uint64>(Data[3]) << 24) |
            (static_cast<uint64>(Data[4]) << 32) |
            (static_cast<uint64>(Data[5]) << 40) |
            (static_cast<uint64>(Data[6]) << 48) |
            (static_cast<uint64>(Data[7]) << 56);
        return static_cast<int64>(UValue);
    }
}

bool FPlayHousePacketCodec::EncodeRequest(const FPlayHousePacket& Packet, TArray<uint8>& OutBytes)
{
    FTCHARToUTF8 MsgIdUtf8(*Packet.MsgId);
    int32 MsgIdLen = MsgIdUtf8.Length();

    if (MsgIdLen <= 0 || MsgIdLen > static_cast<int32>(PlayHouse::Protocol::MaxMsgIdLength))
    {
        return false;
    }

    uint32 ContentSize = 1 + static_cast<uint32>(MsgIdLen) + 2 + 8 + static_cast<uint32>(Packet.Payload.Num());

    OutBytes.Reset();
    OutBytes.Reserve(4 + ContentSize);

    WriteUInt32LE(OutBytes, ContentSize);
    WriteUInt8(OutBytes, static_cast<uint8>(MsgIdLen));
    OutBytes.Append(reinterpret_cast<const uint8*>(MsgIdUtf8.Get()), MsgIdLen);
    WriteUInt16LE(OutBytes, Packet.MsgSeq);
    WriteInt64LE(OutBytes, Packet.StageId);
    OutBytes.Append(Packet.Payload);

    return true;
}

bool FPlayHousePacketCodec::DecodeResponse(const uint8* Data, int32 Size, FPlayHousePacket& OutPacket)
{
    // Validate minimum size for header
    if (Size < static_cast<int32>(PlayHouse::Protocol::MinHeaderSize))
    {
        return false;
    }

    // Validate that Data pointer is not null
    if (Data == nullptr)
    {
        return false;
    }

    // Read and validate content size from the first 4 bytes
    uint32 ContentSize = ReadUInt32LE(Data);

    // Validate content size is reasonable (prevent integer overflow)
    if (ContentSize > PlayHouse::Protocol::MaxBodySize)
    {
        return false;
    }

    // Validate that the declared content size matches the actual data
    // Size should be at least 4 (size prefix) + ContentSize
    if (Size < static_cast<int32>(4 + ContentSize))
    {
        return false;
    }

    int32 Offset = 4;
    int32 ContentEnd = static_cast<int32>(4 + ContentSize);

    uint8 MsgIdLen = Data[Offset++];

    // Validate MsgIdLen: must be > 0 and within bounds
    if (MsgIdLen == 0 || MsgIdLen > PlayHouse::Protocol::MaxMsgIdLength)
    {
        return false;
    }

    // Validate MsgId length doesn't exceed content bounds
    if (Offset + static_cast<int32>(MsgIdLen) > ContentEnd)
    {
        return false;
    }

    if (Offset + MsgIdLen > Size)
    {
        return false;
    }

    {
        FUTF8ToTCHAR Converter(reinterpret_cast<const ANSICHAR*>(Data + Offset), MsgIdLen);
        OutPacket.MsgId = FString(Converter.Length(), Converter.Get());
    }
    Offset += MsgIdLen;

    // Validate MsgSeq field (2 bytes)
    if (Offset + 2 > ContentEnd || Offset + 2 > Size)
    {
        return false;
    }
    OutPacket.MsgSeq = ReadUInt16LE(Data + Offset);
    Offset += 2;

    // Validate StageId field (8 bytes)
    if (Offset + 8 > ContentEnd || Offset + 8 > Size)
    {
        return false;
    }
    OutPacket.StageId = ReadInt64LE(Data + Offset);
    Offset += 8;

    // Validate ErrorCode field (2 bytes)
    if (Offset + 2 > ContentEnd || Offset + 2 > Size)
    {
        return false;
    }
    OutPacket.ErrorCode = ReadInt16LE(Data + Offset);
    Offset += 2;

    // Validate OriginalSize field (4 bytes)
    if (Offset + 4 > ContentEnd || Offset + 4 > Size)
    {
        return false;
    }
    OutPacket.OriginalSize = ReadUInt32LE(Data + Offset);
    Offset += 4;

    // Calculate and validate payload size
    OutPacket.Payload.Reset();
    if (Offset < ContentEnd)
    {
        int32 PayloadSize = ContentEnd - Offset;

        // Validate payload size is non-negative and within bounds
        if (PayloadSize < 0 || Offset + PayloadSize > Size)
        {
            return false;
        }

        // Additional safety check for payload size
        if (PayloadSize > static_cast<int32>(PlayHouse::Protocol::MaxBodySize))
        {
            return false;
        }

        OutPacket.Payload.Append(Data + Offset, PayloadSize);
    }

    return true;
}
