#pragma once

#include <cstdint>
#include <vector>

namespace RayaTrainer::agent
{
enum class SignatureAddressMode : uint8_t
{
    MatchAddress,
    Absolute32AtOffset
};

// A signature is a byte pattern with a parallel mask. mask[i] == 0 means the byte at that
// position is a wildcard (ignored during matching; typically a relocated immediate, an
// absolute address embedded in the instruction, or a relative call/jump displacement whose
// value differs between SKUs); mask[i] != 0 means the byte must match exactly. The symbolic
// name identifies what the resolved address means. Most signatures return the match address;
// data-global signatures read an absolute uint32 operand from the matched instruction.
struct Signature
{
    const char* SymbolicName;
    const unsigned char* Bytes;
    const unsigned char* Mask;
    uint32_t Length;
    SignatureAddressMode AddressMode = SignatureAddressMode::MatchAddress;
    uint32_t OperandOffset = 0;
};

struct ScanResult
{
    const char* SymbolicName;
    uint32_t Address; // resolved absolute x86 address; 0 when not found or ambiguous
};

// Scans the host process's .text section for every signature in the table and returns one
// ScanResult per signature, in input order. Signatures that do not match uniquely (zero or
// multiple hits) report Address = 0 so the caller can detect an unsupported build and refuse
// to install hooks rather than patching the wrong location.
//
// Returns the number of signatures that matched a unique address. The outResults buffer is
// resized to the signature count regardless, so a host can see which symbols failed.
//
// The .text section is read once into an internal buffer and reused across calls within the
// same process; subsequent scans re-scan the cached bytes (fast).
size_t ScanSignatures(const Signature* signatures, size_t count, std::vector<ScanResult>& outResults);

// Convenience wrapper for the built-in signature table (wired up in stage 5). Returns true
// and fills outResults when the scan ran; false when the .text section could not be read.
bool RunBuiltInScan(std::vector<ScanResult>& outResults);

// Validates a resolved hook address against its full built-in masked signature, then returns
// the live bytes that will be overwritten. This permits SKU-specific address operands while
// still rejecting an address whose fixed instruction bytes no longer match the catalog.
bool TryReadVerifiedBuiltInHookBytes(
    uint32_t address,
    uint32_t byteCount,
    std::vector<unsigned char>& outBytes);
}
