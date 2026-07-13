#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <vector>

#include "AgentSignatureScanner.h"

namespace RayaTrainer::agent
{
namespace
{
constexpr int HexNibble(char value)
{
    if (value >= '0' && value <= '9')
    {
        return value - '0';
    }

    if (value >= 'A' && value <= 'F')
    {
        return value - 'A' + 10;
    }

    if (value >= 'a' && value <= 'f')
    {
        return value - 'a' + 10;
    }

    return -1;
}

// Keeps the built-in table readable in the same AOB form used during IDA validation while
// materializing the byte and mask arrays required by the scanner at compile time.
template <size_t N>
struct BuiltInPattern
{
    const char* SymbolicName;
    std::array<unsigned char, N> Bytes = {};
    std::array<unsigned char, N> Mask = {};
    uint32_t Length = 0;
    bool Valid = true;

    constexpr BuiltInPattern(const char* symbolicName, const char (&text)[N])
        : SymbolicName(symbolicName)
    {
        size_t index = 0;
        while (index < N - 1)
        {
            if (text[index] == ' ')
            {
                ++index;
                continue;
            }

            if (text[index] == '?')
            {
                Bytes[Length] = 0;
                Mask[Length] = 0;
                ++Length;
                while (index < N - 1 && text[index] == '?')
                {
                    ++index;
                }
                continue;
            }

            if (index + 1 >= N - 1)
            {
                Valid = false;
                return;
            }

            const int high = HexNibble(text[index]);
            const int low = HexNibble(text[index + 1]);
            if (high < 0 || low < 0)
            {
                Valid = false;
                return;
            }

            Bytes[Length] = static_cast<unsigned char>((high << 4) | low);
            Mask[Length] = 0xFF;
            ++Length;
            index += 2;

            if (index < N - 1 && text[index] != ' ')
            {
                Valid = false;
                return;
            }
        }
    }

