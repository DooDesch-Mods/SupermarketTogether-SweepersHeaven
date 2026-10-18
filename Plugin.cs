using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Mirror;
using StarterAssets;
using SweepersHeaven.Net;
using UnityEngine;

namespace SweepersHeaven;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, "SweepersHeaven", MyPluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    private sealed class Peer { internal readonly RequestGate Gate = new RequestGate(); }
    private sealed class Pending { internal StolenProductSpawn Item; internal float At; }
    internal static Plugin Instance;
    private Harmony harmony;
    private ConfigEntry<bool> featureEnabled;
    private ConfigEntry<float> pickupRadius;
    private ConfigEntry<int> maxItems;
    private ConfigEntry<bool> throwItems;
    private ConfigEntry<KeyCode> pickupKey;
    private ConfigEntry<bool> vehiclePickup;
    private ConfigEntry<bool> allTrash;
    private ConfigEntry<float> cooldown;
    private readonly Dictionary<NetworkConnectionToClient, Peer> peers = new Dictionary<NetworkConnectionToClient, Peer>();
    private readonly Dictionary<uint, Pending> pending = new Dictionary<uint, Pending>();
    private readonly Dictionary<uint, StolenProductSpawn> tracked = new Dictionary<uint, StolenProductSpawn>();
    private readonly Dictionary<uint, ProductMotion> earlyMotion = new Dictionary<uint, ProductMotion>();
    private readonly Dictionary<uint, ProductMotion> lastMotion = new Dictionary<uint, ProductMotion>();
    private readonly Dictionary<uint, float> lastMotionAt = new Dictionary<uint, float>();
    private readonly List<uint> removeIds = new List<uint>();
    private readonly List<NetworkConnectionToClient> removePeers = new List<NetworkConnectionToClient>();
    private readonly Queue<TrashSpawn> trashQueue = new Queue<TrashSpawn>();
    private readonly RecoveryLedger productsRecovered = new RecoveryLedger();
    private readonly RecoveryLedger trashRecovered = new RecoveryLedger();
    private readonly MethodInfo recover = AccessTools.Method(typeof(StolenProductSpawn), "UserCode_CmdRecoverStolenProduct");
    private readonly MethodInfo clearTrash = AccessTools.Method(typeof(TrashSpawn), "UserCode_CmdClearTrash");
    private readonly MethodInfo animate = AccessTools.Method(typeof(PlayerNetwork), "UserCode_CmdPlayAnimation__Int32");
    private HostSettings host;
    private uint sequence;
    private int probes;
    private float nextProbe;
    private float nextMotion;
    private float localCooldown;
    private bool wasServer;
    private bool wasClient;
    private bool processingTrash;
    internal int AcceptedRequests;
    internal int RecoveredProducts;
    internal int RejectedRequests;
    internal int FailedRecoveries;
    internal bool SynchronizedHost => featureEnabled.Value && host.Enabled && host.Protocol == MessageCodec.Protocol;

    private void Awake()
    {
        Instance = this;
        featureEnabled = Config.Bind("General", "Enabled", true, "Enable SweepersHeaven. The host controls shared actions.");
        pickupRadius = Config.Bind("Pickup Settings", "Pickup Radius", 2.5f,
            new ConfigDescription("Host: radius around the product you aim at.", new AcceptableValueRange<float>(0.5f, 5f)));
        maxItems = Config.Bind("Pickup Settings", "Max Items to Pick", 5,
            new ConfigDescription("Host: maximum products in one sweep or throw.", new AcceptableValueRange<int>(1, 50)));
        throwItems = Config.Bind("Pickup Settings", "Throw Items on Pickup", true, "Host: lift products during collection and allow right-click throws.");
        pickupKey = Config.Bind("Controls", "Pickup Key", KeyCode.Mouse0, "Collect products with the broom. Right mouse button throws them.");
        vehiclePickup = Config.Bind("Mini Transporter", "Auto Pickup", true, "Host: recover products touched by a moving Mini Transporter.");
        allTrash = Config.Bind("Trash", "Pick Up All Trash At Once", false, "Host: one normal trash pickup clears the store's current trash.");
        cooldown = Config.Bind("Pickup Settings", "Sweep Cooldown", 1f,
            new ConfigDescription("Host: minimum seconds between sweeps from one player.", new AcceptableValueRange<float>(0.15f, 5f)));
        if (recover == null || clearTrash == null || animate == null) throw new MissingMethodException("The game's server collection or broom animation methods were not found.");
        MessageCodec.Register();
        harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        Config.SettingChanged += SettingsChanged;
        Logger.LogInfo("SweepersHeaven loaded. The host validates collection and sends product motion to supported peers.");
    }

    private HostSettings Settings()
    {
        return new HostSettings { Protocol = MessageCodec.Protocol, Enabled = featureEnabled.Value,
            Radius = pickupRadius.Value, Limit = maxItems.Value, Cooldown = cooldown.Value, Throw = throwItems.Value, Vehicle = vehiclePickup.Value };
    }

    private void SettingsChanged(object sender, SettingChangedEventArgs args)
    {
        if (!NetworkServer.active) return;
        host = Settings();
        foreach (NetworkConnectionToClient connection in peers.Keys)
            if (connection.isReady) connection.Send(host);
    }

    internal void RegisterServer()
    {
        ResetServer();
        MessageCodec.Register();
        NetworkServer.RegisterHandler<SweepRequest>(HandleRequest);
        host = Settings();
    }

    internal void RegisterClient()
    {
        ResetClient();
        MessageCodec.Register();
        NetworkClient.RegisterHandler<HostSettings>(value =>
        {
            if (value.Protocol != MessageCodec.Protocol)
            {
                Logger.LogWarning($"Unsupported SweepersHeaven protocol {value.Protocol}. Expected {MessageCodec.Protocol}.");
                return;
            }
            host = value;
            Logger.LogInfo($"Host settings received: enabled={host.Enabled}, radius={host.Radius}, limit={host.Limit}.");
        });
        NetworkClient.RegisterHandler<ProductMotion>(ReceiveMotion);
    }

    internal void Advertise(NetworkConnectionToClient connection)
    {
        if (!NetworkServer.active || connection == null || !connection.isAuthenticated) return;
        if (!peers.ContainsKey(connection))
        {
            peers.Add(connection, new Peer());
            Logger.LogInfo($"SweepersHeaven peer registered: connection {connection.connectionId}.");
        }
        connection.Send(Settings());
        foreach (StolenProductSpawn product in UnityEngine.Object.FindObjectsOfType<StolenProductSpawn>()) Track(product);
        if (connection != NetworkServer.localConnection)
            foreach (StolenProductSpawn product in tracked.Values)
                if (product) connection.Send(Motion(product));
    }

    private void Update()
    {
        bool server = NetworkServer.active;
        bool client = NetworkClient.active;
        if (wasServer && !server) ResetServer();
        if (wasClient && !client) ResetClient();
        wasServer = server;
        wasClient = client;
        if (server)
        {
            host = Settings();
            ServerTick();
        }
        if (client && !server && NetworkClient.ready && NetworkClient.isConnected && host.Protocol == 0 && Time.unscaledTime >= nextProbe)
        {
            if (probes < 3)
            {
                // Vanilla hosts can read this standard message. No unknown custom type is sent to them.
                NetworkClient.Send(new NetworkPingMessage(MessageCodec.DiscoveryTime, MessageCodec.DiscoveryVersion));
                probes++;
                nextProbe = Time.unscaledTime + 2f;
            }
            else
            {
                host.Protocol = 255;
                Logger.LogWarning("This host did not advertise SweepersHeaven. Normal game pickup remains available; synchronized sweeps are disabled.");
            }
        }
        ReadBroomInput();
    }

    private void ReadBroomInput()
    {
        if (!SynchronizedHost || !NetworkClient.ready || Time.unscaledTime < localCooldown) return;
        FirstPersonController controller = FirstPersonController.Instance;
        if (!controller || !controller.allowPlayerInput || !Camera.main) return;
        PlayerNetwork player = controller.GetComponent<PlayerNetwork>();
        if (!player || !player.isLocalPlayer || player.equippedItem != 3) return;
        bool collect = Input.GetKeyDown(pickupKey.Value);
        bool toss = Input.GetMouseButtonDown(1);
        if (!collect && !toss) return;
        if (!Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, 5f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
        StolenProductSpawn product = hit.collider.GetComponentInParent<StolenProductSpawn>();
        if (!product) return;
        if (toss && !collect && !host.Throw) return;
        Request(product.netId, collect ? SweepAction.Collect : SweepAction.Throw, 0);
        localCooldown = Time.unscaledTime + host.Cooldown;
    }

    internal void Request(uint target, SweepAction action, uint vehicle)
    {
        if (!SynchronizedHost || !NetworkClient.ready) return;
        SweepRequest request = new SweepRequest { Sequence = ++sequence, Target = target, Action = action, Vehicle = vehicle };
        if (NetworkServer.active)
        {
            NetworkConnectionToClient connection = NetworkServer.localConnection;
            if (connection != null)
            {
                if (!peers.ContainsKey(connection)) peers.Add(connection, new Peer());
                HandleRequest(connection, request);
            }
        }
        else NetworkClient.Send(request);
    }

    private static PlayerNetwork Actor(NetworkConnectionToClient connection)
    {
        if (connection.identity)
        {
            PlayerNetwork direct = connection.identity.GetComponent<PlayerNetwork>();
            if (direct) return direct;
        }
        foreach (NetworkIdentity identity in connection.owned)
        {
            if (!identity) continue;
            PlayerNetwork player = identity.GetComponent<PlayerNetwork>();
            if (player) return player;
        }
        return null;
    }

    internal void HandleRequest(NetworkConnectionToClient connection, SweepRequest request)
    {
        if (!featureEnabled.Value || connection == null || !connection.isReady || !peers.TryGetValue(connection, out Peer peer)) return;
        if (!peer.Gate.Accept(request.Sequence, Time.unscaledTime, request.Action == SweepAction.Vehicle ? 0.15 : cooldown.Value))
        { RejectedRequests++; return; }
        PlayerNetwork player = Actor(connection);
        if (!player || !NetworkServer.spawned.TryGetValue(request.Target, out NetworkIdentity identity))
        { RejectedRequests++; return; }
        StolenProductSpawn target = identity.GetComponent<StolenProductSpawn>();
        if (!target || productsRecovered.Contains(target.netId) || pending.ContainsKey(target.netId)) return;
        Transform origin = player.transform;
        bool vehicle = request.Action == SweepAction.Vehicle;
        if (vehicle)
        {
            if (!vehiclePickup.Value || !NetworkServer.spawned.TryGetValue(request.Vehicle, out NetworkIdentity vehicleIdentity)) return;
            MiniTransportBehaviour transport = vehicleIdentity.GetComponent<MiniTransportBehaviour>();
            MiniTransportRework currentTransport = vehicleIdentity.GetComponent<MiniTransportRework>();
            bool hasDriver = (transport && transport.hasDriver) || (currentTransport && currentTransport.hasDriver);
            if (!hasDriver || vehicleIdentity.connectionToClient != connection) return;
            origin = vehicleIdentity.transform;
            if ((origin.position - target.transform.position).sqrMagnitude > 16f) return;
        }
        else if ((request.Action != SweepAction.Collect && request.Action != SweepAction.Throw) || player.equippedItem != 3 ||
                 (origin.position - target.transform.position).sqrMagnitude > 36f ||
                 (request.Action == SweepAction.Throw && !throwItems.Value))
        { RejectedRequests++; return; }
        List<StolenProductSpawn> items = Nearby(target, vehicle ? 1 : maxItems.Value);
        if (items.Count == 0) return;
        AcceptedRequests++;
        if (!vehicle)
        {
            try { animate.Invoke(player, new object[] { 0 }); }
            catch (TargetInvocationException error)
            { Logger.LogWarning($"Broom animation failed in the game RPC: {error.InnerException ?? error}. Product collection continues."); }
        }
        foreach (StolenProductSpawn item in items)
        {
            Track(item);
            if (throwItems.Value)
            {
                Rigidbody body = item.GetComponent<Rigidbody>();
                if (body) body.AddForce((Vector3.up + origin.forward * 0.35f) * 3.5f, ForceMode.Impulse);
            }
            if (request.Action != SweepAction.Throw)
                pending.Add(item.netId, new Pending { Item = item, At = Time.unscaledTime + (throwItems.Value ? 0.35f : 0f) });
        }
        nextMotion = 0f;
    }

    private List<StolenProductSpawn> Nearby(StolenProductSpawn target, int limit)
    {
        Dictionary<uint, StolenProductSpawn> unique = new Dictionary<uint, StolenProductSpawn>();
        unique[target.netId] = target;
        foreach (Collider collider in Physics.OverlapSphere(target.transform.position, pickupRadius.Value,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            StolenProductSpawn item = collider.GetComponentInParent<StolenProductSpawn>();
            if (item && item.netId != 0 && !pending.ContainsKey(item.netId) && !productsRecovered.Contains(item.netId))
                unique[item.netId] = item;
        }
        List<StolenProductSpawn> result = new List<StolenProductSpawn>(unique.Values);
        result.Sort((a, b) =>
        {
            float da = (a.transform.position - target.transform.position).sqrMagnitude;
            float db = (b.transform.position - target.transform.position).sqrMagnitude;
            int distance = da.CompareTo(db);
            return distance != 0 ? distance : a.netId.CompareTo(b.netId);
        });
        if (result.Count > limit) result.RemoveRange(limit, result.Count - limit);
        return result;
    }

    internal bool ClaimProduct(StolenProductSpawn product)
    {
        if (!NetworkServer.active) return false;
        if (!productsRecovered.Claim(product.netId)) return false;
        return true;
    }

    internal bool ClaimTrash(TrashSpawn trash)
    {
        return NetworkServer.active && trashRecovered.Claim(trash.netId);
    }

    internal void QueueTrash()
    {
        if (!featureEnabled.Value || !allTrash.Value || processingTrash || trashQueue.Count > 0 || !NetworkServer.active) return;
        foreach (TrashSpawn trash in UnityEngine.Object.FindObjectsOfType<TrashSpawn>())
            if (!trashRecovered.Contains(trash.netId)) trashQueue.Enqueue(trash);
    }

    private void InvokeRecovery(MethodInfo method, object target, string operation)
    {
        try { method.Invoke(target, null); }
        catch (TargetInvocationException error)
        {
            // A partially applied native recovery must not be replayed and pay twice.
            FailedRecoveries++;
            Logger.LogError($"{operation} failed in the game server method: {error.InnerException ?? error}. The transaction will not be replayed.");
        }
    }

    private void ServerTick()
    {
        removeIds.Clear();
        foreach (KeyValuePair<uint, Pending> pair in pending)
        {
            if (Time.unscaledTime < pair.Value.At) continue;
            if (pair.Value.Item && !productsRecovered.Contains(pair.Key))
                InvokeRecovery(recover, pair.Value.Item, $"Recover product {pair.Key}");
            removeIds.Add(pair.Key);
        }
        foreach (uint id in removeIds) pending.Remove(id);
        processingTrash = true;
        try
        {
            for (int i = 0; i < 16 && trashQueue.Count > 0; i++)
            {
                TrashSpawn trash = trashQueue.Dequeue();
                if (trash && !trashRecovered.Contains(trash.netId)) InvokeRecovery(clearTrash, trash, $"Clear trash {trash.netId}");
            }
        }
        finally { processingTrash = false; }
        removePeers.Clear();
        foreach (NetworkConnectionToClient connection in peers.Keys)
            if (!NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) || current != connection)
                removePeers.Add(connection);
        foreach (NetworkConnectionToClient connection in removePeers) peers.Remove(connection);
        if (Time.unscaledTime < nextMotion) return;
        nextMotion = Time.unscaledTime + 0.1f;
        removeIds.Clear();
        foreach (KeyValuePair<uint, StolenProductSpawn> pair in tracked)
        {
            if (!pair.Value) { removeIds.Add(pair.Key); continue; }
            if (!featureEnabled.Value) continue;
            ProductMotion motion = Motion(pair.Value);
            bool heartbeat = !lastMotionAt.TryGetValue(pair.Key, out float at) || Time.unscaledTime - at >= 2f;
            bool changed = !lastMotion.TryGetValue(pair.Key, out ProductMotion last) ||
                (last.Position - motion.Position).sqrMagnitude > 0.000025f || Quaternion.Angle(last.Rotation, motion.Rotation) > 0.5f;
            if (!heartbeat && !changed) continue;
            lastMotion[pair.Key] = motion;
            lastMotionAt[pair.Key] = Time.unscaledTime;
            foreach (NetworkConnectionToClient connection in peers.Keys)
                if (connection.isReady && connection != NetworkServer.localConnection)
                    connection.Send(motion, heartbeat ? Channels.Reliable : Channels.Unreliable);
        }
        foreach (uint id in removeIds) { tracked.Remove(id); lastMotion.Remove(id); lastMotionAt.Remove(id); }
    }

    private static ProductMotion Motion(StolenProductSpawn product)
    {
        return new ProductMotion { Id = product.netId, Position = product.transform.position, Rotation = product.transform.rotation };
    }

    internal void Track(StolenProductSpawn product)
    {
        if (!product || product.netId == 0) return;
        tracked[product.netId] = product;
        if (NetworkServer.active) return;
        ProductMotionFollower follower = product.GetComponent<ProductMotionFollower>();
        if (!follower)
        {
            follower = product.gameObject.AddComponent<ProductMotionFollower>();
            follower.Initialize(product);
        }
        if (earlyMotion.TryGetValue(product.netId, out ProductMotion motion))
        {
            follower.Receive(motion);
            earlyMotion.Remove(product.netId);
        }
    }

    private void ReceiveMotion(ProductMotion motion)
    {
        if (NetworkServer.active || !SynchronizedHost || motion.Id == 0) return;
        if (NetworkClient.spawned.TryGetValue(motion.Id, out NetworkIdentity identity))
        {
            StolenProductSpawn product = identity.GetComponent<StolenProductSpawn>();
            if (!product) return;
            Track(product);
            product.GetComponent<ProductMotionFollower>().Receive(motion);
        }
        else if (earlyMotion.Count < 4096) earlyMotion[motion.Id] = motion;
    }

    internal void ResetServer()
    {
        peers.Clear(); pending.Clear(); trashQueue.Clear(); productsRecovered.Clear(); trashRecovered.Clear();
        tracked.Clear(); lastMotion.Clear(); lastMotionAt.Clear();
        AcceptedRequests = RecoveredProducts = RejectedRequests = FailedRecoveries = 0;
        processingTrash = false;
    }

    internal void ResetClient()
    {
        foreach (StolenProductSpawn product in tracked.Values)
            if (product) product.GetComponent<ProductMotionFollower>()?.Restore();
        tracked.Clear(); earlyMotion.Clear(); host = default;
        sequence = 0; probes = 0; nextProbe = localCooldown = 0f;
    }

    private void OnDestroy()
    {
        Config.SettingChanged -= SettingsChanged;
        ResetClient(); ResetServer();
        NetworkClient.UnregisterHandler<HostSettings>(); NetworkClient.UnregisterHandler<ProductMotion>();
        NetworkServer.UnregisterHandler<SweepRequest>();
        harmony?.UnpatchSelf();
        if (Instance == this) Instance = null;
    }
}
