using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tower{
    
    /// <summary>
    /// 游戏入口
    /// </summary>
    public partial class GameEntry : MonoBehaviour
    {
        void Start()
        {
            // 初始化所有游戏组件
            InitBuiltinComponents();
            InitCustomComponents();
        }

    }
}
