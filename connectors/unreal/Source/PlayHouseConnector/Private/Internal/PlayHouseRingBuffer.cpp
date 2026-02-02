#include "PlayHouseRingBuffer.h"

FPlayHouseRingBuffer::FPlayHouseRingBuffer(int32 Capacity)
    : Capacity_(Capacity)
{
    Buffer_.SetNumZeroed(Capacity_);
}

int32 FPlayHouseRingBuffer::GetCount() const
{
    return Count_;
}

int32 FPlayHouseRingBuffer::GetCapacity() const
{
    return Capacity_;
}

int32 FPlayHouseRingBuffer::GetFreeSpace() const
{
    return Capacity_ - Count_;
}

bool FPlayHouseRingBuffer::Write(const uint8* Data, int32 Size)
{
    // Validate input parameters
    if (Data == nullptr && Size > 0)
    {
        return false;
    }

    if (Size <= 0)
    {
        return true; // Writing zero bytes is a no-op success
    }

    if (Size > GetFreeSpace())
    {
        int32 FreeSpace = GetFreeSpace();
        UE_LOG(LogTemp, Error, TEXT("[PlayHouse] RingBuffer overflow! Dropping %d bytes. Buffer capacity: %d, Free: %d"),
               Size, Capacity_, FreeSpace);

        if (OnOverflow)
        {
            OnOverflow(Size, Capacity_, FreeSpace);
        }
        return false;
    }

    int32 Contiguous = Capacity_ - Head_;
    int32 FirstChunk = FMath::Min(Size, Contiguous);

    FMemory::Memcpy(Buffer_.GetData() + Head_, Data, FirstChunk);
    Head_ = (Head_ + FirstChunk) % Capacity_;
    Count_ += FirstChunk;

    if (FirstChunk < Size)
    {
        int32 SecondChunk = Size - FirstChunk;
        FMemory::Memcpy(Buffer_.GetData() + Head_, Data + FirstChunk, SecondChunk);
        Head_ = (Head_ + SecondChunk) % Capacity_;
        Count_ += SecondChunk;
    }

    return true;
}

bool FPlayHouseRingBuffer::Read(uint8* Dest, int32 Size)
{
    // Validate input parameters
    if (Dest == nullptr && Size > 0)
    {
        return false;
    }

    if (Size <= 0)
    {
        return true; // Reading zero bytes is a no-op success
    }

    if (Size > Count_)
    {
        return false; // Insufficient data
    }

    int32 Contiguous = Capacity_ - Tail_;
    int32 FirstChunk = FMath::Min(Size, Contiguous);

    FMemory::Memcpy(Dest, Buffer_.GetData() + Tail_, FirstChunk);
    Tail_ = (Tail_ + FirstChunk) % Capacity_;
    Count_ -= FirstChunk;

    if (FirstChunk < Size)
    {
        int32 SecondChunk = Size - FirstChunk;
        FMemory::Memcpy(Dest + FirstChunk, Buffer_.GetData() + Tail_, SecondChunk);
        Tail_ = (Tail_ + SecondChunk) % Capacity_;
        Count_ -= SecondChunk;
    }

    return true;
}

bool FPlayHouseRingBuffer::Peek(uint8* Dest, int32 Size, int32 Offset) const
{
    // Validate input parameters
    if (Dest == nullptr && Size > 0)
    {
        return false;
    }

    if (Size <= 0)
    {
        return true; // Peeking zero bytes is a no-op success
    }

    // Validate offset is non-negative
    if (Offset < 0)
    {
        return false;
    }

    if (Offset + Size > Count_)
    {
        return false; // Insufficient data
    }

    int32 ReadPos = (Tail_ + Offset) % Capacity_;
    int32 Contiguous = Capacity_ - ReadPos;
    int32 FirstChunk = FMath::Min(Size, Contiguous);

    FMemory::Memcpy(Dest, Buffer_.GetData() + ReadPos, FirstChunk);

    if (FirstChunk < Size)
    {
        int32 SecondChunk = Size - FirstChunk;
        FMemory::Memcpy(Dest + FirstChunk, Buffer_.GetData(), SecondChunk);
    }

    return true;
}

bool FPlayHouseRingBuffer::Consume(int32 Size)
{
    if (Size <= 0)
    {
        return true; // Consuming zero bytes is a no-op success
    }

    if (Size > Count_)
    {
        return false; // Insufficient data
    }

    Tail_ = (Tail_ + Size) % Capacity_;
    Count_ -= Size;
    return true;
}

void FPlayHouseRingBuffer::Clear()
{
    Head_ = 0;
    Tail_ = 0;
    Count_ = 0;
}
