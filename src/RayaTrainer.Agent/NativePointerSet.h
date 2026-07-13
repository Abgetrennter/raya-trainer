#pragma once

#include <cstdint>

namespace RayaTrainer::agent
{

enum class NativePointerSlotState : uint8_t
{
    Empty = 0,
    Occupied = 1,
    Tombstone = 2
};

struct NativePointerSlot
{
    uint32_t Key = 0;
    NativePointerSlotState State = NativePointerSlotState::Empty;
};

template<uint32_t Capacity>
class NativePointerSet
{
    static_assert(Capacity > 0, "NativePointerSet capacity must be greater than 0");

    static constexpr uint32_t kMaximumOccupied = (Capacity * 70u) / 100u;

public:
    /// Returns true if @p key is present in the set.
    bool Contains(uint32_t key) const
    {
        if (key == 0) return false;

        const uint32_t start = Hash(key);
        for (uint32_t i = 0; i < Capacity; ++i)
        {
            const uint32_t idx = (start + i) % Capacity;
            if (m_slots[idx].State == NativePointerSlotState::Occupied &&
                m_slots[idx].Key == key)
            {
                return true;
            }
            if (m_slots[idx].State == NativePointerSlotState::Empty)
            {
                return false;
            }
        }
        return false;
    }

    /// Inserts @p key. Returns false if zero, duplicate, or at max capacity.
    ///
    /// Remembers the first tombstone encountered during probing but continues
    /// past it to check for duplicates further in the chain. This guarantees
    /// no duplicate entry is created when a tombstone precedes the occupied
    /// slot holding the same key.
    bool Insert(uint32_t key)
    {
        if (key == 0) return false;
        if (m_count >= kMaximumOccupied) return false;

        const uint32_t start = Hash(key);
        int firstTombstone = -1;

        for (uint32_t i = 0; i < Capacity; ++i)
        {
            const uint32_t idx = (start + i) % Capacity;
            if (m_slots[idx].State == NativePointerSlotState::Occupied)
            {
                if (m_slots[idx].Key == key) return false; // duplicate
            }
            else if (m_slots[idx].State == NativePointerSlotState::Tombstone)
            {
                // Remember first tombstone but keep probing for duplicates.
                if (firstTombstone < 0)
                    firstTombstone = static_cast<int>(idx);
            }
            else // Empty
            {
                const uint32_t insertIdx = (firstTombstone >= 0)
                    ? static_cast<uint32_t>(firstTombstone)
                    : idx;
                m_slots[insertIdx].Key = key;
                m_slots[insertIdx].State = NativePointerSlotState::Occupied;
                ++m_count;
                return true;
            }
        }

        // Probe exhausted — insert at first tombstone if found.
        if (firstTombstone >= 0)
        {
            m_slots[firstTombstone].Key = key;
            m_slots[firstTombstone].State = NativePointerSlotState::Occupied;
            ++m_count;
            return true;
        }
        return false;
    }

    /// Removes @p key. Returns false if not found (or zero key).
    bool Remove(uint32_t key)
    {
        if (key == 0) return false;

        const uint32_t start = Hash(key);
        for (uint32_t i = 0; i < Capacity; ++i)
        {
            const uint32_t idx = (start + i) % Capacity;
            if (m_slots[idx].State == NativePointerSlotState::Occupied &&
                m_slots[idx].Key == key)
            {
                m_slots[idx].State = NativePointerSlotState::Tombstone;
                --m_count;
                return true;
            }
            if (m_slots[idx].State == NativePointerSlotState::Empty)
            {
                return false;
            }
        }
        return false;
    }

    /// Resets all slots to empty.
    void Clear()
    {
        for (auto& slot : m_slots)
        {
            slot.Key = 0;
            slot.State = NativePointerSlotState::Empty;
        }
        m_count = 0;
    }

    /// Returns the number of occupied slots.
    uint32_t Count() const { return m_count; }

    /// Preflight check: returns true if Apply(keys, count, enable) would succeed.
    ///
    /// Non-mutating. Shares counting logic with Apply so the preflight result
    /// is consistent with the actual mutation.
    bool CanApply(const uint32_t* keys, uint32_t count, bool enable) const
    {
        if (!enable) return true; // removal always succeeds
        return m_count + CountDistinctMissing(keys, count) <= kMaximumOccupied;
    }

    /// Batch operation.
    ///
    /// When @p enable is true: inserts distinct non-zero keys from the batch.
    /// Counts missing distinct keys before any mutation; rejects the entire
    /// batch if the result would exceed kMaximumOccupied.
    ///
    /// When @p enable is false: removes each distinct key from the set.
    /// Always returns true.
    bool Apply(const uint32_t* keys, uint32_t count, bool enable)
    {
        if (enable)
        {
            // Phase 1: count distinct missing keys without mutation.
            uint32_t distinctMissing = CountDistinctMissing(keys, count);

            // Phase 2: reject if threshold would be exceeded.
            if (m_count + distinctMissing > kMaximumOccupied) return false;

            // Phase 3: insert all distinct keys.
            for (uint32_t i = 0; i < count; ++i)
            {
                if (keys[i] != 0) Insert(keys[i]);
            }
            return true;
        }
        else
        {
            // Removal mode — always succeeds.
            for (uint32_t i = 0; i < count; ++i)
            {
                if (keys[i] != 0) Remove(keys[i]);
            }
            return true;
        }
    }

private:
    /// Counts distinct non-zero keys from the input that are not already in the set.
    /// Shared by CanApply and Apply so preflight is consistent with mutation.
    uint32_t CountDistinctMissing(const uint32_t* keys, uint32_t count) const
    {
        uint32_t distinctMissing = 0;
        for (uint32_t i = 0; i < count; ++i)
        {
            if (keys[i] == 0) continue;
            // Deduplicate within input.
            bool alreadySeen = false;
            for (uint32_t j = 0; j < i; ++j)
            {
                if (keys[j] == keys[i])
                {
                    alreadySeen = true;
                    break;
                }
            }
            if (alreadySeen) continue;
            if (!Contains(keys[i])) ++distinctMissing;
        }
        return distinctMissing;
    }

    static uint32_t Hash(uint32_t key)
    {
        // Multiplicative hash (Knuth's golden ratio) for decent distribution.
        return key * 2654435761u;
    }

    NativePointerSlot m_slots[Capacity] = {};
    uint32_t m_count = 0;
};

} // namespace RayaTrainer::agent
