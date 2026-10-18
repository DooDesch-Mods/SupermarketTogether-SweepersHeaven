using HarmonyLib;
using Mirror;
using SweepersHeaven.Net;
using UnityEngine;

namespace SweepersHeaven.Patches;

[HarmonyPatch(typeof(NetworkServer), "RegisterMessageHandlers")]
internal static class ServerStartPatch
{
    private static void Postfix() { Plugin.Instance.RegisterServer(); }
}
[HarmonyPatch(typeof(NetworkClient), "RegisterMessageHandlers")]
internal static class ClientStartPatch
{
    private static void Postfix() { Plugin.Instance.RegisterClient(); }
}
[HarmonyPatch(typeof(NetworkTime), "OnServerPing")]
internal static class DiscoveryPatch
{
    private static bool Prefix(NetworkConnectionToClient conn, NetworkPingMessage message)
    {
        if (message.localTime != MessageCodec.DiscoveryTime || message.predictedTimeAdjusted != MessageCodec.DiscoveryVersion) return true;
        Plugin.Instance.Advertise(conn);
        return false;
    }
}
[HarmonyPatch(typeof(NetworkTime), "OnClientPong")]
internal static class VanillaDiscoveryReplyPatch
{
    private static bool Prefix(NetworkPongMessage message) { return message.localTime != MessageCodec.DiscoveryTime; }
}
[HarmonyPatch(typeof(StolenProductSpawn), nameof(StolenProductSpawn.OnStartClient))]
internal static class ProductSpawnPatch
{
    private static void Postfix(StolenProductSpawn __instance) { Plugin.Instance.Track(__instance); }
}
[HarmonyPatch(typeof(StolenProductSpawn), "UserCode_CmdRecoverStolenProduct")]
internal static class ProductRecoveryPatch
{
    private static bool Prefix(StolenProductSpawn __instance, out bool __state)
    { __state = Plugin.Instance.ClaimProduct(__instance); return __state; }
    private static void Postfix(bool __state) { if (__state) Plugin.Instance.RecoveredProducts++; }
}
[HarmonyPatch(typeof(StolenProductSpawn), nameof(StolenProductSpawn.RecoverStolenProductFromEmployee))]
internal static class EmployeeRecoveryPatch
{
    private static bool Prefix(StolenProductSpawn __instance, out bool __state)
    { __state = Plugin.Instance.ClaimProduct(__instance); return __state; }
    private static void Postfix(bool __state) { if (__state) Plugin.Instance.RecoveredProducts++; }
}
[HarmonyPatch(typeof(TrashSpawn), "UserCode_CmdClearTrash")]
internal static class TrashRecoveryPatch
{
    private static bool Prefix(TrashSpawn __instance, out bool __state)
    { __state = Plugin.Instance.ClaimTrash(__instance); return __state; }
    private static void Postfix(bool __state) { if (__state) Plugin.Instance.QueueTrash(); }
}
[HarmonyPatch(typeof(TrashSpawn), nameof(TrashSpawn.ClearTrashFromCleanerRobot))]
internal static class RobotRecoveryPatch
{
    private static bool Prefix(TrashSpawn __instance) { return Plugin.Instance.ClaimTrash(__instance); }
}
[HarmonyPatch(typeof(MiniTransportBehaviour), nameof(MiniTransportBehaviour.CheckCollision))]
internal static class VehiclePickupPatch
{
    private static void Postfix(MiniTransportBehaviour __instance, GameObject otherOBJ)
    {
        if (!Plugin.Instance.SynchronizedHost || !__instance.hasAuthority || !__instance.hasDriver || Mathf.Abs(__instance.velocity) < 0.5f || !otherOBJ) return;
        StolenProductSpawn product = otherOBJ.GetComponentInParent<StolenProductSpawn>();
        if (product) Plugin.Instance.Request(product.netId, SweepAction.Vehicle, __instance.netId);
    }
}

[HarmonyPatch(typeof(MiniTransportRework), nameof(MiniTransportRework.CheckEntitiesCollision))]
internal static class CurrentVehiclePickupPatch
{
    private static void Postfix(MiniTransportRework __instance, GameObject otherOBJ)
    {
        if (!Plugin.Instance.SynchronizedHost || !__instance.hasAuthority || !__instance.hasDriver || Mathf.Abs(__instance.velocity) < 0.5f || !otherOBJ) return;
        StolenProductSpawn product = otherOBJ.GetComponentInParent<StolenProductSpawn>();
        if (product) Plugin.Instance.Request(product.netId, SweepAction.Vehicle, __instance.netId);
    }
}
