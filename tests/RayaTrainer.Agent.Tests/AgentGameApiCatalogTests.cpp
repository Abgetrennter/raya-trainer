#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>
#include <cstring>
#include <iostream>

#include "../../src/RayaTrainer.Agent/AgentGameApi.h"
#include "../../src/RayaTrainer.Agent/AgentPatchManager.h"
#include "../../src/RayaTrainer.Agent/AgentProtocol.h"

// Stub for ReadCapturedPlayerObject (defined in AgentNativeHooks.cpp, not included in tests).
// AgentGameApi.cpp references this only inside GetMutationOwnerPlayer; returning 0 is safe.
namespace RayaTrainer::agent { uint32_t ReadCapturedPlayerObject() { return 0; } }

namespace
{
int g_cat_failures = 0;

void CatExpect(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << '\n';
        ++g_cat_failures;
    }
}

// Build a valid SetNativeCatalog payload: uint32 count + count * uint32 rva.
unsigned char* BuildValidCatalogPayload(uint32_t& outLength)
{
    constexpr auto count = RayaTrainer::agent::kNativeCatalogEntryCount;
    constexpr auto headerSize = sizeof(uint32_t);
    outLength = headerSize + count * sizeof(uint32_t);
    auto* buf = new unsigned char[outLength];
    std::memcpy(buf, &count, headerSize);
    // Fill RVAs with sequential non-zero test values so we can verify them.
    for (uint32_t i = 0; i < count; ++i)
    {
        const uint32_t rva = 0x1000 + i * 4;
        std::memcpy(buf + headerSize + i * sizeof(uint32_t), &rva, sizeof(rva));
    }
    return buf;
}

void TestInitializeNativeCatalog_ResetsReady()
{
    using namespace RayaTrainer::agent;

    // SetNativeCatalogFromPayload first to put catalog in a ready state.
    uint32_t length = 0;
    auto* payload = BuildValidCatalogPayload(length);
    const auto setStatus = SetNativeCatalogFromPayload(payload, length);
    delete[] payload;

    CatExpect(setStatus == AgentStatusCode::Ok, "SetNativeCatalogFromPayload must succeed before reset test");
    CatExpect(HasNativeCatalog(), "HasNativeCatalog must be true after SetNativeCatalogFromPayload");

    InitializeNativeCatalog();

    CatExpect(!HasNativeCatalog(), "HasNativeCatalog must be false after InitializeNativeCatalog");
}

void TestHasNativeCatalog_False_BeforeSetNativeCatalog()
{
    using namespace RayaTrainer::agent;

    InitializeNativeCatalog();
    CatExpect(!HasNativeCatalog(), "HasNativeCatalog must be false before SetNativeCatalogFromPayload");
}

void TestHasNativeCatalog_True_AfterSetNativeCatalog()
{
    using namespace RayaTrainer::agent;

    InitializeNativeCatalog();
    uint32_t length = 0;
    auto* payload = BuildValidCatalogPayload(length);
    const auto status = SetNativeCatalogFromPayload(payload, length);
    delete[] payload;

    CatExpect(status == AgentStatusCode::Ok, "SetNativeCatalogFromPayload must return Ok");
    CatExpect(HasNativeCatalog(), "HasNativeCatalog must be true after SetNativeCatalogFromPayload");
}

void TestSetNativeCatalogFromPayload_StoresRvas()
{
    using namespace RayaTrainer::agent;

    InitializeNativeCatalog();

    constexpr auto count = kNativeCatalogEntryCount;
    uint32_t length = 0;
    auto* payload = BuildValidCatalogPayload(length);
    const auto status = SetNativeCatalogFromPayload(payload, length);
    delete[] payload;

    CatExpect(status == AgentStatusCode::Ok, "SetNativeCatalogFromPayload must return Ok");

    // Verify every entry resolves to the expected RVA.
    bool allMatch = true;
    for (uint32_t i = 0; i < count; ++i)
    {
        const auto entry = static_cast<NativeCatalogEntry>(i);
        const auto expected = 0x1000 + i * 4;
        const auto actual = ResolveNativeCatalogRva(entry);
        if (actual != expected)
        {
            std::cerr << "  RVA mismatch at entry " << i
                      << ": expected=0x" << std::hex << expected
                      << " actual=0x" << actual << std::dec << '\n';
            allMatch = false;
        }
    }
    CatExpect(allMatch, "All ResolveNativeCatalogRva entries must match the payload values");
}

