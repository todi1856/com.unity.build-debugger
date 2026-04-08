using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.BuildDebugger
{
    class InfoWindow : EditorWindow
    {
        string m_Contents;
        TextField m_ContentsView;
        public static InfoWindow Open(string contents)
        {
            var wnd = GetWindow<InfoWindow>("Info Window");
            wnd.SetContents(contents);
            wnd.Focus();
            wnd.Show();
            return wnd;
        }

        public void CreateGUI()
        {
            LoadUI();
        }

        private void LoadUI()
        {
            var r = rootVisualElement;
            r.Clear();
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Utilities.ResolveUIPath("InfoView.uxml"));

            visualTree.CloneTree(r);

            m_ContentsView = r.Q<TextField>("contents");
            var inputElement = m_ContentsView.Q("unity-text-input");

            // Disable word wrap and allow width expansion
            inputElement.style.whiteSpace = WhiteSpace.Pre;
            inputElement.style.width = StyleKeyword.Auto;
            inputElement.style.minWidth = new Length(100, LengthUnit.Percent);

            if (!string.IsNullOrEmpty(m_Contents))
            {
                m_ContentsView.value = m_Contents;
            }
        }

        private void SetContents(string contents)
        {
            m_Contents = contents;
            if (m_ContentsView != null)
            {
                m_ContentsView.value = m_Contents;
            }
        }
    }
}