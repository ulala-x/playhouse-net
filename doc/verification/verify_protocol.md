# Protocol Format Verification

## Fixed Protocol Format

### Client → Server Packet Structure

**Complete format:**
```
Length(4) + ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Body
```

### Field Details

| Field | Size | Type | Description |
|-------|------|------|-------------|
| Length | 4 bytes | Int32 LE | Total packet size (excluding length field itself) |
| ServiceId | 2 bytes | Int16 LE | Service identifier (0 for default) |
| MsgIdLen | 1 byte | Byte | Length of message ID string |
| MsgId | N bytes | UTF-8 String | Message type name from protobuf |
| MsgSeq | 2 bytes | UInt16 LE | Message sequence number |
| StageId | 8 bytes | Int64 LE | Target stage ID |
| Body | Variable | Bytes | Serialized protobuf payload |

### Changes Made

**Before (WRONG):**
```
MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(4) + OriginalSize(4) + Body
```

**After (CORRECT):**
```
Length(4) + ServiceId(2) + MsgIdLen(1) + MsgId(N) + MsgSeq(2) + StageId(8) + Body
```

**Key fixes:**
1. ✅ Added `ServiceId` (2 bytes, Int16) after length prefix
2. ✅ Changed `StageId` from Int32 (4 bytes) to Int64 (8 bytes)
3. ✅ Removed `OriginalSize` field (only in SERVER → CLIENT responses)
4. ✅ Updated method signatures to accept `long stageId` and `short serviceId`

### Reference Implementation

Source: `D:\project\kairos\playhouse\playhouse-net\PlayHouse\PlayHouseTests\TestClient\TestClientPacket.cs` (lines 57-63)

### Modified Files

- `D:\project\ulalax\playhouse-net\connector\PlayHouse.Connector\Protocol\PacketEncoder.cs`
  - Updated `EncodeMessage()` method
  - Updated `EncodeWithLengthPrefix()` method
  - Added `serviceId` parameter with default value 0
  - Changed `stageId` from `int` to `long`

### Verification

Build status: ✅ SUCCESS (0 errors, 0 warnings)

All existing call sites continue to work due to:
- Default parameter values (`serviceId = 0`)
- Named parameters in PlayHouseClient.cs
- Backward-compatible method signatures
