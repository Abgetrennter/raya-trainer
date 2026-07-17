#pragma once

#include <cstdint>

#include "AgentProtocol.h"

namespace RayaTrainer::agent
{
enum class NativeFeatureStateId : uint32_t
{
    MoneyPulse = 1,
    Power = 2,
    SecretProtocolPoints = 3,
    AllSecretProtocols = 4,
    FastBuild = 5,
    SuperPower = 6,
    DisableAllSuperPowers = 7,
    Zoom = 8,
    RevealMap = 9,
    EnemyCannotBuild = 10,
    GodMode = 11,
    OneHitKill = 12,
    ChallengeMoneyPulse = 13,
    ChallengeTime = 14,
    FreeBuild = 15,
    SecretProtocolDependencyBypass = 16,
    IgnorePrerequisites = 17,
    IgnoreQuantityLimit = 18,
    RunInBackground = 19,
    FrameRateUnlock = 20,
    AutoRepairPulse = 21,
    DangerLevelMode = 22,
    RestoreOrePulse = 23,
    AutoRepair = 24,
    SlowMotionMode = 25,
    LogicTimeFreeze = 26,
    MoneyAmount = 100,
    PowerValue = 101,
    SecretProtocolPointValue = 102,
    SelectedUnitMaxHealthBits = 103
};

// DEPRECATED v11: replaced by SetFeatureStatesFromPayload
AgentStatusCode ApplyNativeFeatureStatesFromPayload(
    const unsigned char* data,
    uint32_t length);

struct FeatureStateReadback
{
    uint32_t StateId;
    uint32_t Value;
};

// Strict v11 SetFeatureStates handler. Validates all rules before applying.
AgentStatusCode SetFeatureStatesFromPayload(
    const unsigned char* data,
    uint32_t length);

uint32_t ReadNativeFeatureState(NativeFeatureStateId id);
uint32_t ConsumeNativeFeatureState(NativeFeatureStateId id);
void ResetNativeFeatureStates();

// Sticky-bit pulse tracking.
void NotifyPulseFired(NativeFeatureStateId id);
uint32_t ReadStickyPulseBit(NativeFeatureStateId id);
void ClearAllStickyPulseBits();

// Bulk readback for GetFeatureStates. Fills out with all declared state IDs in enum order.
// Does NOT clear sticky bits — caller must call ClearAllStickyPulseBits after serializing.
AgentStatusCode ReadAllFeatureStates(FeatureStateReadback* out, uint32_t capacity, uint32_t& outCount);
}
