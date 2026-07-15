using System;

namespace Tower
{
    [Serializable]
    public struct BuffSnapshot
    {
        public int defId;
        public float startTimeOnServer;
        public float duration;
        public int stackCount;
        public byte[] payload;
    }
}
