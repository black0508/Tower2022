using System;
using System.Collections.Generic;

namespace Tower
{
    public static class BuffBehaviourRegistry
    {
        static readonly Dictionary<BuffDefId, Func<BuffBehaviour>> Factories = new()
        {
            // Day3: { BuffDefId.Slow, () => new SlowBuff() },
        };

        public static void Register(BuffDefId id, Func<BuffBehaviour> factory) =>
            Factories[id] = factory;

        public static BuffBehaviour Create(BuffDefId id) =>
            Factories.TryGetValue(id, out var f) ? f() : null;

        public static BuffBehaviour Create(int id) => Create((BuffDefId)id);
    }
}
