using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using UnifiedPicoModule.Protocol;

namespace UnifiedPicoModule.Transport;

public sealed class BridgeUdpSource : UdpTrackingSource
{
    public const int Port = 9030;
    private static readonly IPAddress Multicast = IPAddress.Parse("239.255.255.250");
    private readonly string listenAddress;
    private readonly string? configuredIP;
    private readonly InputProtocol? configuredProtocol;
    private readonly int reconnectMs, controlTimeoutMs;
    private readonly BridgeSplitDecoder split = new();
    private readonly DaemonLegacyDecoder legacy = new();
    private readonly List<UnicastIPAddressInformation> discoveryInterfaces = [];
    private Socket? socket;
    private IPAddress? requiredIP;
    private IPEndPoint? peer;
    private double nextDiscovery, lastDiscovery, lastActivity, lastData, lastMode;
    private readonly int requestedMask;
    private string subscriptionId = "";
    private bool splitPeer, subscribed;
    private ulong streamEpoch;
    private int effectiveMask;
    private double nextSubscription;

    /// <summary>Headset address of the locked Bridge peer (configured or auto-discovered), or null.</summary>
    public IPAddress? PeerAddress => Volatile.Read(ref peer)?.Address;

    public BridgeUdpSource(BridgeSettings settings, Action<string> log, bool eyeEnabled = true, bool faceEnabled = true) : base(SourceKind.Bridge, log)
    {
        ArgumentNullException.ThrowIfNull(settings);
        listenAddress = settings.ListenAddress;
        configuredIP = settings.IP;
        configuredProtocol = settings.Protocol;
        reconnectMs = Math.Clamp(settings.ReconnectMs, 100, 60000);
        controlTimeoutMs = Math.Clamp(settings.ControlTimeoutMs, 1000, 120000);
        requestedMask = (eyeEnabled ? 1 : 0) | (faceEnabled ? 2 : 0);
    }

    protected override void OpenSockets()
    {
        if (configuredProtocol is not (null or InputProtocol.BridgeSplit or InputProtocol.DaemonLegacy536))
            throw new ArgumentException("Bridge.Protocol must be BridgeSplit or DaemonLegacy536");
        requiredIP = ParsePeer(configuredIP);
        socket = Bind(listenAddress, Port);
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
    }

