#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <iostream>

#include "../../src/RayaTrainer.Agent/NativePointerSet.h"

namespace
{
int g_ns_failures = 0;

void Expect(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << '\n';
        ++g_ns_failures;
    }
}

void TestBasicInsertContainsAndCount()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    Expect(set.Insert(5), "insert new key 5");
    Expect(set.Contains(5), "contains inserted key 5");
    Expect(!set.Contains(99), "does not contain missing key 99");
    Expect(set.Count() == 1, "count is 1 after one insert");
    Expect(!set.Insert(5), "duplicate insert returns false");
    Expect(set.Count() == 1, "count unchanged after duplicate insert");
}

void TestRemoveExistingAndMissing()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    set.Insert(5);
    Expect(set.Remove(5), "remove existing key returns true");
    Expect(!set.Contains(5), "removed key is no longer contained");
    Expect(set.Count() == 0, "count is 0 after removing only element");
    Expect(!set.Remove(5), "remove missing key returns false");
}

void TestZeroKeyRejected()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    Expect(!set.Insert(0), "zero key rejected by Insert");
    Expect(!set.Contains(0), "zero key not contained");
    const uint32_t keys[] = { 0, 1 };
    Expect(set.Apply(keys, 2, true), "batch with zero and valid key succeeds");
    Expect(set.Contains(1), "valid key inserted despite zero in batch");
    Expect(!set.Contains(0), "zero key still not inserted");
    Expect(set.Count() == 1, "only valid key counted after batch with zero");
}

void TestDuplicateBatchKeysDeduplicated()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    const uint32_t keys[] = { 1, 1, 2, 2, 3 };
    Expect(set.Apply(keys, 5, true), "batch with duplicates succeeds");
    Expect(set.Count() == 3, "duplicates deduplicated in batch count");
    Expect(set.Contains(1) && set.Contains(2) && set.Contains(3), "all unique keys inserted from batch");
}

void TestSeventyPercentThresholdAndRejectedBatchAtomicity()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    const uint32_t fill[] = { 1, 2, 3, 4, 5, 6, 7 };
    const uint32_t overflow[] = { 8 };
    Expect(set.Apply(fill, 7, true), "70 percent batch fills set");
    Expect(set.Count() == 7, "count is 7 after batch");
    Expect(set.Contains(1) && set.Contains(7), "first and last batch keys inserted");
    Expect(!set.Apply(overflow, 1, true), "over-capacity batch rejects");
    Expect(set.Count() == 7 && !set.Contains(8), "rejection is atomic - state unchanged");
    // Single insert also respects threshold
    Expect(!set.Insert(8), "single insert rejected at max capacity");
    Expect(set.Count() == 7, "count still 7 after rejected single insert");
}

void TestCollisionWithDuplicateAfterTombstone()
{
    using namespace RayaTrainer::agent;
    // Knuth hash multiplier 2654435761 ≡ 1 (mod 10), so keys with the same
    // last decimal digit collide for Capacity=10. 1, 11, 21 all map to bucket 1.
    NativePointerSet<10> set;

    set.Insert(1);   // bucket 1 → slot 1
    set.Insert(11);  // bucket 1, slot 1 occupied (key=1), probes → slot 2

    Expect(set.Contains(1), "first key found");
    Expect(set.Contains(11), "second colliding key found via linear probe");

    set.Remove(1);   // slot 1 → Tombstone

    // Duplicate-after-tombstone: Insert(11) must continue past the tombstone
    // at slot 1, find the duplicate at slot 2, and reject insertion.
    Expect(!set.Insert(11), "duplicate key rejected past tombstone");
    Expect(set.Count() == 1, "count unchanged after duplicate insert past tombstone");

    // New colliding key 21: must reuse the tombstone at slot 1 since no
    // duplicate exists further in the chain (slot 2 holds 11, slot 3 empty).
    Expect(set.Insert(21), "new colliding key inserted via tombstone reuse");
    Expect(set.Contains(21), "new colliding key present");
    Expect(set.Count() == 2, "count 2 after tombstone-reuse insert of new key");
}

void TestTombstoneReuse()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    set.Insert(1);
    set.Remove(1);
    Expect(set.Count() == 0, "count 0 after remove in tombstone test");
    // Colliding key reuses the tombstone slot
    Expect(set.Insert(11), "insert colliding key after tombstone");
    Expect(set.Contains(11), "colliding key found after tombstone-reuse insert");
    Expect(set.Count() == 1, "count 1 after tombstone-reuse insert");
    Expect(!set.Contains(1), "original removed key still absent");
}

