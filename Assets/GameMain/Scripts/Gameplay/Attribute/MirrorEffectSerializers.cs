using Mirror;

namespace Tower
{
    public static class MirrorEffectSerializers
    {
        public static void WriteAttributeEntry(this NetworkWriter writer, AttributeEntry value)
        {
            writer.WriteInt((int)value.key);
            writer.WriteFloat(value.value);
        }

        public static AttributeEntry ReadAttributeEntry(this NetworkReader reader)
        {
            return new AttributeEntry
            {
                key = (AttributeKey)reader.ReadInt(),
                value = reader.ReadFloat(),
            };
        }

        public static void WriteBuffSnapshot(this NetworkWriter writer, BuffSnapshot value)
        {
            writer.WriteInt(value.defId);
            writer.WriteFloat(value.startTimeOnServer);
            writer.WriteFloat(value.duration);
            writer.WriteInt(value.stackCount);
            writer.WriteBytesAndSize(value.payload);
        }

        public static BuffSnapshot ReadBuffSnapshot(this NetworkReader reader)
        {
            return new BuffSnapshot
            {
                defId = reader.ReadInt(),
                startTimeOnServer = reader.ReadFloat(),
                duration = reader.ReadFloat(),
                stackCount = reader.ReadInt(),
                payload = reader.ReadBytesAndSize(),
            };
        }
    }
}