    constexpr Signature ToSignature(
        SignatureAddressMode addressMode = SignatureAddressMode::MatchAddress,
        uint32_t operandOffset = 0) const
    {
        return Signature{
            SymbolicName,
            Bytes.data(),
            Mask.data(),
            Valid ? Length : 0,
            addressMode,
            operandOffset
        };
    }
};

template <size_t N>
constexpr BuiltInPattern<N> MakeBuiltInPattern(const char* symbolicName, const char (&text)[N])
{
    return BuiltInPattern<N>(symbolicName, text);
}

// 1.12 hook signatures. Address-bearing operands are wildcarded; object/stack offsets and
// constants remain exact. Every entry was verified with idalib-mcp-headless as a unique hit
// in both the Traditional Chinese and Steam English 1.12 binaries.
static constexpr auto kHook01PlayerID = MakeBuiltInPattern(
    "_BackPlayerID",
    "8B 50 28 8B 42 20 8D 14 24 52 50 E8 ? ? ? ?"); // TW 0x4FF95B, EN 0x54119B
static constexpr auto kHook02PlayerMoney = MakeBuiltInPattern(
    "_BackPlayerMoney",
    "03 78 04 8B 11 8B 42 0C 57 FF D0 6A 01 8B 8E ?? ?? ?? ??"); // TW 0xACFDFE, EN 0xA64E9E, Uprising 0xA64A3A (offset 0x708 -> 0x754 wildcarded)
static constexpr auto kHook03PlayerPower = MakeBuiltInPattern(
    "_BackPlayerPower",
    "8B 40 04 8B 8E ?? ?? ?? ?? 8B 39 52 8B 57 0C 50"); // TW 0xACFD0D, EN 0xA64DAD, Uprising 0xA64938 (offset 0x3B0 -> 0x3E0 wildcarded)
static constexpr auto kHook04PlayerSCPoint = MakeBuiltInPattern(
    "_BackPlayerSCPoint",
    "8B 78 34 8B 4E 3C E8 ? ? ? ? 8B 10 8B C8 8B 42 04"); // TW 0xACFE6C, EN 0xA64F0C
static constexpr auto kHook05PlayerHaveAllSC = MakeBuiltInPattern(
    "_BackPlayerHaveAllSC",
    "F3 0F 10 47 2C F3 0F 10 15 ? ? ? ? F3 0F 2A C8"); // TW 0xACFEB5, EN 0xA64F55
static constexpr auto kHook06PlayerFastBuildContext = MakeBuiltInPattern(
    "_BackPlayerFastBuildContext",
    "83 EC 08 55 8B 6C 24 10 85 ED 56 8B F1 75 ? 5E 32 C0 5D 83 C4 08 C2 04 00 8B 46 10 85 C0 F3 0F 10 46 20"); // TW 0x70F440, EN 0x74D830
static constexpr auto kHook07PlayerFastBuild = MakeBuiltInPattern(
    "_BackPlayerFastBuild",
    "F3 0F 2C 46 1C 5E 59 C3 CC CC CC CC CC CC CC CC"); // TW 0x70F42E, EN 0x74D81E
static constexpr auto kHook08PlayerFastBuild2 = MakeBuiltInPattern(
    "_BackPlayerFastBuild2",
    "D9 46 1C 89 46 5C D9 5E 60 8B 0D ? ? ? ? E8 ? ? ? ?"); // TW 0x70F38C, EN 0x74D77C
static constexpr auto kHook09PlayerFastBuild3 = MakeBuiltInPattern(
    "_BackPlayerFastBuild3",
    "D9 86 BC 01 00 00 55 D9 5C 24 14 52 8B CE E8 ? ? ? ?"); // TW 0x6F6CFF, EN 0x73511F
static constexpr auto kHook10PlayerSuperPower = MakeBuiltInPattern(
    "_BackPlayerSuperPower",
    "8B 98 ?? 04 00 00 85 DB 74 ? 8B 16 8B 42 1C 8B CE"); // TW 0x6EBB69, EN 0x729F19, Uprising 0x723C59 (owner 0x418 -> 0x428)
static constexpr auto kHook11PlayerSuperPower2 = MakeBuiltInPattern(
    "_BackPlayerSuperPower2",
    "8B 70 50 8B 01 3B C1 74 ? 39 50 08 74 ? 8B 00"); // TW 0x83EC19, EN 0x87CB89
static constexpr auto kHook12DisableAllSuperPower = MakeBuiltInPattern(
    "_BackDisableAllSuperPower",
    "8B 44 24 04 8B 50 08 8B 02 8B 50 04 8B 81 18 02 00 00 81 C1 18 02 00 00 3B C1 74 ? 8D 64 24 00 39 50 08 74 ? 8B 00 3B C1 75 ? 32 C0 C2 04 00"); // TW 0x83EC70, EN 0x87CBE0
static constexpr auto kHook13DisableAllSuperPower2 = MakeBuiltInPattern(
    "_BackDisableAllSuperPower2",
    "A1 ? ? ? ? 80 B8 ?? 00 00 00 00 56 8B F1 75 ? 32 C0 5E C3 80 7E 30 00 75 ? 8B 46 F8 85 C0 57 74 ?"); // TW 0x70A580, EN 0x748990, Uprising 0x743B40 (in-game flag 0xA5 -> 0xA6)
static constexpr auto kHook14PlayerFreeBuild = MakeBuiltInPattern(
    "_BackPlayerFreeBuild",
    "84 DB 75 ? B8 02 00 00 00 5B 83 C4 0C C2 10 00"); // TW 0x4E7563, EN 0x528DF3
static constexpr auto kHook15SecretProtocolDependency = MakeBuiltInPattern(
    "_BackSecretProtocolDependency",
    "8D 54 24 14 52 E8 ? ? ? ? 85 C0 0F 85 ? ? ? ?"); // TW 0x70A6CD, EN 0x748ADD
static constexpr auto kHook16IgnorePrerequisites = MakeBuiltInPattern(
    "_BackIgnorePrerequisites",
    "51 56 8B F1 8B 0D ? ? ? ? 85 C9 74 ? 8D 44 24 04"); // TW 0x7DF770, EN 0x81DA90
static constexpr auto kHook17IgnorePrerequisitesUiDependency = MakeBuiltInPattern(
    "_BackIgnorePrerequisitesUiDependency",
    "E8 ? ? ? ? 85 C0 74 ? 8B 77 28 85 F6 74 ?"); // TW 0x7BA8F7, EN 0x7F8C47
static constexpr auto kHook18IgnorePrerequisitesUiChildDependency = MakeBuiltInPattern(
    "_BackIgnorePrerequisitesUiChildDependency",
    "E8 ? ? ? ? 85 C0 74 ? 83 F8 06 75 ? 8B 76 58"); // TW 0x7BA935, EN 0x7F8C85
static constexpr auto kHook19IgnorePrerequisitesCommandStatusDependency = MakeBuiltInPattern(
    "_BackIgnorePrerequisitesCommandStatusDependency",
    "E8 ? ? ? ? 85 C0 75 ? 8B 4E 18 8B 51 14 0F 57 C0"); // TW 0xABB455, EN 0xA50085
static constexpr auto kHook20IgnorePrerequisitesPlacementDependency = MakeBuiltInPattern(
    "_BackIgnorePrerequisitesPlacementDependency",
    "E8 ? ? ? ? 85 C0 74 ? 8B 6C 24 14 83 C5 01"); // TW 0x54674D, EN 0x587C8D
static constexpr auto kHook21IgnorePrerequisitesProductionDependency = MakeBuiltInPattern(
    "_BackIgnorePrerequisitesProductionDependency",
    "E8 ? ? ? ? 83 F8 01 75 ? 5F 5E 5D 83 C4 0C"); // TW 0x78E511, EN 0x7CC8B1
static constexpr auto kHook22IgnorePrerequisitesScience = MakeBuiltInPattern(
    "_BackIgnorePrerequisitesScience",
    "51 56 8B 74 24 0C 85 F6 57 8B F9 74 ? 56 E8 ? ? ? ?"); // TW 0x8451F0, EN 0x883260
static constexpr auto kHook23IgnorePrerequisitesBuildability = MakeBuiltInPattern(
    "_BackIgnorePrerequisitesBuildability",
    "56 8B 74 24 08 85 F6 57 8B F9 75 ? 5F 32 C0 5E C2 04 00 56 E8 ? ? ? ?"); // TW 0x845250, EN 0x8832C0
static constexpr auto kHook24QuantityLimitGate = MakeBuiltInPattern(
    "_BackQuantityLimitGate",
    "83 EC 08 57 8B 7C 24 10 85 FF 74 ? 8B 44 24 14"); // TW 0x549740, EN 0x58AC10
static constexpr auto kHook25PlayerZoom = MakeBuiltInPattern(
    "_BackPlayerZoom",
    "F3 0F 11 46 44 74 ? 8B 96 FC 26 00 00 8B 42 04"); // TW 0x5EC7CD, EN 0x62B82D
static constexpr auto kHook26PlayerMap = MakeBuiltInPattern(
    "_BackPlayerMap",
    "F3 0F 11 85 60 02 00 00 ?? ?? ?? ?? ?? ?? ?? ??"); // TW 0x7C226C, EN 0x8005BC, Uprising 0x7F319D (tail rewritten, 8-byte common prefix + wildcarded tail)
static constexpr auto kHook27SelectUnitAmmo = MakeBuiltInPattern(
    "_BackSelectUnitAmmo",
    "8B 0C BA 85 C9 0F 84 ? ? ? ? 8B 41 04 8B 40 04"); // TW 0x528735, EN 0x569D85
static constexpr auto kHook28DangerLevel = MakeBuiltInPattern(
    "_BackDangerLevel",
    "8B 81 78 12 00 00 85 C0 74 ? 8B 40 08 C3 33 C0"); // TW 0x838ED0, EN 0x877070
static constexpr auto kHook29RestoreOreMine = MakeBuiltInPattern(
    "_BackRestoreOreMine",
    "8B ?? ?? 2B 41 14 79 ? 33 C0 C3 CC CC"); // 1.12 TW 0x6D4933, 1.12 EN 0x712EB3, 1.13 0x6FDBB3, Uprising 0x70A8D0 (first mov reg/disp wildcarded: [eax+8] -> [ecx+28h])
static constexpr auto kHook30EnemyCantBuild = MakeBuiltInPattern(
    "_BackEnemyCantBuild",
    "03 50 04 3B D7 72 ? 8B 46 10 85 C0 74 ? 8B 48 04"); // TW 0x70F530, EN 0x74D920
static constexpr auto kHook31PlayerGodMode = MakeBuiltInPattern(
    "_BackPlayerGodMode",
    "8B 50 3C 8B CE FF D2 D9 5C 24 0C F3 0F 10 54 24 08"); // TW 0x52EEDF, EN 0x5705DF
static constexpr auto kHook32PlayerOneKillItMode = MakeBuiltInPattern(
    "_BackPlayerOneKillItMode",
    "F3 0F 11 46 04 74 ? 0F 57 C0 0F 2F C1 76 ? D9 44 24 0C"); // TW 0x7651AE, EN 0x7A366E
static constexpr auto kHook33PlayerOneKillItModeData = MakeBuiltInPattern(
    "_BackPlayerOneKillItModeData",
    "8B 8E 3C 03 00 00 8B 95 80 00 00 00 8B 92 DC 00 00 00"); // TW 0x7FE714, EN 0x83C964
static constexpr auto kHook34PlayerOneKillItModeData2 = MakeBuiltInPattern(
    "_BackPlayerOneKillItModeData2",
    "8B 89 3C 03 00 00 85 C9 74 ? 8B 11 8B 82 D0 00 00 00"); // TW 0x6E24E3, EN 0x720963
static constexpr auto kHook35PlayerOneKillItModeCaller = MakeBuiltInPattern(
    "_BackPlayerOneKillItModeCaller",
    "F6 86 71 04 00 00 01 57 8B 7C 24 0C 75 ? 8B 8E 3C 03 00 00"); // TW 0x78E2F3, EN 0x7CC693
static constexpr auto kHook36PlayerOneKillItModeCaller2 = MakeBuiltInPattern(
    "_BackPlayerOneKillItModeCaller2",
    "53 55 57 8B F9 8B AF 00 01 00 00 8D 9F 00 01 00 00"); // TW 0x78E360, EN 0x7CC700
static constexpr auto kHook37BackgroundRunPauseGate = MakeBuiltInPattern(
    "_BackBackgroundRunPauseGate",
    "E8 ? ? ? ? 8B 0D ? ? ? ? 85 C9 74 ? 8B 11 8B 42 44 FF D0 8B 0D ? ? ? ?"); // TW 0x401437, EN 0x401457
static constexpr auto kHook38SelectedUnitAttackSpeedScale = MakeBuiltInPattern(
    "_BackSelectedUnitAttackSpeedScale",
    "83 EC 08 F3 0F 10 05 ? ? ? ? 8B 41 18 F3 0F 11 04 24 8B 40 24 85 C0 74 ? 8B 80 ? 03 00 00"); // 1.12 0x6F8FE0, 1.13 0x722190, Uprising 1.0/1.1 0x749550/0x731670
static constexpr auto kHook39UprisingChallengeTime = MakeBuiltInPattern(
    "_BackChallengeModeTime",
    "F3 0F 2C 58 38 8B 7D 08 68 ? ? ? ? 8D 4C 24 38"); // Uprising 1.1 0xA60E5A
static constexpr auto kHook40UprisingChallengeMoney = MakeBuiltInPattern(
    "_BackChallengeModeMoney",
    "8B 78 08 68 ? ? ? ? 8D 4C 24 38 E8 ? ? ? ? 57 8D 4C 24 20"); // Uprising 1.1 0xA60FA7
static constexpr auto kHook41FrameRateUnlockGameUpdate = MakeBuiltInPattern(
    "_BackFrameRateUnlockGameUpdate",
    "8B 01 8B 90 0C 01 00 00 FF D2 8B 0D ? ? ? ? 80 B9 C4 00 00 00 00"); // RA3 1.12 0x626630
static constexpr auto kHook42ZoomClamp = MakeBuiltInPattern(
    "_BackZoomClamp",
    "F3 0F 10 4C 24 08 F3 0F 10 44 24 04 0F 2F C8 76 05 D9 44 24 08 C3"); // 1.12 0x4FDBD0, 1.13 0x4D81E0, Uprising 1.0/1.1 0x4DB620/0x4E44D0
static constexpr auto kHook43SelectedUnitAttackRangeScale = MakeBuiltInPattern(
    "_BackSelectedUnitAttackRangeScale",
    "83 EC 08 F3 0F 10 15 ? ? ? ? F3 0F 10 4C 24 10 56 8B F1 8B 46 04"); // 1.12 0x713770 WeaponTemplate_GetWeaponActualRange
static constexpr auto kHook44LogicTimeFreezeGate = MakeBuiltInPattern(
    "_BackLogicTimeFreezeGate",
    "E8 ? ? ? ? 8B 0D ? ? ? ? 8B 01 8B 90 ? ? ? ? FF D2 8B 0D ? ? ? ? 80 B9"); // 1.12 0x626625 game-update entry (sub_626620+5)
static constexpr auto kHook45SelectedUnitAutoAcquireRange = MakeBuiltInPattern(
    "_BackSelectedUnitAutoAcquireRange",
    "D9 44 24 0C 57 8D 4C 24 14 51 6A 01 51 8B 0D ? ? ? ? D9 1C 24"); // 1.12 0x836ECB BaseAITargetChooser_FindEnemyTargetInternal
static constexpr auto kHook46SelectedUnitIdleAutoAcquireRange = MakeBuiltInPattern(
    "_BackSelectedUnitIdleAutoAcquireRange",
    "8D 8C 24 9C 00 00 00 51 E9 ? ? ? ? 8D 94 24 40 01 00 00 52 E9 ? ? ? ? F3 0F 10 44 24 0C"); // 1.12 0x836E1B BaseAITargetChooser_FindEnemyTargetInternal idle filter branch
static constexpr auto kHook47SelectedUnitTurretTargetAngle = MakeBuiltInPattern(
    "_BackSelectedUnitTurretTargetAngle",
    "8B 86 98 00 00 00 85 C0 8B 4E 5C 74 0F 8B 56 58 F3 0F 10 42 4C"); // 1.12 0x7F2944 TurretAI_IsInTurretAngle shared angle gate
static constexpr auto kHook48SelectedUnitTurretAimDeflection = MakeBuiltInPattern(
    "_BackSelectedUnitTurretAimDeflection",
    "0F 86 ? ? ? ? 85 FF 74 0C F3 0F 10 47 64 F3 0F 58 43 4C EB 05"); // 1.12 0x80DF79 TurretAI turn max-deflection branch

// Bootstrap and profile address catalog. Code entries resolve to their match address. Data
// entries use a unique code reference as the anchor and read the wildcarded absolute operand.
static constexpr auto kRva1160 = MakeBuiltInPattern(
    "Rva_1160",
    "8B 44 24 08 8B 54 24 04 6A 00 50 6A 00 52 81 C1 50 01 00 00"); // TW 0x401160, EN 0x401180
static constexpr auto kRva1437 = MakeBuiltInPattern(
    "Rva_1437",
    "E8 ? ? ? ? 8B 0D ? ? ? ? 85 C9 74 ? 8B 11 8B 42 44 FF D0 8B 0D ? ? ? ?"); // TW 0x401437, EN 0x401457
static constexpr auto kRva143C = MakeBuiltInPattern(
    "Rva_143C",
    "8B 0D ? ? ? ? 85 C9 74 ? 8B 11 8B 42 44 FF D0"); // TW 0x40143C, EN 0x40145C
static constexpr auto kRvaE2C10 = MakeBuiltInPattern(
    "Rva_E2C10",
    "56 57 8B 7C 24 0C 57 8B F1 E8 ? ? ? ? 85 C0 75 ? 39 46 04 74 ? 57"); // TW 0x4E2C10, EN 0x524630
static constexpr auto kRvaE7573 = MakeBuiltInPattern(
    "Rva_E7573",
    "33 C0 38 44 24 07 5B 0F 95 C0 83 C4 0C C2 10 00"); // TW 0x4E7573, EN 0x528E03
static constexpr auto kRva1456F0 = MakeBuiltInPattern(
    "Rva_1456F0",
    "8B 44 24 04 8B 50 04 85 D2 74 ? 8B 41 24 85 C0"); // TW 0x5456F0, EN 0x586CF0
static constexpr auto kRva14678D = MakeBuiltInPattern(
    "Rva_14678D",
    "5E 5D 84 C0 5F 0F 95 C0 5B 83 C4 14 C2 08 00 CC"); // TW 0x54678D, EN 0x587CCD
static constexpr auto kRva147260 = MakeBuiltInPattern(
    "Rva_147260",
    "8B 41 24 85 C0 74 ? 8B 4C 24 04 EB ? 8D 49 00 8B 50 0C 8B 12 3B 4A 04"); // TW 0x547260, EN 0x588730
static constexpr auto kRva149740 = MakeBuiltInPattern(
    "Rva_149740",
    "83 EC 08 57 8B 7C 24 10 85 FF 74 ? 8B 44 24 14"); // TW 0x549740, EN 0x58AC10
static constexpr auto kRva149748 = MakeBuiltInPattern(
    "Rva_149748",
    "85 FF 74 ? 8B 44 24 14 85 C0 74 ? 8B 48 14 66 83 B9 ?? 02 00 00 00"); // TW 0x549748, EN 0x58AC18, Uprising 0x5791F8 (field 0x242 -> 0x24A)
static constexpr auto kRva1ED4A0 = MakeBuiltInPattern(
    "Rva_1ED4A0",
    "81 EC D0 00 00 00 83 BC 24 D4 00 00 00 00 55 0F 84 ? ? ? ?"); // TW 0x5ED4A0, EN 0x62C500
static constexpr auto kRva205240 = MakeBuiltInPattern(
    "Rva_205240",
    "8B 4C 24 08 83 EC ?? 53 33 DB 33 C0 3B CB 0F 84 ? ? ? ?"); // TW 0x605240, EN 0x6440F0, Uprising 0x638220 (local frame 0x3C -> 0x40)
static constexpr auto kRva35C200 = MakeBuiltInPattern(
    "Rva_35C200",
    "55 8B 6C 24 08 85 ED 57 8B F9 7F ? 5F 32 C0 5D"); // TW 0x75C200, EN 0x79A840
static constexpr auto kRva38E651 = MakeBuiltInPattern(
    "Rva_38E651",
    "38 5C 24 10 75 ? 39 BE 18 02 00 00 74 ? 57 8B CE"); // TW 0x78E651, EN 0x7CC9F1
static constexpr auto kRva39EA50 = MakeBuiltInPattern(
    "Rva_39EA50",
    "81 EC 9C 00 00 00 56 8B F1 8D 4C 24 08 C7 44 24 04 ? ? ? ? E8 ? ? ? ? 8B 8C 24 A8 00 00 00"); // TW 0x79EA50, EN 0x7DCDF0
static constexpr auto kRva3E3D00 = MakeBuiltInPattern(
    "Rva_3E3D00",
    "83 EC 40 56 8B F1 8B 46 04 8B 88 C0 00 00 00 C1 E9 06 F6 C1 01 0F 85 FC 00 00 00"); // 1.12 0x7E3D00, 1.13 0x80CFC0, Uprising 1.0/1.1 0x717B40/0x6FFAB0
static constexpr auto kRva3AD79E = MakeBuiltInPattern(
    "Rva_3AD79E",
    "8B 43 08 80 B8 93 00 00 00 00 F3 0F 10 40 5C F3 0F 59 84 24 9C 00 00 00"); // TW 0x7AD79E, EN 0x7EBABE
static constexpr auto kRva3ADEE2 = MakeBuiltInPattern(
    "Rva_3ADEE2",
    "5F 5E 81 C4 A0 00 00 00 C2 04 00 CC CC CC D9 44 24 10"); // TW 0x7ADEE2, EN 0x7EC202
static constexpr auto kRva3BA8C9 = MakeBuiltInPattern(
    "Rva_3BA8C9",
    "85 C0 8B 5C 24 18 75 ? 38 45 4E 8B 57 F8 89 5C 24 44"); // TW 0x7BA8C9, EN 0x7F8C19
static constexpr auto kRva3BA9CC = MakeBuiltInPattern(
    "Rva_3BA9CC",
    "84 C0 75 ? 32 DB EB ? B3 01 8B 45 10 8B 74 24 18"); // TW 0x7BA9CC, EN 0x7F8D1C
static constexpr auto kRva3E4230 = MakeBuiltInPattern(
    "Rva_3E4230",
    "83 EC 08 56 8D 71 28 8D 44 24 10 50 8D 4C 24 08 51 8B CE E8 ? ? ? ? 8B 56 08 8B 4E 04 8B 44 24 04 3B 04 91 5E 74 ?"); // TW 0x7E4230, EN 0x822510
static constexpr auto kRva4393E0 = MakeBuiltInPattern(
    "Rva_4393E0",
    "8B 41 28 85 C0 75 ? C3 80 B8 06 01 00 00 00 75 ?"); // TW 0x8393E0, EN 0x877580
static constexpr auto kRva43C300 = MakeBuiltInPattern(
    "Rva_43C300",
    "8B 44 24 04 6A 00 50 68 D9 18 08 7F E8 ? ? ? ?"); // TW 0x83C300, EN 0x87A3B0
static constexpr auto kRva44A2D0 = MakeBuiltInPattern(
    "Rva_44A2D0",
    "51 8B 44 24 08 85 C0 75 ? 32 C0 59 C2 04 00 8B 40 0C 8B 10 8B 42 04 56 8D 71 58 8D 4C 24 0C 51"); // TW 0x84A2D0, EN 0x888300
static constexpr auto kRva44D7C0 = MakeBuiltInPattern(
    "Rva_44D7C0",
    "83 EC 08 53 8B 5C 24 10 55 56 57 8B F9 8B 77 34"); // TW 0x84D7C0, EN 0x88B800
static constexpr auto kRva454300 = MakeBuiltInPattern(
    "Rva_454300",
    "83 EC 0C 56 8B F1 8B 56 1C 8B 46 18 3B C2 8D 4E 18"); // TW 0x854300, EN 0x892340
static constexpr auto kRva6BB430 = MakeBuiltInPattern(
    "Rva_6BB430",
    "85 C0 75 ? 8B 4E 18 8B 89 ?? ?? ?? ?? 3B CB 8B 56 10"); // TW 0xABB430, EN 0xA50060, Uprising 0xA4EB1A (field 0x318 -> 0x30C)
static constexpr auto kRva6BB81C = MakeBuiltInPattern(
    "Rva_6BB81C",
    "84 C0 75 ? 8B CE E8 ? ? ? ? 5D 5F 5B B0 01"); // TW 0xABB81C, EN 0xA5044C
static constexpr auto kRva6BBA87 = MakeBuiltInPattern(
    "Rva_6BBA87",
    "83 F8 02 74 ? 83 F8 03 75 ? 83 7B 2C 01 75 ?"); // TW 0xABBA87, EN 0xA506B7
static constexpr auto kRva6BBA9F = MakeBuiltInPattern(
    "Rva_6BBA9F",
    "84 C0 74 ? 57 8B CB E8 ? ? ? ? 84 C0 74 ? 8B 0D ? ? ? ? 8B 01"); // TW 0xABBA9F, EN 0xA506CF
static constexpr auto kRva6BBAAB = MakeBuiltInPattern(
    "Rva_6BBAAB",
    "84 C0 74 ? 8B 0D ? ? ? ? 8B 01 8B 50 44 57"); // TW 0xABBAAB, EN 0xA506DB
static constexpr auto kRva6C62E8 = MakeBuiltInPattern(
    "Rva_6C62E8",
    "84 C0 0F 84 ? ? ? ? 8B 0D ? ? ? ? 8B 01 8B 50 44 56 53 FF D2 85 C0"); // TW 0xAC62E8, EN 0xA5B2E8
static constexpr auto kRva8D8CE4 = MakeBuiltInPattern(
    "Rva_8D8CE4",
    "8B 35 ? ? ? ? 6A 00 6A 03 8B CE E8 ? ? ? ?"); // TW 0xCD8CE4, EN 0xCDDE84
static constexpr auto kRva8DAEFC = MakeBuiltInPattern(
    "Rva_8DAEFC",
    "8B 0D ? ? ? ? 85 C9 74 ? 6A 00 EB ? 85 F6"); // TW 0xCDAEFC, EN 0xCE009C
static constexpr auto kRva8DB73C = MakeBuiltInPattern(
    "Rva_8DB73C",
    "8B 0D ? ? ? ? 8B 01 8B 50 0C FF D2 A1 ? ? ? ?"); // TW 0xCDB73C, EN 0xCE08DC
static constexpr auto kRva8DBBF0 = MakeBuiltInPattern(
    "Rva_8DBBF0",
    "8B 0D ? ? ? ? 52 E8 ? ? ? ? 50 8B CB E8 ? ? ? ?"); // TW 0xCDBBF0, EN 0xCE0D90
static constexpr auto kRva8E6C58 = MakeBuiltInPattern(
    "Rva_8E6C58",
    "8B 0D ? ? ? ? 50 E8 ? ? ? ? 80 7F 08 00"); // TW 0xCE6C58, EN 0xCEBDE8
static constexpr auto kRva8E8C9C = MakeBuiltInPattern(
    "Rva_8E8C9C",
    "8B 0D ? ? ? ? 8D 44 24 1C 50 E8 ? ? ? ? 8B F8 85 FF 74 ?"); // TW 0xCE8C9C, EN 0xCEDE2C, Uprising 0xD05DD4 (extended +5 bytes to disambiguate Uprising's 2-hit case)
static constexpr auto kRva8E9838 = MakeBuiltInPattern(
    "Rva_8E9838",
    "A1 ? ? ? ? 85 C0 74 ? 8B 0D ? ? ? ? 50"); // TW 0xCE9838, EN 0xCEE9C8
static constexpr auto kRva88DFD0 = MakeBuiltInPattern(
    "Rva_88DFD0",
    "8B 78 08 68 ? ? ? ? 8D 4C 24 38 E8 ? ? ? ? 57 8D 4C 24 20"); // Uprising challenge-money replay immediate

static constexpr Signature kBuiltInSignatures[] = {
    kHook01PlayerID.ToSignature(),
    kHook02PlayerMoney.ToSignature(),
    kHook03PlayerPower.ToSignature(),
    kHook04PlayerSCPoint.ToSignature(),
    kHook05PlayerHaveAllSC.ToSignature(),
    kHook06PlayerFastBuildContext.ToSignature(),
    kHook07PlayerFastBuild.ToSignature(),
    kHook08PlayerFastBuild2.ToSignature(),
    kHook09PlayerFastBuild3.ToSignature(),
    kHook10PlayerSuperPower.ToSignature(),
    kHook11PlayerSuperPower2.ToSignature(),
    kHook12DisableAllSuperPower.ToSignature(),
    kHook13DisableAllSuperPower2.ToSignature(),
    kHook14PlayerFreeBuild.ToSignature(),
    kHook15SecretProtocolDependency.ToSignature(),
    kHook16IgnorePrerequisites.ToSignature(),
    kHook17IgnorePrerequisitesUiDependency.ToSignature(),
    kHook18IgnorePrerequisitesUiChildDependency.ToSignature(),
    kHook19IgnorePrerequisitesCommandStatusDependency.ToSignature(),
    kHook20IgnorePrerequisitesPlacementDependency.ToSignature(),
    kHook21IgnorePrerequisitesProductionDependency.ToSignature(),
    kHook22IgnorePrerequisitesScience.ToSignature(),
    kHook23IgnorePrerequisitesBuildability.ToSignature(),
    kHook24QuantityLimitGate.ToSignature(),
    kHook25PlayerZoom.ToSignature(),
    kHook26PlayerMap.ToSignature(),
    kHook27SelectUnitAmmo.ToSignature(),
    kHook28DangerLevel.ToSignature(),
    kHook29RestoreOreMine.ToSignature(),
    kHook30EnemyCantBuild.ToSignature(),
    kHook31PlayerGodMode.ToSignature(),
    kHook32PlayerOneKillItMode.ToSignature(),
    kHook33PlayerOneKillItModeData.ToSignature(),
    kHook34PlayerOneKillItModeData2.ToSignature(),
    kHook35PlayerOneKillItModeCaller.ToSignature(),
    kHook36PlayerOneKillItModeCaller2.ToSignature(),
    kHook37BackgroundRunPauseGate.ToSignature(),
    kHook38SelectedUnitAttackSpeedScale.ToSignature(),
    kHook39UprisingChallengeTime.ToSignature(),
    kHook40UprisingChallengeMoney.ToSignature(),
    kHook41FrameRateUnlockGameUpdate.ToSignature(),
    kHook42ZoomClamp.ToSignature(),
    kHook43SelectedUnitAttackRangeScale.ToSignature(),
    kHook44LogicTimeFreezeGate.ToSignature(),
    kHook45SelectedUnitAutoAcquireRange.ToSignature(),
    kHook46SelectedUnitIdleAutoAcquireRange.ToSignature(),
    kHook47SelectedUnitTurretTargetAngle.ToSignature(),
    kHook48SelectedUnitTurretAimDeflection.ToSignature(),
    kRva1160.ToSignature(),
    kRva1437.ToSignature(),
    kRva143C.ToSignature(),
    kRvaE2C10.ToSignature(),
    kRvaE7573.ToSignature(),
    kRva1456F0.ToSignature(),
    kRva14678D.ToSignature(),
    kRva147260.ToSignature(),
    kRva149740.ToSignature(),
    kRva149748.ToSignature(),
    kRva1ED4A0.ToSignature(),
    kRva205240.ToSignature(),
    kRva35C200.ToSignature(),
    kRva38E651.ToSignature(),
    kRva39EA50.ToSignature(),
    kRva3E3D00.ToSignature(),
    kRva3AD79E.ToSignature(),
    kRva3ADEE2.ToSignature(),
    kRva3BA8C9.ToSignature(),
    kRva3BA9CC.ToSignature(),
    kRva3E4230.ToSignature(),
    kRva4393E0.ToSignature(),
    kRva43C300.ToSignature(),
    kRva44A2D0.ToSignature(),
    kRva44D7C0.ToSignature(),
    kRva454300.ToSignature(),
    kRva6BB430.ToSignature(),
    kRva6BB81C.ToSignature(),
    kRva6BBA87.ToSignature(),
    kRva6BBA9F.ToSignature(),
    kRva6BBAAB.ToSignature(),
    kRva6C62E8.ToSignature(),
    kRva8D8CE4.ToSignature(SignatureAddressMode::Absolute32AtOffset, 2),
    kRva8DAEFC.ToSignature(SignatureAddressMode::Absolute32AtOffset, 2),
    kRva8DB73C.ToSignature(SignatureAddressMode::Absolute32AtOffset, 2),
    kRva8DBBF0.ToSignature(SignatureAddressMode::Absolute32AtOffset, 2),
    kRva8E6C58.ToSignature(SignatureAddressMode::Absolute32AtOffset, 2),
    kRva8E8C9C.ToSignature(SignatureAddressMode::Absolute32AtOffset, 2),
    kRva8E9838.ToSignature(SignatureAddressMode::Absolute32AtOffset, 1),
    kRva88DFD0.ToSignature(SignatureAddressMode::Absolute32AtOffset, 4),
};

static_assert(sizeof(kBuiltInSignatures) / sizeof(kBuiltInSignatures[0]) == 88);
constexpr size_t kBuiltInHookCount = 48;

// Process-local memory read wrapped in SEH, matching the pattern used elsewhere in the DLL
// (AgentMemoryAccess.cpp / AgentPatchManager.cpp). Returns true on success.
bool ReadLocal(uint32_t address, void* buffer, size_t length)
{
    if (address == 0 || buffer == nullptr || length == 0)
    {
        return false;
    }

    __try
    {
        std::memcpy(buffer, reinterpret_cast<const void*>(static_cast<uintptr_t>(address)), length);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return false;
    }

    return true;
}

// Minimal PE header reader. We only need the section table to locate .text; full PE parsing
// (as in the host's PeImage) is unnecessary here because the image is already mapped and we
// can read its headers directly from the module base.
struct TextSection
{
    uint32_t VirtualAddress; // RVA of .text
    uint32_t VirtualSize;
};

bool TryReadTextSection(uint32_t moduleBase, TextSection& outSection)
{
    // DOS header -> e_lfanew -> PE signature.
    uint32_t peOffset = 0;
    if (!ReadLocal(moduleBase + 0x3C, &peOffset, sizeof(peOffset)))
    {
        return false;
    }

    const uint32_t peAddress = moduleBase + peOffset;
    uint32_t signature = 0;
    if (!ReadLocal(peAddress, &signature, sizeof(signature)) || signature != 0x00004550u) // "PE\0\0"
    {
        return false;
    }

    uint16_t numberOfSections = 0;
    uint16_t sizeOfOptionalHeader = 0;
    if (!ReadLocal(peAddress + 6, &numberOfSections, sizeof(numberOfSections)) ||
        !ReadLocal(peAddress + 20, &sizeOfOptionalHeader, sizeof(sizeOfOptionalHeader)))
    {
        return false;
    }

    const uint32_t sectionTable = peAddress + 24 + sizeOfOptionalHeader;
    for (uint16_t i = 0; i < numberOfSections; ++i)
    {
        const uint32_t entry = sectionTable + static_cast<uint32_t>(i) * 40u;
        char name[8] = {};
        if (!ReadLocal(entry, name, sizeof(name)))
        {
            return false;
        }

        // .text is conventionally the first executable section; match the standard name.
        if (std::memcmp(name, ".text", 5) != 0)
        {
            continue;
        }

        uint32_t virtualSize = 0;
        uint32_t virtualAddress = 0;
        if (!ReadLocal(entry + 8, &virtualSize, sizeof(virtualSize)) ||
            !ReadLocal(entry + 12, &virtualAddress, sizeof(virtualAddress)))
        {
            return false;
        }

        outSection.VirtualAddress = virtualAddress;
        outSection.VirtualSize = virtualSize;
        return true;
    }

    return false;
}

// Cached .text snapshot. Read once per process; re-scanning reuses the bytes. The section is
// a few MB at most, so caching it keeps repeat scans fast.
struct TextSnapshot
{
    std::vector<unsigned char> Bytes;
    uint32_t BaseAddress = 0; // absolute address of byte 0
};

const TextSnapshot* TryGetTextSnapshot()
{
    static TextSnapshot cached;
    if (!cached.Bytes.empty())
    {
        return &cached;
    }

    const uint32_t moduleBase = static_cast<uint32_t>(
        reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)));

