using Sirenix.OdinInspector;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Tower
{
    //TODO: 后期采用自动资源加载方式而不是拖拽方式
    public class ClientConfigComponent : GameFrameworkComponent
    {
        [LabelText("UI配置")]
        public UIConfig uIConfig;

        public UIConfigData GetUIConfigData(UIFormId formId)
        {
            return uIConfig.GetUIConfigData(formId);
        }
    
    }
}