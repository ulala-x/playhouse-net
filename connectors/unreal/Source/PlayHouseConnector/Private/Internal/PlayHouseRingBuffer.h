#pragma once

#include "CoreMinimal.h"

class FPlayHouseRingBuffer
{
public:
    explicit FPlayHouseRingBuffer(int32 Capacity);

    int32 GetCount() const;
    int32 GetCapacity() const;
    int32 GetFreeSpace() const;

    void Write(const uint8* Data, int32 Size);
    void Read(uint8* Dest, int32 Size);
    void Peek(uint8* Dest, int32 Size, int32 Offset = 0) const;
    void Consume(int32 Size);
    void Clear();

    /** Called when buffer overflows. Parameters: (BytesDropped, BufferCapacity, FreeSpace) */
    TFunction<void(int32, int32, int32)> OnOverflow;

private:
    TArray<uint8> Buffer_;
    int32 Capacity_ = 0;
    int32 Head_ = 0;
    int32 Tail_ = 0;
    int32 Count_ = 0;
};
