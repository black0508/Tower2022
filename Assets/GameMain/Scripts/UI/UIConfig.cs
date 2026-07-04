using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Tower
{
    public struct UIConfigData
    {
        [LabelText("资源名")] public string AssetName;
        [LabelText("界面组名")] public string GroupName;
        [LabelText("是否允许多个界面实例")] public bool AllowMultiInstance;
        [LabelText("是否暂停被其覆盖的界面")] public bool PauseCoveredUIForm;
    }

    [CreateAssetMenu(menuName = "Config/UIConfig")]
    public class UIConfig : SerializedScriptableObject
    {
        [SerializeField]
        private Dictionary<UIFormId, UIConfigData> _uiConfigData = new Dictionary<UIFormId, UIConfigData>();

        public UIConfigData GetUIConfigData(UIFormId uiFormId)
        {
            return _uiConfigData.TryGetValue(uiFormId, out var value) ? value : new UIConfigData();
        }

#if UNITY_EDITOR
        [Button("自动添加UI键")]
        private void AutoAddUIKeys()
        {
            var allUIFormIds = System.Enum.GetValues(typeof(UIFormId)).Cast<UIFormId>();

            foreach (var uiFormId in allUIFormIds)
            {
                if (uiFormId == UIFormId.Undefined) continue; // 跳过Undefined

                if (!_uiConfigData.ContainsKey(uiFormId))
                {
                    _uiConfigData[uiFormId] = new UIConfigData
                    {
                        AssetName = uiFormId.ToString(), // 默认使用枚举名作为资源名
                        GroupName = "Default",
                        AllowMultiInstance = false,
                        PauseCoveredUIForm = true
                    };
                }
            }

            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
    


    
}