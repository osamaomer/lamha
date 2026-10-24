using System.Net.NetworkInformation;
using Lamha.Core.Translation;

namespace Lamha.Windows.Shell;

/// <summary>
/// Whether this PC has a network at all: the translation service skips the internet straight away without one, as
/// Lamha 2 did with the browser's <c>navigator.onLine</c> (connected to something, not proven to reach the internet).
/// </summary>
public sealed class NetworkStatus : INetworkStatus
{
    public bool IsOffline => !NetworkInterface.GetIsNetworkAvailable();
}
