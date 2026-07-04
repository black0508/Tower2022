//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2020 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework.DataTable;
using GameFramework.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace Tower
{
    public static class UIExtension
    {
        public static int? OpenUIForm(this UIComponent uiComponent, UIFormId uiFormId,object userData = null)
        {
            ClientConfigComponent clientConfigComponent = GameEntry.ClientConfig;
            UIConfigData uiConfigData = clientConfigComponent.GetUIConfigData(uiFormId);
            string assetName = AssetUtility.GetUIFormAsset(uiConfigData.AssetName);
            return uiComponent.OpenUIForm(assetName,uiConfigData.GroupName,uiConfigData.PauseCoveredUIForm,userData);
        }
        public static void CloseUIForm(this UIComponent uiComponent, UGUIForm uiForm)
        {
            uiComponent.CloseUIForm(uiForm.UIForm);
        }

        public static bool HasUIForm(this UIComponent uiComponent, UIFormId uiFormId)
        {
            ClientConfigComponent clientConfigComponent = GameEntry.ClientConfig;
            UIConfigData uiConfigData = clientConfigComponent.GetUIConfigData(uiFormId);
            string assetName = AssetUtility.GetUIFormAsset(uiConfigData.AssetName);
            return uiComponent.HasUIForm(assetName);
        }

        public static UGUIForm GetUIForm(this UIComponent uiComponent, UIFormId uiFormId)
        {
            ClientConfigComponent clientConfigComponent = GameEntry.ClientConfig;
            UIConfigData uiConfigData = clientConfigComponent.GetUIConfigData(uiFormId);
            
            string assetName = AssetUtility.GetUIFormAsset(uiConfigData.AssetName);
            string uiGroupName = uiConfigData.GroupName;
            UIForm uiForm = null;
            //没有组，那就直接全局找
            if (string.IsNullOrEmpty(uiGroupName))
            {
                uiForm = uiComponent.GetUIForm(assetName);
                if (uiForm == null) return null;
                
                return (UGUIForm)uiForm.Logic;
            }
            
            IUIGroup uiGroup = uiComponent.GetUIGroup(uiGroupName);
            if (uiGroup == null)
            {
                return null;
            }
            
            uiForm = (UIForm)uiGroup.GetUIForm(assetName);
            if (uiForm == null)
            {
                return null;
            }
            
            return (UGUIForm)uiForm.Logic;
        }
        
        
        /// <summary>
        /// 图片注入点击事件
        /// </summary>
        /// <param name="image"></param>
        /// <param name="eventType"></param>
        /// <param name="action"></param>
        public static void AddListener(this Image image, EventTriggerType eventType, UnityAction<BaseEventData> action)
        {
            var trigger = image.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = image.gameObject.AddComponent<EventTrigger>();
            }

            var entry = new EventTrigger.Entry
            {
                eventID = eventType
            };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }
    }
}
