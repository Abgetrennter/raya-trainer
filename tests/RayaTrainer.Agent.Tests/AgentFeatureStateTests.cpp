#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <cstring>
#include <iostream>

#include "../../src/RayaTrainer.Agent/AgentFeatureState.h"

namespace
{
int g_afs_failures = 0;

void Expect(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << '\n';
        ++g_afs_failures;
    }
}

// Helper: build a v11 SetFeatureStates payload.
// Format: uint32_t Count, then Count x {uint32_t StateId, uint32_t Value}.
unsigned char* BuildV11Payload(uint32_t count, const uint32_t* ids, const uint32_t* values, uint32_t& outLength)
{
    outLength = sizeof(uint32_t) + count * (sizeof(uint32_t) * 2);
    auto* buf = new unsigned char[outLength];
    std::memcpy(buf, &count, sizeof(count));
    for (uint32_t i = 0; i < count; ++i)
    {
        const auto offset = sizeof(uint32_t) + i * (sizeof(uint32_t) * 2);
        std::memcpy(buf + offset, &ids[i], sizeof(ids[i]));
        std::memcpy(buf + offset + sizeof(uint32_t), &values[i], sizeof(values[i]));
    }
    return buf;
}

// Helper: build a v10 (legacy) SetFeatureStates payload.
// Format: uint32_t Count, then Count x {uint32_t StateId, uint32_t AddressMode, uint32_t ByteCount, uint32_t Value}.
unsigned char* BuildV10Payload(uint32_t count, const uint32_t* ids, const uint32_t* modes, const uint32_t* byteCounts, const uint32_t* values, uint32_t& outLength)
{
    outLength = sizeof(uint32_t) + count * (sizeof(uint32_t) * 4);
    auto* buf = new unsigned char[outLength];
    std::memcpy(buf, &count, sizeof(count));
    for (uint32_t i = 0; i < count; ++i)
    {
        const auto offset = sizeof(uint32_t) + i * (sizeof(uint32_t) * 4);
        std::memcpy(buf + offset, &ids[i], sizeof(ids[i]));
        std::memcpy(buf + offset + sizeof(uint32_t), &modes[i], sizeof(modes[i]));
        std::memcpy(buf + offset + sizeof(uint32_t) * 2, &byteCounts[i], sizeof(byteCounts[i]));
        std::memcpy(buf + offset + sizeof(uint32_t) * 3, &values[i], sizeof(values[i]));
    }
    return buf;
}

// v10 entry shape: 4 x uint32_t = 16 bytes
void TestSetFeatureStates_FromV10LegacyShape_RejectsAsInvalidCommand()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // v10 format: Count=1, then {stateId=2, addressMode=0, byteCount=4, value=999}
    const uint32_t ids[] = {2};
    const uint32_t modes[] = {0};
    const uint32_t byteCounts[] = {4};
    const uint32_t values[] = {999};
    uint32_t length = 0;
    auto* payload = BuildV10Payload(1, ids, modes, byteCounts, values, length);

    // Length is 4 + 16 = 20 bytes, but v11 expects exactly 4 + 8 = 12 for Count=1
    const auto status = SetFeatureStatesFromPayload(payload, length);
    Expect(status == AgentStatusCode::InvalidCommand,
        "v10 legacy shape (16 bytes/entry) must be rejected as InvalidCommand");

    // Verify nothing was written
    Expect(ReadNativeFeatureState(NativeFeatureStateId::Power) == 0,
        "v10 trailing-byte rejection must not write Power state");

    delete[] payload;
}

void TestSetFeatureStates_CountOverflow_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Count=33 (over limit of 32)
    const uint32_t count = 33;
    unsigned char payload[4];
    std::memcpy(payload, &count, sizeof(count));

    const auto status = SetFeatureStatesFromPayload(payload, sizeof(payload));
    Expect(status == AgentStatusCode::InvalidCommand,
        "Count=33 over limit must return InvalidCommand");
}

