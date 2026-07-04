using System.Collections.Generic;
using UnityEngine;

namespace Tower
{
    /// <summary>
    /// 建造栏容器：扫描子 TowerCardUI，统一驱动 Init/Clear，保证初始化顺序。
    /// 不转发任何具体事件——子卡片自己订阅自己关心的状态。
    /// </summary>
    public class BuildBarUI : MonoBehaviour
    {
        readonly List<TowerCardUI> cards = new();

        public void Init()
        {
            GetComponentsInChildren(true, cards);
            for (int i = 0; i < cards.Count; i++)
                cards[i].Init();
        }

        public void Clear()
        {
            for (int i = 0; i < cards.Count; i++)
                cards[i].Clear();
            cards.Clear();
        }
    }
}
