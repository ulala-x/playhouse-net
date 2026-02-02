#pragma once

#include "CoreMinimal.h"

class FPlayHouseRingBuffer
{
public:
    explicit FPlayHouseRingBuffer(int32 Capacity);

    int32 GetCount() const;
    int32 GetCapacity() const;
    int32 GetFreeSpace() const;

    /**
     * Writes data to the buffer.
     * @param Data Pointer to data to write
     * @param Size Number of bytes to write
     * @return true if write succeeded, false if buffer overflow or invalid parameters
     */
    bool Write(const uint8* Data, int32 Size);

    /**
     * Reads and removes data from the buffer.
     * @param Dest Destination buffer
     * @param Size Number of bytes to read
     * @return true if read succeeded, false if insufficient data or invalid parameters
     */
    bool Read(uint8* Dest, int32 Size);

    /**
     * Peeks data without removing from buffer.
     * @param Dest Destination buffer
     * @param Size Number of bytes to peek
     * @param Offset Offset from the read position
     * @return true if peek succeeded, false if insufficient data or invalid parameters
     */
    bool Peek(uint8* Dest, int32 Size, int32 Offset = 0) const;

    /**
     * Consumes (discards) data from the buffer.
     * @param Size Number of bytes to consume
     * @return true if consume succeeded, false if insufficient data
     */
    bool Consume(int32 Size);

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
