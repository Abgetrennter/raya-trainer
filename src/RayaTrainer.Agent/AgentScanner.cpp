#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <cstdint>

#include <Zydis/Decoder.h>
#include <Zydis/Utils.h>

#include "AgentScanner.h"

namespace RayaTrainer::agent
{
namespace
{
// Cached self-test result. Computed once on the first IsScannerReady() call so the cost
// is paid at DLL init, not on every request.
bool g_scannerReady = false;
bool g_scannerTested = false;

bool RunDecoderSelfTest()
{
    // Self-test bytes: the 1.12 Player Money hook prologue
    //   add edi, [eax+04]   -> 03 78 04
    //   mov edx, [ecx]      -> 8B 11
    // Decoding these two instructions confirms Zydis is linked and the decoder is usable.
    static const unsigned char kSelfTestBytes[] = { 0x03, 0x78, 0x04, 0x8B, 0x11 };

    ZydisDecoder decoder = {};
    if (ZYAN_FAILED(ZydisDecoderInit(&decoder, ZYDIS_MACHINE_MODE_LEGACY_32, ZYDIS_STACK_WIDTH_32)))
    {
        return false;
    }

    // First instruction must be `add` (ZYDIS_MNEMONIC_ADD).
    ZydisDecodedInstruction instruction = {};
    ZydisDecodedOperand operands[ZYDIS_MAX_OPERAND_COUNT];
    if (ZYAN_FAILED(ZydisDecoderDecodeFull(
            &decoder,
            kSelfTestBytes,
            sizeof(kSelfTestBytes),
            &instruction,
            operands)))
    {
        return false;
    }

    if (instruction.mnemonic != ZYDIS_MNEMONIC_ADD)
    {
        return false;
    }

    // Second instruction starts right after the first; must be `mov`.
    const ZyanUSize offset = instruction.length;
    ZydisDecodedInstruction second = {};
    if (ZYAN_FAILED(ZydisDecoderDecodeFull(
            &decoder,
            kSelfTestBytes + offset,
            sizeof(kSelfTestBytes) - offset,
            &second,
            operands)))
    {
        return false;
    }

    return second.mnemonic == ZYDIS_MNEMONIC_MOV;
}
}

bool IsScannerReady()
{
    if (!g_scannerTested)
    {
        g_scannerReady = RunDecoderSelfTest();
        g_scannerTested = true;
    }

    return g_scannerReady;
}
}
