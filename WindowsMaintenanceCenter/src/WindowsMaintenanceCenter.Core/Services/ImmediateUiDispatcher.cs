using WindowsMaintenanceCenter.Core.Abstractions;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Dispatcher that executes callbacks on the calling thread. Used by unit tests and by any host
/// without a message loop, so that services never depend on a UI framework.
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => true;

    public void Post(Action action) => action();

    public void Invoke(Action action) => action();
}
