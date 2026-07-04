using Sirenix.OdinInspector;
using UnityEngine;

namespace Tower
{
    /// <summary>客户端侧配置：UI 映射等仅客户端需要的资产。</summary>
    public class ClientConfigComponent : GameConfigComponent
    {
        [LabelText("UI配置")]
        public UIConfig uIConfig;

        public UIConfigData GetUIConfigData(UIFormId formId)
        {
            return uIConfig.GetUIConfigData(formId);
        }
    }
}
