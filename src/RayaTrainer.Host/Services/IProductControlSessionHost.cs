namespace RayaTrainer.Host.Services;

/// <summary>
/// Same-assembly exposure seam for the live <see cref="IProductControlSession"/> owned by
/// <see cref="TrainerSessionManager"/>. The session type is <c>internal</c>, so it must not
/// appear on the public <see cref="ITrainerSessionService"/> surface — that would be a CS0059
/// inconsistent-accessibility error. Managed consumers in this assembly (the U2 WPF
/// management surface and the parallel U4 Web task) reach the session via
/// <c>(_sessionManager as IProductControlSessionHost)?.ProductControl</c> and branch on the
/// structured <see cref="ProductControlNegotiation"/> / <see cref="ProductControlOutcome{T}"/>
/// results; they never reassemble those into a <c>StatusMessage</c> string.
/// </summary>
internal interface IProductControlSessionHost
{
    /// <summary>
    /// The live product-control session for the currently attached target, or <c>null</c>
    /// when no target is attached. The session's <see cref="IProductControlSession.Negotiation"/>
    /// reports the attach-time capability/identity/schema result even before it is ready, so
    /// callers can render "negotiating" / capability / fingerprint / schema states without a
    /// separate signal.
    /// </summary>
    IProductControlSession? ProductControl { get; }
}