void TestSetFeatureStates_CountZero_Ok()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Count=0, no entries, length=4
    const uint32_t count = 0;
    unsigned char payload[4];
    std::memcpy(payload, &count, sizeof(count));

    const auto status = SetFeatureStatesFromPayload(payload, sizeof(payload));
    Expect(status == AgentStatusCode::Ok,
        "Count=0 with no entries must return Ok");
}

void TestSetFeatureStates_TrailingBytes_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Count=1 but payload has extra 4 trailing bytes (length = 16 instead of expected 12)
    const uint32_t ids[] = {2};
    const uint32_t vals[] = {100};
    uint32_t baseLength = 0;
    auto* basePayload = BuildV11Payload(1, ids, vals, baseLength); // 12 bytes

    // Extend by 4 bytes
    const uint32_t extendedLength = baseLength + 4;
    auto* extended = new unsigned char[extendedLength];
    std::memcpy(extended, basePayload, baseLength);
    std::memset(extended + baseLength, 0, 4);

    const auto status = SetFeatureStatesFromPayload(extended, extendedLength);
    Expect(status == AgentStatusCode::InvalidCommand,
        "Trailing bytes after entries must return InvalidCommand");

    delete[] basePayload;
    delete[] extended;
}

void TestSetFeatureStates_DuplicateStateId_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Two entries, both with StateId=2 (duplicate)
    const uint32_t ids[] = {2, 2};
    const uint32_t vals[] = {100, 200};
    uint32_t length = 0;
    auto* payload = BuildV11Payload(2, ids, vals, length);

    const auto status = SetFeatureStatesFromPayload(payload, length);
    Expect(status == AgentStatusCode::InvalidCommand,
        "Duplicate StateId in payload must return InvalidCommand");

    delete[] payload;
}

void TestSetFeatureStates_PulseIdAccepted_WritesState()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Pulse IDs (MoneyPulse=1) ARE valid write targets: C# produces the trigger,
    // native hook consumes via ConsumeNativeFeatureState on next frame.
    const uint32_t ids[] = {1};
    const uint32_t vals[] = {5000};
    uint32_t length = 0;
    auto* payload = BuildV11Payload(1, ids, vals, length);

    const auto status = SetFeatureStatesFromPayload(payload, length);
    Expect(status == AgentStatusCode::Ok,
        "Pulse ID (MoneyPulse=1) as write target must succeed");
    Expect(ReadNativeFeatureState(NativeFeatureStateId::MoneyPulse) == 5000,
        "MoneyPulse slot must hold written value until hook consumes");

    delete[] payload;

    // Verify other pulse IDs also writable
    const uint32_t pulseIds[] = {13, 21, 23};
    for (auto pid : pulseIds)
    {
        ResetNativeFeatureStates();
        uint32_t len = 0;
        auto* p = BuildV11Payload(1, &pid, vals, len);
        Expect(SetFeatureStatesFromPayload(p, len) == AgentStatusCode::Ok,
            "Pulse ID writes must succeed");
        delete[] p;
    }
}

void TestSetFeatureStates_OutOfRangeStateId_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // StateId=200 is out of declared range
    const uint32_t ids[] = {200};
    const uint32_t vals[] = {999};
    uint32_t length = 0;
    auto* payload = BuildV11Payload(1, ids, vals, length);

    const auto status = SetFeatureStatesFromPayload(payload, length);
    Expect(status == AgentStatusCode::InvalidCommand,
        "Out-of-range StateId (200) must return InvalidCommand");

    // Reserved gap IDs (27-99) should also be rejected
    const uint32_t gapId = 50;
    uint32_t len2 = 0;
    auto* p2 = BuildV11Payload(1, &gapId, vals, len2);
    Expect(SetFeatureStatesFromPayload(p2, len2) == AgentStatusCode::InvalidCommand,
        "Reserved gap StateId (50) must return InvalidCommand");
    delete[] p2;

    delete[] payload;
}