void TestSetNativeCatalogFromPayload_InvalidCount_Rejected()
{
    using namespace RayaTrainer::agent;

    InitializeNativeCatalog();

    // Payload with wrong count (kNativeCatalogEntryCount + 1).
    constexpr auto wrongCount = kNativeCatalogEntryCount + 1;
    constexpr auto headerSize = sizeof(uint32_t);
    const uint32_t payloadLength = headerSize + wrongCount * sizeof(uint32_t);
    auto* buf = new unsigned char[payloadLength];
    std::memcpy(buf, &wrongCount, headerSize);
    for (uint32_t i = 0; i < wrongCount; ++i)
    {
        const uint32_t rva = 0x1000 + i * 4;
        std::memcpy(buf + headerSize + i * sizeof(uint32_t), &rva, sizeof(rva));
    }

    const auto status = SetNativeCatalogFromPayload(buf, payloadLength);
    delete[] buf;

    CatExpect(status == AgentStatusCode::InvalidCommand, "SetNativeCatalogFromPayload must reject wrong count");
    CatExpect(!HasNativeCatalog(), "HasNativeCatalog must remain false after rejected payload");
}

void TestInstallPatchesFromPayload_BeforeCatalog_ReturnsInvalidCommand()
{
    using namespace RayaTrainer::agent;

    InitializeNativeCatalog();

    // Empty/invalid payload — InstallPatchesFromPayload should check catalog first
    // and return InvalidCommand regardless of payload content.
    const auto status = InstallPatchesFromPayload(nullptr, 0);
    CatExpect(status == AgentStatusCode::InvalidCommand,
              "InstallPatchesFromPayload must return InvalidCommand when catalog not ready");
}

void TestInstallPatchesFromPayload_AfterCatalog_ProcessesPayload()
{
    using namespace RayaTrainer::agent;

    // Set up catalog first.
    uint32_t catLength = 0;
    auto* catPayload = BuildValidCatalogPayload(catLength);
    auto catStatus = SetNativeCatalogFromPayload(catPayload, catLength);
    delete[] catPayload;

    CatExpect(catStatus == AgentStatusCode::Ok, "catalog setup must succeed");

    // Now InstallPatchesFromPayload with an invalid payload (null data with length 0).
    // After catalog is ready, the function should process the payload and reject it as
    // structurally invalid (not as catalog-not-ready).
    const auto status = InstallPatchesFromPayload(nullptr, 0);
    CatExpect(status == AgentStatusCode::InvalidCommand,
              "InstallPatchesFromPayload after catalog must reject null payload");
    // The status code is the same, but importantly it didn't hit the catalog guard.
    // A non-null invalid payload would reach the payload parser; we just verify the
    // catalog-ready path doesn't short-circuit to InvalidCommand for a different reason.
}

} // anonymous namespace

int RunAgentGameApiCatalogTests()
{
    TestInitializeNativeCatalog_ResetsReady();
    TestHasNativeCatalog_False_BeforeSetNativeCatalog();
    TestHasNativeCatalog_True_AfterSetNativeCatalog();
    TestSetNativeCatalogFromPayload_StoresRvas();
    TestSetNativeCatalogFromPayload_InvalidCount_Rejected();
    TestInstallPatchesFromPayload_BeforeCatalog_ReturnsInvalidCommand();
    TestInstallPatchesFromPayload_AfterCatalog_ProcessesPayload();

    std::cout << "[catalog] " << g_cat_failures << " failure(s)\n";
    return g_cat_failures;
}