    TextSection section = {};
    if (!TryReadTextSection(moduleBase, section) || section.VirtualSize == 0)
    {
        return nullptr;
    }

    cached.BaseAddress = moduleBase + section.VirtualAddress;
    cached.Bytes.resize(section.VirtualSize);
    if (!ReadLocal(cached.BaseAddress, cached.Bytes.data(), section.VirtualSize))
    {
        cached.Bytes.clear();
        return nullptr;
    }

    return &cached;
}

// Returns the absolute address of the first match of (bytes,mask) within the snapshot, or 0
// when there is no match. Sets *outAmbiguous when more than one match exists so callers can
// reject ambiguous signatures instead of silently picking the first.
uint32_t FindFirstMatch(
    const TextSnapshot& snapshot,
    const unsigned char* bytes,
    const unsigned char* mask,
    uint32_t length,
    bool* outAmbiguous)
{
    if (outAmbiguous != nullptr)
    {
        *outAmbiguous = false;
    }

    if (length == 0 || length > snapshot.Bytes.size())
    {
        return 0;
    }

    const auto& haystack = snapshot.Bytes;
    const size_t limit = haystack.size() - length;
    uint32_t anchor = 0;
    while (anchor < length && mask[anchor] == 0)
    {
        ++anchor;
    }
    if (anchor == length)
    {
        return 0;
    }

    uint32_t matchAddress = 0;
    size_t searchStart = 0;
    while (searchStart <= limit)
    {
        const auto* anchorHit = static_cast<const unsigned char*>(std::memchr(
            haystack.data() + searchStart + anchor,
            bytes[anchor],
            limit - searchStart + 1));
        if (anchorHit == nullptr)
        {
            break;
        }

        const size_t i = static_cast<size_t>(anchorHit - haystack.data()) - anchor;
        searchStart = i + 1;
        bool ok = true;
        for (uint32_t j = 0; j < length; ++j)
        {
            if (mask[j] != 0 && haystack[i + j] != bytes[j])
            {
                ok = false;
                break;
            }
        }

        if (!ok)
        {
            continue;
        }

        if (matchAddress == 0)
        {
            matchAddress = snapshot.BaseAddress + static_cast<uint32_t>(i);
        }
        else
        {
            // Second hit: ambiguous. Stop scanning.
            if (outAmbiguous != nullptr)
            {
                *outAmbiguous = true;
            }
            return 0;
        }
    }

    return matchAddress;
}