void TestSetFeatureStates_ValidSingleEntry_WritesState()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Count=1, StateId=2 (Power), Value=99999
    const uint32_t ids[] = {2};
    const uint32_t vals[] = {99999};
    uint32_t length = 0;
    auto* payload = BuildV11Payload(1, ids, vals, length);

    const auto status = SetFeatureStatesFromPayload(payload, length);
    Expect(status == AgentStatusCode::Ok,
        "Valid single entry must return Ok");
    Expect(ReadNativeFeatureState(NativeFeatureStateId::Power) == 99999,
        "Power state must be written with value 99999");

    delete[] payload;
}

void TestSetFeatureStates_MultipleEntries_AllWritten()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Count=5: Power=10, FastBuild=1, SuperPower=1, FreeBuild=1, IgnorePrerequisites=1
    const uint32_t ids[] = {2, 5, 6, 15, 17};
    const uint32_t vals[] = {10, 1, 1, 1, 1};
    uint32_t length = 0;
    auto* payload = BuildV11Payload(5, ids, vals, length);

    const auto status = SetFeatureStatesFromPayload(payload, length);
    Expect(status == AgentStatusCode::Ok,
        "Multiple entries must return Ok");

    Expect(ReadNativeFeatureState(NativeFeatureStateId::Power) == 10, "Power=10");
    Expect(ReadNativeFeatureState(NativeFeatureStateId::FastBuild) == 1, "FastBuild=1");
    Expect(ReadNativeFeatureState(NativeFeatureStateId::SuperPower) == 1, "SuperPower=1");
    Expect(ReadNativeFeatureState(NativeFeatureStateId::FreeBuild) == 1, "FreeBuild=1");
    Expect(ReadNativeFeatureState(NativeFeatureStateId::IgnorePrerequisites) == 1, "IgnorePrerequisites=1");

    delete[] payload;
}

void TestGetFeatureStates_Returns30Entries_OrderedById()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    FeatureStateReadback reads[30];
    uint32_t count = 0;
    const auto status = ReadAllFeatureStates(reads, 30, count);

    Expect(status == AgentStatusCode::Ok, "ReadAllFeatureStates must return Ok");
    Expect(count == 30, "ReadAllFeatureStates must return 30 entries");

    // Verify ordering: 1-26 then 100-103
    for (uint32_t i = 0; i < 26; ++i)
    {
        Expect(reads[i].StateId == i + 1,
            "First 26 entries must be IDs 1-26 in order");
    }
    for (uint32_t i = 26; i < 30; ++i)
    {
        const uint32_t expected = 100 + (i - 26);
        Expect(reads[i].StateId == expected,
            "Last 4 entries must be IDs 100-103 in order");
    }
}

void TestGetFeatureStates_PulseIdReadsFromStickyBit_NotFromStates()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Write g_states[1] (MoneyPulse slot) directly via deprecated v10 function.
    // g_states[1] should NOT be returned by GetFeatureStates for pulse IDs.
    const uint32_t ids[] = {1};
    const uint32_t modes[] = {0};
    const uint32_t byteCounts[] = {4};
    const uint32_t values[] = {999};
    uint32_t v10len = 0;
    auto* v10 = BuildV10Payload(1, ids, modes, byteCounts, values, v10len);
    ApplyNativeFeatureStatesFromPayload(v10, v10len);
    delete[] v10;

    // Verify the raw g_states[1] was set by using ConsumeNativeFeatureState
    Expect(ConsumeNativeFeatureState(NativeFeatureStateId::MoneyPulse) == 999,
        "g_states[MoneyPulse] must be 999 after v10 write");

    // Now ReadAllFeatureStates should read from sticky bit (which is 0), not g_states
    FeatureStateReadback reads[30];
    uint32_t count = 0;
    ReadAllFeatureStates(reads, 30, count);

    Expect(reads[0].StateId == 1, "First entry must be MoneyPulse");
    Expect(reads[0].Value == 0,
        "MoneyPulse value must be 0 (from sticky bit), not 999 (from g_states)");
}

