#pragma once

namespace RayaTrainer::agent
{
// Zydis/zasm-backed signature scanner. This is the DLL-core facility that locates game
// addresses at runtime (the host no longer ships hard-coded RVA tables), so the trainer
// adapts to different SKUs of the same nominal game version (e.g. Steam English vs TW
// Traditional-Chinese ra3_1.12.game, which share a PE FileVersion but differ in code
// layout). Stage 1 wires the libraries in and self-tests decoding; stage 2 adds the
// .text-section signature scan.
//
// Returns true once the Zydis decoder initialized and successfully decoded a fixed
// self-test instruction sequence. The result is computed once on first call and cached.
bool IsScannerReady();
}