uint32_t ResolveMatchAddress(
    const TextSnapshot& snapshot,
    uint32_t matchAddress,
    const Signature& signature)
{
    if (matchAddress == 0)
    {
        return 0;
    }

    if (signature.AddressMode == SignatureAddressMode::MatchAddress)
    {
        return matchAddress;
    }

    if (signature.AddressMode != SignatureAddressMode::Absolute32AtOffset ||
        signature.OperandOffset > signature.Length ||
        signature.Length - signature.OperandOffset < sizeof(uint32_t) ||
        matchAddress < snapshot.BaseAddress)
    {
        return 0;
    }

    const size_t matchIndex = static_cast<size_t>(matchAddress - snapshot.BaseAddress);
    const size_t operandIndex = matchIndex + signature.OperandOffset;
    if (operandIndex > snapshot.Bytes.size() ||
        snapshot.Bytes.size() - operandIndex < sizeof(uint32_t))
    {
        return 0;
    }

    uint32_t resolved = 0;
    std::memcpy(&resolved, snapshot.Bytes.data() + operandIndex, sizeof(resolved));
    return resolved;
}

size_t ScanSnapshot(
    const TextSnapshot& snapshot,
    const Signature* signatures,
    size_t count,
    std::vector<ScanResult>& outResults)
{
    outResults.resize(count);

    size_t matched = 0;
    for (size_t i = 0; i < count; ++i)
    {
        const auto& sig = signatures[i];
        bool ambiguous = false;
        const uint32_t address = FindFirstMatch(snapshot, sig.Bytes, sig.Mask, sig.Length, &ambiguous);

        // A signature must match exactly once. Zero matches or multiple matches both leave the
        // symbol unresolved so the caller treats it as an unsupported build.
        const uint32_t resolved = (address != 0 && !ambiguous)
            ? ResolveMatchAddress(snapshot, address, sig)
            : 0;
        outResults[i] = ScanResult{ sig.SymbolicName, resolved };
        if (resolved != 0)
        {
            ++matched;
        }
    }

    return matched;
}
}

