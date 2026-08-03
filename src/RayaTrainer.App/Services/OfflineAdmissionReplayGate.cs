using RayaTrainer.Core.Agent;

namespace RayaTrainer.App.Services;

/// <summary>
/// Deduplicates App-side desired-state replay by positively proven MapEpoch.
/// Non-ready, unproven and zero-epoch observations never authorize replay.
/// </summary>
internal sealed class OfflineAdmissionReplayGate
{
    private ulong _lastReplayedMapEpoch;

    public bool ShouldReplay(
        MatchLifecycle lifecycle,
        SinglePlayerProof singlePlayerProof,
        ulong mapEpoch)
    {
        if (lifecycle != MatchLifecycle.Ready ||
            singlePlayerProof != SinglePlayerProof.Proven ||
            mapEpoch == 0 ||
            mapEpoch == _lastReplayedMapEpoch)
        {
            return false;
        }

        return true;
    }

    public void MarkReplayed(ulong mapEpoch)
    {
        if (mapEpoch != 0)
        {
            _lastReplayedMapEpoch = mapEpoch;
        }
    }

    public void Reset() => _lastReplayedMapEpoch = 0;
}