void TestGetFeatureStates_ClearsStickyBitsAfterRead()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // Fire the sticky bit for MoneyPulse
    NotifyPulseFired(NativeFeatureStateId::MoneyPulse);
    Expect(ReadStickyPulseBit(NativeFeatureStateId::MoneyPulse) == 1,
        "Sticky bit for MoneyPulse must be 1 after NotifyPulseFired");

    // First readback should see the sticky bit as 1
    FeatureStateReadback reads1[30];
    uint32_t count1 = 0;
    ReadAllFeatureStates(reads1, 30, count1);
    Expect(reads1[0].Value == 1,
        "First ReadAllFeatureStates must see MoneyPulse sticky bit as 1");

    // Clear sticky bits (as GetFeatureStates would do after serializing)
    ClearAllStickyPulseBits();

    // Second readback should see 0
    FeatureStateReadback reads2[30];
    uint32_t count2 = 0;
    ReadAllFeatureStates(reads2, 30, count2);
    Expect(reads2[0].Value == 0,
        "Second ReadAllFeatureStates must see MoneyPulse as 0 after ClearAllStickyPulseBits");
}

void TestNotifyPulseFired_OnNonPulseId_IsNoOp()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    // NotifyPulseFired on a non-pulse ID should be a no-op
    NotifyPulseFired(NativeFeatureStateId::Power);  // ID=2, not a pulse

    Expect(ReadStickyPulseBit(NativeFeatureStateId::Power) == 0,
        "ReadStickyPulseBit on non-pulse must return 0");
    Expect(ReadNativeFeatureState(NativeFeatureStateId::Power) == 0,
        "Power state must not be affected by NotifyPulseFired");
}

void TestReadAllFeatureStates_CapacityTooSmall_ReturnsInvalid()
{
    using namespace RayaTrainer::agent;
    ResetNativeFeatureStates();

    FeatureStateReadback reads[10];
    uint32_t count = 999;

    // Capacity=10, but need 30
    const auto status = ReadAllFeatureStates(reads, 10, count);
    Expect(status == AgentStatusCode::InvalidCommand,
        "ReadAllFeatureStates with capacity<30 must return InvalidCommand");
    Expect(count == 0, "outCount must be 0 when capacity too small");

    // nullptr also rejected
    uint32_t count2 = 999;
    Expect(ReadAllFeatureStates(nullptr, 30, count2) == AgentStatusCode::InvalidCommand,
        "ReadAllFeatureStates with nullptr must return InvalidCommand");
}
}

int RunAgentFeatureStateTests()
{
    TestSetFeatureStates_FromV10LegacyShape_RejectsAsInvalidCommand();
    TestSetFeatureStates_CountOverflow_ReturnsInvalid();
    TestSetFeatureStates_CountZero_Ok();
    TestSetFeatureStates_TrailingBytes_ReturnsInvalid();
    TestSetFeatureStates_DuplicateStateId_ReturnsInvalid();
    TestSetFeatureStates_PulseIdAccepted_WritesState();
    TestSetFeatureStates_OutOfRangeStateId_ReturnsInvalid();
    TestSetFeatureStates_ValidSingleEntry_WritesState();
    TestSetFeatureStates_MultipleEntries_AllWritten();
    TestGetFeatureStates_Returns30Entries_OrderedById();
    TestGetFeatureStates_PulseIdReadsFromStickyBit_NotFromStates();
    TestGetFeatureStates_ClearsStickyBitsAfterRead();
    TestNotifyPulseFired_OnNonPulseId_IsNoOp();
    TestReadAllFeatureStates_CapacityTooSmall_ReturnsInvalid();

    if (g_afs_failures != 0)
    {
        std::cerr << g_afs_failures << " AgentFeatureState test(s) FAILED\n";
    }
    return g_afs_failures;
}