    protected override void Tick(double now)
    {
        if (peer is not null)
        {
            if ((now - lastActivity) * 1000 >= controlTimeoutMs)
            {
                // Do not STOP a paused or transiently missing peer. End local ownership and start
                // a fresh discovery generation; only final cancellation stops the current session.
                peer = null;
                ClearSubscription();
                lastData = lastMode = 0;
                Reset(null, new(), ConnectionState.Searching, "Control/data timeout; reconnecting without STOP", timeout: true);
                Publish(Current with { LastControl = 0 });
                nextDiscovery = now;
            }
            else
            {
                if (splitPeer && !subscribed && now >= nextSubscription) SendSubscription(now);
                // Current Bridge announces mode every 10s. After two missed advertisements,
                // downgrade capability even if MARCO/data continue; never infer mode from vectors.
                var modeTimeout = Math.Min(20000, controlTimeoutMs);
                if (Current.Capabilities.ModeKnown && (now - lastMode) * 1000 >= modeTimeout)
                    Reset(Current.Protocol, new(), Current.State, "Mode advertisement expired; capabilities downgraded", timeout: true);
                if ((lastData == 0 || now - lastData > 0.25) && Current.State != ConnectionState.MissingFrames)
                    Publish(Current with { State = ConnectionState.MissingFrames,
                        Detail = subscribed && effectiveMask == 0 ? "Peer alive; negotiated channels paused by local switches" :
                            "Peer alive; missing frames (pause and producer stall cannot be distinguished)" });
                return;
            }
        }
        if (now < nextDiscovery) return;
        RefreshDiscoveryInterfaces();
        var sent = false;
        if (requiredIP is not null)
            sent = Send(socket!, BridgeControl.Discover, new(requiredIP, Port));
        else
        {
            foreach (var network in discoveryInterfaces)
            {
                try
                {
                    socket!.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                        network.Address.GetAddressBytes());
                    sent |= Send(socket, BridgeControl.Discover, new(Multicast, Port));
                }
                catch (SocketException ex) { Warn("interface", $"Discovery interface unavailable: {ex.SocketErrorCode}"); }
            }
            if (discoveryInterfaces.Count == 0)
                Warn("discovery", "No active non-virtual Ethernet/Wi-Fi IPv4 interface; configure Bridge.IP explicitly");
        }
        if (sent) lastDiscovery = now;
        nextDiscovery = now + reconnectMs / 1000d;
    }

    protected override void Receive(Socket receiver, IPEndPoint sender, ReadOnlySpan<byte> bytes, double now)
    {
        if (!Allowed(sender))
        {
            Drop("peer", "Packet rejected: Bridge endpoint/IP/subnet does not match the locked discovery peer");
            return;
        }
        if (peer is null && (lastDiscovery == 0 || now - lastDiscovery > Math.Max(1, reconnectMs / 1000d)))
        {
            Drop("unsolicited", "Packet outside the finite discovery response window rejected");
            return;
        }
        if (BridgeControl.IsPing(bytes))
        {
            LockPeer(sender, now, new());
            if (Send(receiver, BridgeControl.Polo, sender))
            {
                lastActivity = now;
                Publish(Current with { LastControl = now });
            }
            return;
        }
        if (BridgeControl.IsModeLine(bytes))
        {
            if (!BridgeControl.TryParseMode(bytes, out var capabilities))
            {
                Drop("mode", "Malformed/unknown PXR_MODE version rejected; enhancement is not assumed");
                if (peer is not null && Current.Capabilities.ModeKnown)
                    Reset(Current.Protocol, new(), Current.State, "Unknown mode; capabilities downgraded");
                return;
            }
            // Legacy eye fields do not carry the split protocol's capability guarantee. Even a
            // coincident mode line cannot silently turn the 536 decoder into independent gaze.
            if (Current.Protocol == InputProtocol.DaemonLegacy536) capabilities = new();
            LockPeer(sender, now, capabilities);
            if (capabilities != Current.Capabilities)
                Reset(Current.Protocol, capabilities, Current.State, "Bridge mode changed; cached E/F cleared");
            lastActivity = lastMode = now;
            Publish(Current with { LastControl = now });
            // Only a BridgeSplit Bridge announces its mode. Never send PXR_SUB to a peer that has
            // not: the legacy daemon treats any non-POLO reply to MARCO as a failed heartbeat.
            if (configuredProtocol == InputProtocol.DaemonLegacy536 || Current.Protocol == InputProtocol.DaemonLegacy536)
            {
                Warn("subscription-pinned", "Protocol pinned to DaemonLegacy536: BridgeSplit subscription disabled");
                return;
            }
            if (!splitPeer) Log($"Bridge: BridgeSplit peer; requesting mask={requestedMask} (host ownership AND configuration)");
            splitPeer = true;
            // Refresh the idempotent request on every announcement, even after the first
            // acknowledgement, recovering a lost request/ACK. A restarted Bridge forgets its peer
            // and announces nothing until rediscovery, so restart recovery relies on the control timeout.
            if (now >= nextSubscription) SendSubscription(now);
            return;
        }
        if (BridgeControl.IsSubscriptionLine(bytes))
        {
            if (peer is null || !splitPeer ||
                !BridgeControl.TryParseSubscription(bytes, out var id, out var mask, out var epoch) ||
                id != subscriptionId || (mask & ~requestedMask) != 0)
            {
                Drop("subscription", "Malformed, stale or unsolicited subscription acknowledgement rejected"); return;
            }
            if (subscribed && epoch < streamEpoch)
            {
                Drop("subscription", "Regressing subscription epoch rejected");
                // Defensive: a rebooted headset restarts its uptime-based epoch, but it also
                // forgets this peer, so the normal path is the control timeout. Never adopt an
                // old epoch; if data has stopped, rediscover with a fresh id instead of stalling.
                if (lastData == 0 || now - lastData > 1)
                {
                    peer = null; ClearSubscription(); lastData = lastMode = 0;
                    Reset(null, new(), ConnectionState.Searching, "Epoch regressed without fresh data; rediscovering with a new subscription id");
                    Publish(Current with { LastControl = 0 }); nextDiscovery = now;
                }
                return;
            }
            if (!subscribed || epoch != streamEpoch || mask != effectiveMask)
            {
                if (subscribed && epoch == streamEpoch) { Drop("subscription", "Channel mask changed without a new epoch"); return; }
                subscribed = true; streamEpoch = epoch; effectiveMask = mask;
                lastData = 0;
                Reset(InputProtocol.BridgeSplit, Current.Capabilities, ConnectionState.MissingFrames,
                    $"Subscription acknowledged: requested={requestedMask} effective={mask} epoch={epoch:x16}; cached channels cleared");
            }
            lastActivity = now; Publish(Current with { LastControl = now });
            return;
        }
        DecodedPacket? packet;
        if (!split.TryDecode(bytes, now, out packet) && !legacy.TryDecode(bytes, now, out packet))
        {
            Drop("format", "Unsupported Bridge length/tag; exact MARCO\\0 or known tracking packet required");
            return;
        }
        if (packet!.Protocol == InputProtocol.BridgeSplit &&
            (!subscribed || packet.Epoch != streamEpoch || packet.ChannelMask != effectiveMask))
        {
            Drop("stream-epoch", "BridgeSplit packet does not match acknowledged epoch/channel mask; awaiting control acknowledgement");
            if (splitPeer && now >= nextSubscription) SendSubscription(now);
            return;
        }
        if (configuredProtocol is not null && packet!.Protocol != configuredProtocol ||
            Current.Protocol is not null && packet!.Protocol != Current.Protocol)
        {
            Drop("protocol", "Bridge protocol mismatch; locked until the next session");
            return;
        }
        if (peer is null && !Usable(packet!))
        {
            Drop("invalid-discovery", "Invalid tracking fields cannot establish a discovery peer");
            return;
        }
        LockPeer(sender, now, new());
        if (Current.Protocol is null)
        {
            Reset(packet!.Protocol, packet.Protocol == InputProtocol.DaemonLegacy536 ? new() : Current.Capabilities,
                ConnectionState.Connected, "Validated Bridge protocol locked");
            Log($"Bridge: {packet.Protocol} compatibility path; no remote demand control, disables only affect local output. Face reception is not proof of face validity.");
        }
        if (Commit(packet!)) lastActivity = lastData = now;
    }

    private void LockPeer(IPEndPoint sender, double now, CapabilitySnapshot capabilities)
    {
        if (peer is not null) return;
        ClearSubscription();
        peer = new(sender.Address, sender.Port);
        lastActivity = now;
        lastData = 0;
        lastMode = capabilities.ModeKnown ? now : 0;
        Reset(null, capabilities, ConnectionState.MissingFrames, $"Bridge peer locked: {sender}");
        Publish(Current with { LastControl = 0 });
    }

    private void ClearSubscription()
    {
        subscriptionId = Guid.NewGuid().ToString("N")[..16];
        splitPeer = subscribed = false; streamEpoch = 0; effectiveMask = 0; nextSubscription = 0;
    }

    private void SendSubscription(double now)
    {
        if (peer is null || socket is null) return;
        Send(socket, BridgeControl.Subscribe(subscriptionId, requestedMask), peer);
        nextSubscription = now + 1;
    }

    private bool Allowed(IPEndPoint sender)
    {
        if (sender.Port != Port || !IsUnicast(sender.Address)) return false;
        if (requiredIP is not null && !requiredIP.Equals(sender.Address)) return false;
        if (peer is not null) return peer.Equals(sender);
        if (requiredIP is not null) return true;
        return discoveryInterfaces.Any(network => SameSubnet(network, sender.Address));
    }

    private static bool SameSubnet(UnicastIPAddressInformation network, IPAddress address)
    {
        var local = network.Address.GetAddressBytes();
        var remote = address.GetAddressBytes();
        var mask = network.IPv4Mask.GetAddressBytes();
        if (mask.Length != 4 || mask.All(x => x == 0)) return false;
        for (var i = 0; i < 4; i++)
            if ((local[i] & mask[i]) != (remote[i] & mask[i])) return false;
        return !local.SequenceEqual(remote); // Auto-discovery does not pick a local process.
    }

    private void RefreshDiscoveryInterfaces()
    {
        discoveryInterfaces.Clear();
        foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (card.OperationalStatus != OperationalStatus.Up || !card.SupportsMulticast ||
                card.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)) continue;
            var name = card.Name + " " + card.Description;
            if (new[] { "virtual", "vpn", "tunnel", "wireguard", "zerotier", "tailscale", "tap-", "hyper-v", "vmware", "virtualbox" }
                .Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var address in card.GetIPProperties().UnicastAddresses)
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address) &&
                    (listenAddress == "0.0.0.0" || address.Address.ToString() == listenAddress))
                    discoveryInterfaces.Add(address);
        }
    }

    protected override void StopOwnedSession()
    {
        if (peer is null || socket is null) return;
        Send(socket, BridgeControl.Stop, peer); // Only our current validated, discovered endpoint.
        peer = null;
        ClearSubscription();
    }
}
