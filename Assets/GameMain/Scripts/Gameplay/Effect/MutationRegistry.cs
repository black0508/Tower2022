using System.Collections.Generic;

namespace Tower
{
    public static class MutationRegistry
    {
        static readonly Dictionary<MutationId, MutationDef> Map = new();

        public static MutationDef Get(MutationId id) =>
            Map.TryGetValue(id, out var d) ? d : null;

        public static MutationDef Get(int id) => Get((MutationId)id);

        public static void Register(MutationDef def) => Map[def.Id] = def;
    }
}