void TestClear()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    const uint32_t keys[] = { 1, 2, 3, 4, 5 };
    set.Apply(keys, 5, true);
    set.Clear();
    Expect(set.Count() == 0, "count 0 after clear");
    Expect(!set.Contains(1) && !set.Contains(2) && !set.Contains(3), "keys gone after clear");
    // Re-insert after clear works
    Expect(set.Apply(keys, 5, true), "re-insert after clear succeeds");
    Expect(set.Count() == 5, "count 5 after re-insert");
}

void TestRemovalBatch()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    const uint32_t insertKeys[] = { 1, 2, 3, 4, 5 };
    set.Apply(insertKeys, 5, true);
    const uint32_t removeKeys[] = { 1, 2, 3 };
    Expect(set.Apply(removeKeys, 3, false), "removal batch succeeds");
    Expect(set.Count() == 2, "removal batch removed 3 of 5");
    Expect(!set.Contains(1) && !set.Contains(2) && !set.Contains(3), "batch-removed keys gone");
    Expect(set.Contains(4) && set.Contains(5), "non-removed keys remain");
}

void TestRemovalBatchOfMissingKeys()
{
    using namespace RayaTrainer::agent;
    NativePointerSet<10> set;
    const uint32_t insertKeys[] = { 1, 2 };
    set.Apply(insertKeys, 2, true);
    const uint32_t removeKeys[] = { 3, 4 };
    Expect(set.Apply(removeKeys, 2, false), "removal always succeeds even for absent keys");
    Expect(set.Count() == 2, "count unchanged after removing absent keys");
    Expect(set.Contains(1) && set.Contains(2), "original keys untouched");
}

void TestBoundedProbeMaxCapacity()
{
    using namespace RayaTrainer::agent;
    // Force every slot to be occupied (except one) to test probe bound
    NativePointerSet<3> set;  // kMaximumOccupied = 2, capacity = 3
    // Fill to max
    set.Insert(1);
    set.Insert(2);
    Expect(set.Count() == 2, "small set at max occupancy");
    Expect(!set.Insert(3), "insert rejected when at max capacity");
    // Remove and re-insert should work (tombstone reuse)
    set.Remove(1);
    Expect(set.Insert(3), "insert works after removal frees slot");
    Expect(set.Contains(3), "new key found");
}

void TestApplyAtomicWithExistingKeys()
{
    using namespace RayaTrainer::agent;
    // Capacity=10, kMaximumOccupied=7
    NativePointerSet<10> set;
    const uint32_t first[] = { 1, 2, 3, 4, 5 };
    Expect(set.Apply(first, 5, true), "fill set with 5 keys");

    // Batch where 3 keys exist (1,2,3) and 3 are new (6,7,8).
    // distinctMissing = 3, m_count+3 = 8 > 7 → reject atomically.
    const uint32_t attempt[] = { 1, 2, 3, 6, 7, 8 };
    Expect(!set.Apply(attempt, 6, true),
           "batch with mix of existing+new keys rejected when over threshold");

    // State must be completely unchanged.
    Expect(set.Count() == 5, "count unchanged after rejected batch");
    Expect(set.Contains(1) && set.Contains(2) && set.Contains(3)
           && set.Contains(4) && set.Contains(5),
           "all original keys still present");
    Expect(!set.Contains(6) && !set.Contains(7) && !set.Contains(8),
           "no new key inserted after atomic rejection");
}
}

int RunNativePointerSetTests()
{
    TestBasicInsertContainsAndCount();
    TestRemoveExistingAndMissing();
    TestZeroKeyRejected();
    TestDuplicateBatchKeysDeduplicated();
    TestSeventyPercentThresholdAndRejectedBatchAtomicity();
    TestCollisionWithDuplicateAfterTombstone();
    TestTombstoneReuse();
    TestClear();
    TestRemovalBatch();
    TestRemovalBatchOfMissingKeys();
    TestBoundedProbeMaxCapacity();
    TestApplyAtomicWithExistingKeys();

    if (g_ns_failures != 0)
    {
        std::cerr << g_ns_failures << " NativePointerSet test(s) FAILED\n";
    }
    return g_ns_failures;
}
