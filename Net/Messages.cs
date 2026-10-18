using Mirror;
using UnityEngine;

namespace SweepersHeaven.Net;

internal enum SweepAction : byte { Collect, Throw, Vehicle }
internal struct SweepRequest : NetworkMessage
{
    internal uint Sequence;
    internal uint Target;
    internal uint Vehicle;
    internal SweepAction Action;
}
internal struct HostSettings : NetworkMessage
{
    internal byte Protocol;
    internal bool Enabled;
    internal float Radius;
    internal int Limit;
    internal float Cooldown;
    internal bool Throw;
    internal bool Vehicle;
}
internal struct ProductMotion : NetworkMessage
{
    internal uint Id;
    internal Vector3 Position;
    internal Quaternion Rotation;
}

internal static class MessageCodec
{
    internal const byte Protocol = 1;
    // A normal Mirror ping uses nonnegative elapsed time. These finite values cannot be a clock sample.
    internal const double DiscoveryTime = -931047.125;
    internal const double DiscoveryVersion = -931047.25;

    internal static void Register()
    {
        Writer<SweepRequest>.write = (writer, value) =>
        {
            writer.WriteUInt(value.Sequence); writer.WriteUInt(value.Target);
            writer.WriteUInt(value.Vehicle); writer.WriteByte((byte)value.Action);
        };
        Reader<SweepRequest>.read = reader => new SweepRequest
        {
            Sequence = reader.ReadUInt(), Target = reader.ReadUInt(), Vehicle = reader.ReadUInt(),
            Action = (SweepAction)reader.ReadByte()
        };
        Writer<HostSettings>.write = (writer, value) =>
        {
            writer.WriteByte(value.Protocol); writer.WriteBool(value.Enabled); writer.WriteFloat(value.Radius);
            writer.WriteInt(value.Limit); writer.WriteFloat(value.Cooldown); writer.WriteBool(value.Throw); writer.WriteBool(value.Vehicle);
        };
        Reader<HostSettings>.read = reader => new HostSettings
        {
            Protocol = reader.ReadByte(), Enabled = reader.ReadBool(), Radius = reader.ReadFloat(),
            Limit = reader.ReadInt(), Cooldown = reader.ReadFloat(), Throw = reader.ReadBool(), Vehicle = reader.ReadBool()
        };
        Writer<ProductMotion>.write = (writer, value) =>
        {
            writer.WriteUInt(value.Id); writer.WriteVector3(value.Position); writer.WriteQuaternion(value.Rotation);
        };
        Reader<ProductMotion>.read = reader => new ProductMotion
        {
            Id = reader.ReadUInt(), Position = reader.ReadVector3(), Rotation = reader.ReadQuaternion()
        };
    }
}