size_t ScanSignatures(
    const Signature* signatures,
    size_t count,
    std::vector<ScanResult>& outResults)
{
    outResults.resize(count);

    const auto* snapshot = TryGetTextSnapshot();
    if (snapshot == nullptr)
    {
        for (size_t i = 0; i < count; ++i)
        {
            outResults[i] = ScanResult{ signatures[i].SymbolicName, 0 };
        }
        return 0;
    }

    return ScanSnapshot(*snapshot, signatures, count, outResults);
}

bool RunBuiltInScan(std::vector<ScanResult>& outResults)
{
    const auto* snapshot = TryGetTextSnapshot();
    if (snapshot == nullptr)
    {
        outResults.clear();
        return false;
    }

    ScanSnapshot(
        *snapshot,
        kBuiltInSignatures,
        sizeof(kBuiltInSignatures) / sizeof(kBuiltInSignatures[0]),
        outResults);
    return true;
}

bool TryReadVerifiedBuiltInHookBytes(
    uint32_t address,
    uint32_t byteCount,
    std::vector<unsigned char>& outBytes)
{
    outBytes.clear();
    const auto* snapshot = TryGetTextSnapshot();
    if (snapshot == nullptr || address == 0 || byteCount == 0)
    {
        return false;
    }

    // Cache the immutable snapshot's resolved catalog. Live bytes are still read and checked
    // below on every call, so a hook that has already been patched cannot pass validation.
    static std::vector<ScanResult> resolvedHooks;
    if (resolvedHooks.empty())
    {
        std::vector<ScanResult> allResults;
        ScanSnapshot(
            *snapshot,
            kBuiltInSignatures,
            sizeof(kBuiltInSignatures) / sizeof(kBuiltInSignatures[0]),
            allResults);
        resolvedHooks.assign(allResults.begin(), allResults.begin() + kBuiltInHookCount);
    }

    for (size_t index = 0; index < kBuiltInHookCount; ++index)
    {
        if (resolvedHooks[index].Address != address)
        {
            continue;
        }

        const auto& signature = kBuiltInSignatures[index];
        if (signature.AddressMode != SignatureAddressMode::MatchAddress ||
            byteCount > signature.Length)
        {
            return false;
        }

        std::vector<unsigned char> liveSignature(signature.Length);
        if (!ReadLocal(address, liveSignature.data(), liveSignature.size()))
        {
            return false;
        }

        for (uint32_t offset = 0; offset < signature.Length; ++offset)
        {
            if (signature.Mask[offset] != 0 && liveSignature[offset] != signature.Bytes[offset])
            {
                return false;
            }
        }

        outBytes.assign(liveSignature.begin(), liveSignature.begin() + byteCount);
        return true;
    }

    return false;
}
}
