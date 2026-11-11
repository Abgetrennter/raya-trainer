namespace RayaTrainer.Core.Memory;

public sealed record Win32MemoryRegion(
    nint BaseAddress,
    nuint RegionSize,
    uint AllocationProtect,
    uint State,
    uint Protect,
    uint Type)
{
    public override string ToString()
    {
        return $"base=0x{BaseAddress:X}, size=0x{RegionSize:X}, " +
            $"allocationProtect=0x{AllocationProtect:X}, state=0x{State:X}, protect=0x{Protect:X}, type=0x{Type:X}";
    }
}
