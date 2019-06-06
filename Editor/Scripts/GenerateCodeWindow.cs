
//using Rotorz.ReorderableList;
using Rotorz.Games.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;


namespace Quick.Code
{

    public class GenerateCodeWindow : EditorWindow
    {
        //配置项：
        public bool filterNotRaycastTarget = false;
        public string namePrefix = "";
        public bool hasTypesuffix = false;



        [MenuItem("QuickTool/QuickGenCode")]
        public static void OpenWindow()
        {
            if (codeWindow == null)
                codeWindow = EditorWindow.GetWindow(typeof(GenerateCodeWindow)) as GenerateCodeWindow;

            Texture2D icon = (Texture2D)EditorGUIUtility.Load(iconPath);
            codeWindow.titleContent = new GUIContent("QuickCode", icon);
            codeWindow.Show();

        }

        private static string iconPath = "Assets/QuickCode/Editor/Images/icon.png";
        private static GenerateCodeWindow codeWindow = null;
        private SerializedObject serializedObj;

        //选择的根游戏体
        private GameObject root;
        //ui控件列表
        private List<UIBehaviour> uiWidgets = new List<UIBehaviour>();
        //ui游戏对象列表
        private List<GameObject> uiObjects = new List<GameObject>();
        //视图宽度一半
        private float halfViewWidth;
        //视图高度一半
        private float halfViewHeight;

        private Vector2 scrollWidgetPos;
        private Vector2 scrollObjectPos;
        private Vector2 scrollTextPos;

        private int selectedBar = 1;
        private bool isMono = true;

        #region 代码生成
        private StringBuilder codeStateText;
        private StringBuilder codeEventText;
        private StringBuilder codeAssignText;
        private StringBuilder codeAllText;

        public Dictionary<string,string> widgetNameMap = new Dictionary<string, string>{{"Toggle", "UITools.c_toggle" },
        { "Image", "UITools.c_Image" }, { "TextMeshProUGUI", " UITools.c_TMP" }, { "Button", " UITools.c_Button" }, { "RectTransform", " UITools.c_RectTrans" }, { "InputField", " UITools.c_Input" }};

        public List<string> allowableWidgets = new List<string> { "Toggle", "Image", "TextMeshProUGUI", "Button", "InputField"};


        //缓存所有变量名和对应控件对象，对重名作处理
        private Dictionary<string, object> variableNameDic = new Dictionary<string, object>();
        //变量编号
        private int variableNum;
        //需要注册事件的控件,可通过toggle选择
        private Dictionary<string, bool> selectedEventWidgets = new Dictionary<string, bool>();
        //UI 类名
        private string className;
        //生成脚本的类型
        private Type scriptType;
        #endregion

        private GameObject SelectedObject = null;

        private List<GameObject> tempDeletedGameobjectList = new List<GameObject>();
        private List<GameObject> deletedGameobjectList = new List<GameObject>();

        #region 代码格式分类
        private string regionStartFmt { get { return selectedBar == 0 ? CodeConfig.regionStartFmt : CodeConfig.regionStartFmtLua; } }
        private string regionEnd { get { return selectedBar == 0 ? CodeConfig.regionEnd : CodeConfig.regionEndLua; } }
        private string statementRegion { get { return CodeConfig.statementRegion; } }
        private string eventRegion { get { return selectedBar == 0 ? CodeConfig.eventRegion : CodeConfig.eventRegionLua; } }
        private string assignRegion { get { return selectedBar == 0 ? CodeConfig.assignRegion : CodeConfig.assignRegionLua; } }
        private string methodStartFmt { get { return selectedBar == 0 ? CodeConfig.methodStartFmt : CodeConfig.methodStartFmtLua; } }
        private string methodEnd { get { return selectedBar == 0 ? CodeConfig.methodEnd : CodeConfig.methodEndLua; } }
        private string assignCodeFmt { get { return selectedBar == 0 ? CodeConfig.assignCodeFmt : CodeConfig.assignCodeFmtLua; } }
        private string assignGameObjectCodeFmt { get { return selectedBar == 0 ? CodeConfig.assignGameObjectCodeFmt : CodeConfig.assignGameObjectCodeFmtLua; } }
        private string assignRootCodeFmt { get { return selectedBar == 0 ? CodeConfig.assignRootCodeFmt : CodeConfig.assignRootCodeFmtLua; } }
        private string onClickSerilCode { get { return selectedBar == 0 ? CodeConfig.onClickSerilCode : CodeConfig.onClickSerilCodeLua; } }

        private string onImageClickSerilCode { get { return selectedBar == 0 ? CodeConfig.onClickSerilCode : CodeConfig.onImageClickSerilCodeLua; } }
        private string onValueChangeSerilCode { get { return selectedBar == 0 ? CodeConfig.onValueChangeSerilCode : CodeConfig.onValueChangeSerilCodeLua; } }
        private string btnCallbackSerilCode { get { return selectedBar == 0 ? CodeConfig.btnCallbackSerilCode : CodeConfig.btnCallbackSerilCodeLua; } }
        private string eventCallbackSerilCode { get { return selectedBar == 0 ? CodeConfig.eventCallbackSerilCode : CodeConfig.eventCallbackSerilCodeLua; } }
        #endregion


        void OnEnable()
        {
            serializedObj = new SerializedObject(this);

            Selection.selectionChanged = delegate
            {
                //SelectedObject = (GameObject)Selection.activeObject;

                tempDeletedGameobjectList.Clear();
                foreach (var item in Selection.gameObjects)
                {
                    tempDeletedGameobjectList.Add(item);
                    //Debug.Log("选择数组:" + item.name);
                }
            };
        }

        void OnGUI()
        {
            serializedObj.Update();

            if (codeWindow == null)
            {
                codeWindow = GetWindow<GenerateCodeWindow>();
            }
            halfViewWidth = EditorGUIUtility.currentViewWidth / 2f;
            halfViewHeight = codeWindow.position.height / 2f;

            using (new EditorGUILayout.HorizontalScope())
            {
                //左半部分
                using (EditorGUILayout.VerticalScope vScope = new EditorGUILayout.VerticalScope(GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.5f)))
                {
                    GUI.backgroundColor = Color.white;
                    Rect rect = vScope.rect;
                    rect.height = codeWindow.position.height * 0.8f;
                    GUI.Box(rect, "");

                    DrawSelectUI();
                    DrawFindWidget();
                    DrawWidgetDetail();
                    DrawWidgetList();
                    DrawCustomObjectList();
                }
                //右半部分
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.5f)))
                {
                    DrawCodeGenTitle();
                    DrawCodeGenToolBar();
                }
            }

            serializedObj.ApplyModifiedProperties();
        }

        /// <summary>
        /// 绘制 选择要分析的UI
        /// </summary>
        private void DrawSelectUI()
        {
            EditorGUILayout.Space();
            using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = Color.white;
                Rect rect = hScope.rect;
                rect.height = EditorGUIUtility.singleLineHeight;
                GUI.Box(rect, "");

                EditorGUILayout.LabelField("选择待处理UI:", GUILayout.Width(halfViewWidth / 4f));
                GameObject lastRoot = root;
                root = EditorGUILayout.ObjectField(root, typeof(GameObject), true) as GameObject;

                if (lastRoot != null && lastRoot != root)
                {
                    uiWidgets.Clear();
                    uiObjects.Clear();
                }
            }
        }

        /// <summary>
        /// 绘制 查找UI控件
        /// </summary>
        private void DrawFindWidget()
        {
            EditorGUILayout.Space();
            using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = Color.white;
                Rect rect = hScope.rect;
                rect.height = EditorGUIUtility.singleLineHeight;
                GUI.Box(rect, "");

                if (GUILayout.Button("查找UI控件", GUILayout.Width(halfViewWidth / 2f)))
                {
                    RefreshUIList();
                }

                if (GUILayout.Button("清除控件"))
                {
                    deletedGameobjectList.Clear();
                    uiWidgets.Clear();
                }
                if (GUILayout.Button("清除其他"))
                {
                    uiObjects.Clear();
                }
            }
        }


        public void RefreshUIList()
        {
            if (root == null)
            {
                Debug.LogWarning("请先选择一个UI物体!");
                return;
            }

            RecursiveUI(root.transform, (tran) =>
            {
                MaskableGraphic uiGraphic = tran.GetComponent<MaskableGraphic>();
                if (!filterNotRaycastTarget || !(filterNotRaycastTarget && uiGraphic != null && uiGraphic.raycastTarget == true && filterNotRaycastTarget && tran.GetComponent<Image>()))
                {
                    UIBehaviour[] widgets = tran.GetComponents<UIBehaviour>();
                    for (int i = 0; i < widgets.Length; i++)
                    {
                        var widget = widgets[i];
                        Type type = widget.GetType();
                        foreach (string name in allowableWidgets)
                        {
                            if (type.Name == name)
                            {
                                if (widget != null && !uiWidgets.Contains(widget))
                                {
                                    uiWidgets.Add(widget);
                                }
                                break;
                            }
                        }
                    }
                }
            });
        }


        public enum WidgetEventType
        {
            image = 1,
            button = 2,
        }
        [SerializeField]
        public WidgetEventType flagtype;

        private bool toggle1;
        private bool toggle2;
        private void DrawWidgetDetail()
        {
            using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope(GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            //using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.Space();
                //GUI.backgroundColor = Color.gray;
                if (GUILayout.Button("删除自己及子控件", GUILayout.Width(halfViewWidth / 4f)))
                {
                    if (tempDeletedGameobjectList.Count > 0)
                    {
                        foreach(var item in tempDeletedGameobjectList)
                        {
                            if(!deletedGameobjectList.Contains(item))
                            {
                                deletedGameobjectList.Add(item);
                            }
                        }
                        tempDeletedGameobjectList.Clear();
                        uiWidgets.Clear();
                        RefreshUIList();
                        //Debug.Log("选择了控件" + SelectedObject.name);
                        //SelectedObject = null;
                    }
                    else
                    {
                        Debug.Log("请选择一个控件！");
                    }
                }

                if (GUILayout.Button("生成Transform组件", GUILayout.Width(halfViewWidth / 4f)))
                {
                    if (tempDeletedGameobjectList.Count > 0)
                    {
                        foreach (var item in tempDeletedGameobjectList)
                        {
                            if (!uiObjects.Contains(item))
                            {
                                uiObjects.Add(item);
                            }
                        }
                        tempDeletedGameobjectList.Clear();
                        //Debug.Log("选择了控件" + SelectedObject.name);
                        //SelectedObject = null;
                    }
                    else
                    {
                        Debug.Log("请选择一个控件！");
                    }
                }
                    //    Rect rect = hScope.rect;
                    //    rect.height = EditorGUIUtility.singleLineHeight;
                    //    GUI.Box(rect, "xxxxxx");

                    //    GUILayout.Toggle(false, new GUIContent("生成点击事件"));
                    //    GUILayout.SelectionGrid(4, new[] { "1", "11", "111", "1111" }, 4);
                    //    EditorGUILayout.LayerField("LayerField", 1);
                    //    EditorGUILayout.TagField("TagField", "一个tag");


                    //    flagtype = (WidgetEventType)EditorGUILayout.EnumPopup(flagtype, GUILayout.Width(halfViewWidth / 8f));
                    //    Debug.Log("----------" + flagtype);
                    //    using (EditorGUILayout.VerticalScope vScope =
                    //        new EditorGUILayout.VerticalScope(GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                    //    {
                    //        for (int i = 0; i < 11; i++)
                    //        {
                    //            toggle1 = GUILayout.Toggle(toggle1, new GUIContent("aaaaaaaaaaaaaa"));
                    //            toggle2 = GUILayout.Toggle(toggle2, new GUIContent("bbbbbbbbbbbbbbbb"));
                    //            EditorGUILayout.Space();
                    //        }
                    //    }

                    //}

                    //EditorGUILayout.Space();


                    //toggle1 = EditorGUILayout.ToggleLeft("选项1", toggle1,
                    //    GUILayout.Width(halfViewWidth / 8f));
                    //toggle2 = EditorGUILayout.ToggleLeft("选项2", toggle2,
                    //    GUILayout.Width(halfViewWidth / 8f));

                }
        }

        /// <summary>
        /// 绘制 控件列表
        /// </summary>
        private void DrawWidgetList()
        {
            EditorGUILayout.Space();

            ReorderableListGUI.Title("UI控件");
            scrollWidgetPos = EditorGUILayout.BeginScrollView(scrollWidgetPos);
            ReorderableListGUI.ListField<UIBehaviour>(uiWidgets, DrawWidget, ReorderableListFlags.ShowIndices | ReorderableListFlags.ShowSizeField);
            //ReorderableListGUI.ListField<TestEditorUI>(testUIList, DrawWidget1, ReorderableListFlags.ShowIndices | ReorderableListFlags.ShowSizeField);
            EditorGUILayout.EndScrollView();
        }

        private UIBehaviour DrawWidget(Rect position, UIBehaviour item)
        {
            item = (UIBehaviour)EditorGUI.ObjectField(position, item, typeof(UIBehaviour), true);
            return item;
        }

        /// <summary>
        /// 绘制 其他ui gameobject,比如某些节点要控制下层的隐藏显示
        /// </summary>
        private void DrawCustomObjectList()
        {
            EditorGUILayout.Space();

            ReorderableListGUI.Title("其他UI对象");
            scrollObjectPos = EditorGUILayout.BeginScrollView(scrollObjectPos);
            ReorderableListGUI.ListField<GameObject>(uiObjects, DrawCustomObject);
            EditorGUILayout.EndScrollView();
        }

        private GameObject DrawCustomObject(Rect position, GameObject item)
        {
            item = (GameObject)EditorGUI.ObjectField(position, item, typeof(GameObject), true);
            return item;
        }

        private void DrawCodeGenTitle()
        {
            EditorGUILayout.Space();
            using (var hScope = new EditorGUILayout.HorizontalScope(GUILayout.Height(EditorGUIUtility.singleLineHeight)))
            {
                GUI.backgroundColor = Color.white;
                Rect rect = hScope.rect;
                GUI.Box(rect, "");

                EditorGUILayout.LabelField("代码生成:");
            }
        }

        private void DrawCodeGenToolBar()
        {
            EditorGUILayout.Space();

            selectedBar = GUILayout.Toolbar(selectedBar, new string[] { "C#", "Lua" });           

            switch (selectedBar)
            {
                case 0:
                    DrawCsPage();
                    break;
                case 1:
                    DrawLuaPage();
                    break;
                default:
                    break;
            }
        }

        private void DrawCsPage()
        {
            EditorGUILayout.Space();
            isMono = GUILayout.Toggle(isMono, "继承MonoBehaviour");
            EditorGUILayout.Space();
            if (GUILayout.Button("变量声明", GUILayout.Width(halfViewWidth / 3f)))
            {
                BuildStatementCode();
            }

            EditorGUILayout.Space();
            using (EditorGUILayout.VerticalScope vScope = new EditorGUILayout.VerticalScope())
            {
                GUI.backgroundColor = Color.white;
                GUI.Box(vScope.rect, "");

                EditorGUILayout.LabelField("选择需要注册事件回调的控件:");
                DrawEventWidget();

                EditorGUILayout.Space();
                if (GUILayout.Button("注册事件", GUILayout.Width(halfViewWidth / 3f)))
                {
                    BuildEventCode();
                }
            }

            EditorGUILayout.Space();
            using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("查找赋值"))
                {
                    BuildAssignmentCode();
                }

                if (GUILayout.Button("复制代码"))
                {
                    TextEditor p = new TextEditor();
                    codeAllText = new StringBuilder(codeStateText.ToString());
                    codeAllText.Append(codeAssignText);
                    codeAllText.Append(codeEventText);
                    p.text = codeAllText.ToString();
                    p.OnFocus();
                    p.Copy();

                    EditorUtility.DisplayDialog("提示", "代码复制成功", "OK");
                }
                if (GUILayout.Button("生成脚本"))
                {
                    CreateCsUIScript();
                }
            }

            EditorGUILayout.Space();
            using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope())
            {
                if (isMono)
                {
                    if (GUILayout.Button("挂载脚本组件"))
                    {
                        AddScriptComponent();
                    }
                    if (GUILayout.Button("绑定UI(无需查找赋值)"))
                    {
                        BindSerializeWidget();
                    }
                }
            }
            DrawPreviewText();
        }

        private void DrawLuaPage()
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("查找赋值", GUILayout.Width(halfViewWidth / 3f)))
            {
                BuildStatementCode();
                BuildAssignmentCode();
            }

            EditorGUILayout.Space();
            using (EditorGUILayout.VerticalScope vScope = new EditorGUILayout.VerticalScope())
            {
                GUI.backgroundColor = Color.white;
                GUI.Box(vScope.rect, "");

                EditorGUILayout.LabelField("选择需要注册事件回调的控件:");
                DrawEventWidget();

                EditorGUILayout.Space();
                if (GUILayout.Button("注册事件", GUILayout.Width(halfViewWidth / 3f)))
                {
                    BuildEventCode();
                }
            }

            EditorGUILayout.Space();
            using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制代码"))
                {
                    TextEditor p = new TextEditor();
                    codeAllText = new StringBuilder();
                    codeAllText.Append(codeAssignText);
                    codeAllText.Append(codeEventText);
                    p.text = codeAllText.ToString();
                    p.OnFocus();
                    p.Copy();

                    EditorUtility.DisplayDialog("提示", "代码复制成功", "OK");
                }
                if (GUILayout.Button("生成脚本"))
                {
                    CreateLuaUIScript();
                }
            }

            DrawPreviewText();
        }

        /// <summary>
        /// 遍历UI
        /// </summary>
        /// <param name="parent">父节点</param>
        /// <param name="callback">回调</param>
        public void RecursiveUI(Transform parent, UnityAction<Transform> callback)
        {
            if (deletedGameobjectList.Count > 0)
            {
                foreach(var item in deletedGameobjectList)
                {
                    if(item == parent.gameObject)
                    {
                        return;
                    }
                }
            }

            if (callback != null)
                callback(parent);

            if (parent.childCount >= 0)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);

                    RecursiveUI(child, callback);
                }
            }
        }

        private string BuildStatementCode()
        {
            variableNum = 0;
            variableNameDic.Clear();

            codeStateText = null;
            codeStateText = new StringBuilder();

            codeStateText.Append(CodeConfig.statementRegion);
            //非mono类声明一个transform
            if (!isMono)    
            {
                codeStateText.Append(CodeConfig.stateTransform);
            }

            List<GameObject> tempUiObject = new List<GameObject>(uiObjects);
            //控件列表
            for (int i = 0; i < uiWidgets.Count; i++)
            {
                if (uiWidgets[i] == null) continue;
                foreach(var item in tempUiObject)
                {
                    if (item == uiWidgets[i].gameObject || IsChild(item, uiWidgets[i].gameObject))
                    {
                        GenerateTransformName(item);
                        tempUiObject.Remove(item);
                        break;
                    }
                }

                GenerateWidgetName(uiWidgets[i]);
            }
            //其他对象列表，目前都是GameObject
            for (int i = 0; i < tempUiObject.Count; i++)
            {
                if (tempUiObject[i] == null) continue;

                GenerateTransformName(tempUiObject[i]);
            }

            codeStateText.Append(CodeConfig.regionEnd);
            //Debug.Log(codeStateText);
            return codeStateText.ToString();
        }

        private void GenerateTransformName(GameObject go)
        {
            Type type = go.GetType();
            if (type == null)
            {
                Debug.LogError("BuildUICode type error !");
                return;
            }

            string typeName = type.Name;
            string variableName = string.Format("{0}Panel", go.name.Substring(0, 1).ToLower());
            variableName = variableName.Replace(' ', '_');   //命名有空格的情况
                                                             //重名处理
            if (variableNameDic.ContainsKey(variableName))
            {
                ++variableNum;
                variableName += variableNum;
            }
            variableNameDic.Add(variableName, go);

            if (isMono)
            {
                codeStateText.AppendFormat(CodeConfig.serilStateCodeFmt, typeName, variableName);
            }
            else
            {
                codeStateText.AppendFormat(CodeConfig.stateCodeFmt, typeName, variableName);
            }
        }

        private bool IsChild(GameObject parennt,GameObject child)
        {
            for (int i = 0; i < parennt.transform.childCount; i++)
            {
                if (parennt.transform.GetChild(i).gameObject == child)
                {
                    return true;
                }
            }

            return false;
        }


        private void GenerateWidgetName(UIBehaviour uiWidgets)
        {
            Type type = uiWidgets.GetType();
            if (type == null)
            {
                Debug.LogError("BuildUICode type error !");
                return;
            }

            string typeName = type.Name;
            if ("TextMeshProUGUI" == typeName)
            {
                typeName = "Text";
            }

            string suffix = hasTypesuffix ? typeName : "";
            string variableName = string.Format("{2}{1}{0}", suffix, uiWidgets.name.Substring(0, 1).ToLower() + uiWidgets.name.Substring(1), namePrefix);
            variableName = variableName.Replace(' ', '_');   //命名有空格的情况

            if (variableNameDic.ContainsKey(variableName))
            {
                ++variableNum;
                variableName += variableNum;
            }
            variableNameDic.Add(variableName, uiWidgets);

            if (isMono)
            {
                codeStateText.AppendFormat(CodeConfig.serilStateCodeFmt, typeName, variableName);
            }
            else
            {
                codeStateText.AppendFormat(CodeConfig.stateCodeFmt, typeName, variableName);
            }
        }
        
        private void DrawEventWidget()
        {            
            using (EditorGUILayout.HorizontalScope hScope = new EditorGUILayout.HorizontalScope())
            {
                //筛选当前UI的事件控件
                foreach (var elem in Enum.GetValues(typeof(CodeConfig.EventWidgetType)))
                {
                    for (int i = 0; i < uiWidgets.Count; i++)
                    {
                        if (uiWidgets[i] == null) continue;

                        Type type = uiWidgets[i].GetType(); 
                        if (type == null)
                        {
                            Debug.LogError("BuildUICode type error !");
                            continue;
                        }

                        MaskableGraphic uiGraphic = uiWidgets[i].gameObject.transform.GetComponent<MaskableGraphic>();
                        if (uiGraphic != null && uiGraphic.raycastTarget == false && uiWidgets[i].gameObject.transform.GetComponent<Image>())
                        {
                            continue;
                        }

                        if (type.Name == elem.ToString() && !selectedEventWidgets.ContainsKey(type.Name))
                        {
                            selectedEventWidgets.Add(type.Name, true);
                        }
                    }                   
                }                

                //绘制toggle,注意不能遍历dic的同时赋值
                List<string> list = new List<string>(selectedEventWidgets.Keys);
                foreach(string wedagetName in list)
                {
                    selectedEventWidgets[wedagetName] = EditorGUILayout.ToggleLeft(wedagetName, selectedEventWidgets[wedagetName],
                        GUILayout.Width(halfViewWidth / 8f));
                }
            }
        }

        /// <summary>
        /// 构建注册控件事件的代码
        /// </summary>
        /// <returns></returns>
        private string BuildEventCode()
        {
            codeEventText = null;
            codeEventText = new StringBuilder();

            codeEventText.Append(eventRegion);
            //codeEventText.AppendFormat(methodStartFmt, "_BindingEvents");

            bool hasEventWidget = false;    //标识是否有控件注册了事件
            for (int i = 0; i < uiWidgets.Count; i++)
            {
                if (uiWidgets[i] == null) continue;

                //剔除不是事件或者是事件但未勾选toggle的控件
                string typeName = uiWidgets[i].GetType().Name;
                if (!selectedEventWidgets.ContainsKey(typeName) || !selectedEventWidgets[typeName])
                {
                    continue;
                }                
             
                foreach (var vName in variableNameDic.Keys)
                {
                    if (uiWidgets[i].Equals(variableNameDic[vName]))
                    {
                        string variableName = vName;
                        if (!string.IsNullOrEmpty(variableName))
                        {
                            string methodName = variableName.Substring(variableName.IndexOf('_') + 1);
                            if (uiWidgets[i] is Button)
                            {
                                string onClickStr = string.Format(onClickSerilCode, variableName, methodName);
                                if (hasEventWidget)
                                {
                                    string str = codeEventText.ToString();
                                    codeEventText.Insert(str.LastIndexOf(';') + 1, "\n" + onClickStr);
                                }
                                else
                                {
                                    codeEventText.Append(onClickStr);
                                }
                                codeEventText.AppendFormat(btnCallbackSerilCode, methodName);

                                hasEventWidget = true;
                            }
                            else if (uiWidgets[i] is Image)
                            {
                                MaskableGraphic uiGraphic = uiWidgets[i].gameObject.transform.GetComponent<MaskableGraphic>();
                                if (uiGraphic != null && uiGraphic.raycastTarget == false && uiWidgets[i].gameObject.transform.GetComponent<Image>())
                                {
                                    continue;
                                }
                                string onClickStr = string.Format(onImageClickSerilCode, variableName, methodName,"{","}");
                                if (hasEventWidget)
                                {
                                    string str = codeEventText.ToString();
                                    codeEventText.Insert(str.LastIndexOf("})") + 2, "\n" +  onClickStr);
                                }
                                else
                                {
                                    codeEventText.Append(onClickStr);
                                }
                                codeEventText.AppendFormat(btnCallbackSerilCode, methodName);

                                hasEventWidget = true;
                            }
                            else
                            {
                                string addEventStr = string.Format(onValueChangeSerilCode, variableName, methodName);
                                if (hasEventWidget)
                                {
                                    codeEventText.Insert(codeEventText.ToString().LastIndexOf(';') + 1, addEventStr);
                                }
                                else
                                {
                                    codeEventText.Append(addEventStr);
                                }

                                string paramType = "";
                                foreach (string widgetType in CodeConfig.eventCBParamDic.Keys)
                                {
                                    if (typeName == widgetType)
                                    {
                                        paramType = CodeConfig.eventCBParamDic[widgetType];
                                        break;
                                    }
                                }

                                if (!string.IsNullOrEmpty(paramType))
                                {
                                    codeEventText.AppendFormat(eventCallbackSerilCode, methodName, paramType);
                                }

                                hasEventWidget = true;
                            }                           
                        }
                        break;
                    }                    
                }               
            }

            string codeStr = codeEventText.ToString();
            if (hasEventWidget)
            {
                codeEventText.Insert(codeStr.LastIndexOf(';') + 1, methodEnd);
            }
            else
            {
                codeEventText.Append(methodEnd);
            }
            codeEventText.Append(regionEnd);
            return codeEventText.ToString();
        }

        /// <summary>
        /// 当前操作生成的代码预览
        /// </summary>
        private void DrawPreviewText()
        {
            EditorGUILayout.Space();
            
             using (var ver = new EditorGUILayout.VerticalScope())
             {
                GUI.backgroundColor = Color.white;
                GUI.Box(ver.rect, "");

                EditorGUILayout.HelpBox("代码预览:", MessageType.None);
                using (var scr = new EditorGUILayout.ScrollViewScope(scrollTextPos))
                {                    
                    scrollTextPos = scr.scrollPosition;

                    if (codeStateText != null && !string.IsNullOrEmpty(codeStateText.ToString()) && selectedBar == 0)
                    {
                        //GUILayout.TextArea(codeStateText.ToString());
                        GUILayout.Label(codeStateText.ToString());
                    }

                    if (codeAssignText != null && !string.IsNullOrEmpty(codeAssignText.ToString()))
                    {
                        GUILayout.Label(codeAssignText.ToString());
                    }

                    if (codeEventText != null && !string.IsNullOrEmpty(codeEventText.ToString()))
                    {
                        //GUILayout.TextArea(codeEventText.ToString());
                        GUILayout.Label(codeEventText.ToString());
                    }
                }
            }
        }

        /// <summary>
        /// 生成C# UI脚本
        /// </summary>
        private void CreateCsUIScript()
        {
            string path = EditorPrefs.GetString("create_script_folder", "");
            path = EditorUtility.SaveFilePanel("Create Script", path, root.name + ".cs", "cs");
            if (string.IsNullOrEmpty(path)) return;

            int index = path.LastIndexOf('/');
            className = path.Substring(index + 1, path.LastIndexOf('.') - index - 1);

            StringBuilder scriptBuilder = new StringBuilder();
            scriptBuilder.Append(CodeConfig.codeAnnotation);
            scriptBuilder.Append(CodeConfig.usingNamespace);
            if (isMono)
            {
                scriptBuilder.AppendFormat(CodeConfig.classMonoStart, className);
            }
            else
            {
                scriptBuilder.AppendFormat(CodeConfig.classStart, className);
            }
            scriptBuilder.Append(codeStateText);
            scriptBuilder.Append(codeAssignText);
            scriptBuilder.Append(codeEventText);
            scriptBuilder.Append(CodeConfig.classEnd);  

            File.WriteAllText(path, scriptBuilder.ToString(), new UTF8Encoding(false));
            AssetDatabase.Refresh();

            Debug.Log("脚本生成成功,生成路径为:" + path);
            EditorPrefs.SetString("create_script_folder", path);            
        }

        /// <summary>
        /// 生成Lua UI脚本
        /// </summary>
        private void CreateLuaUIScript()
        {
            string path = EditorPrefs.GetString("create_script_folder_lua", "");
            path = EditorUtility.SaveFilePanel("Create lua Script", path, root.name + ".lua", "lua");
            if (string.IsNullOrEmpty(path)) return;

            int index = path.LastIndexOf('/');
            className = path.Substring(index + 1, path.LastIndexOf('.') - index - 1);

            StringBuilder scriptBuilder = new StringBuilder();
            scriptBuilder.Append(CodeConfig.codeAnnotationLua);
            scriptBuilder.Append(CodeConfig.requireCode);
            scriptBuilder.AppendFormat(CodeConfig.classStartLua, className, className);
            scriptBuilder.AppendFormat(CodeConfig.classCtorLua, className);
            scriptBuilder.Append(codeAssignText);
            scriptBuilder.Append(codeEventText);
            scriptBuilder.AppendFormat(CodeConfig.classEndLua, className);

            File.WriteAllText(path, scriptBuilder.ToString().Replace("Class", className), new UTF8Encoding(false));
            AssetDatabase.Refresh();

            Debug.Log("脚本生成成功,生成路径为:" + path);
            EditorPrefs.SetString("create_script_folder_lua", path);
        }

        /// <summary>
        /// 在根物体上挂载生成的脚本(必须继承monobehavior)
        /// </summary>
        private void AddScriptComponent()
        {
            if (EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此操作", "OK");
                return;
            }

            if (root == null || string.IsNullOrEmpty(className))
            {
                EditorUtility.DisplayDialog("警告", "请先按顺序生成UI脚本再执行此操作", "OK");
                return;
            }

            //通过Assembly-CSharp程序集挂载脚本
            Assembly[] AssbyCustmList = System.AppDomain.CurrentDomain.GetAssemblies();
            Assembly asCSharp = null;
            for (int i = 0; i < AssbyCustmList.Length; i++)
            {
                string assbyName = AssbyCustmList[i].GetName().Name;
                if (assbyName == "Assembly-CSharp")
                {
                    asCSharp = AssbyCustmList[i];
                    break;
                }
            }

            scriptType = asCSharp.GetType(className);
            if(scriptType == null)
            {
                EditorUtility.DisplayDialog("警告", "挂载失败，请先检查脚本是否正确生成", "OK");
                return;
            }

            var target = root.GetComponent(scriptType);
            if (target == null)
            {
                target = root.AddComponent(scriptType);
            }
        }
        /// <summary>
        /// 给UI变量绑定实例
        /// </summary>
        private void BindSerializeWidget()
        {
            if (EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("警告", "请等待编辑器完成编译再执行此操作", "OK");
                return;
            }

            if(root == null || string.IsNullOrEmpty(className))
            {
                EditorUtility.DisplayDialog("警告", "请先按顺序生成UI脚本再执行此操作", "OK");
                return;
            }

            if(scriptType == null)
            {
                EditorUtility.DisplayDialog("警告", "请先按顺序挂载UI脚本再执行此操作", "OK");
                return;
            }
            
            object target = null;
            target = root.GetComponent(scriptType);
            if (target == null)
            {
                target = root.AddComponent(scriptType);
            }

            //奇怪，资源刷新以后variableNameDic被清空了，先再获取一遍
            BuildStatementCode();

            foreach (var name in variableNameDic.Keys)
            {
                if (string.IsNullOrEmpty(name)) continue;
                scriptType.InvokeMember(name,
                                BindingFlags.SetField |
                                BindingFlags.Instance |
                                BindingFlags.NonPublic,
                                null, target, new object[] { variableNameDic[name] }, null, null, null);
            }                     
        }
        

        private void BuildAssignmentCode()
        {
            codeAssignText = new StringBuilder();
            codeAssignText.Append(assignRegion);
            codeAssignText.AppendFormat(methodStartFmt, "InitUI");
            if (!isMono && selectedBar == 0)
            {
                codeAssignText.Append(CodeConfig.assignTransform);
            }

            var allPath = GetChildrenPaths(root);

            if (variableNameDic == null)
            {
                return;
            }

            //格式：变量名 = transform.Find("").Getcomponent<>();
            foreach (var name in variableNameDic.Keys)
            {
                var obj = variableNameDic[name];
                if (obj == null) continue;

                string path = "";
                bool isRootComponent = false;
                foreach (var tran in allPath.Keys)
                {
                    if (tran == null) continue;

                    UIBehaviour behav = obj as UIBehaviour;
                    if (behav != null)
                    {
                        //判断是否挂在根上，根上不需要路径
                        isRootComponent = behav.gameObject == root;
                        if (isRootComponent) break;

                        if (behav.gameObject == tran.gameObject)
                        {
                            path = allPath[tran];
                            break;
                        }
                    }
                    else
                    {
                        if (tran.gameObject == obj)
                        {
                            path = allPath[tran];
                            break;
                        }
                    }
                }

                if (obj is GameObject)
                {
                    codeAssignText.AppendFormat(assignGameObjectCodeFmt, name, path);
                }
                else
                {
                    string luaName;
                    if (!widgetNameMap.TryGetValue(obj.GetType().Name, out luaName))
                    {
                        luaName = obj.GetType().Name;

                    }
                    if (isRootComponent)
                    {
                        codeAssignText.AppendFormat(assignRootCodeFmt, name, luaName);
                    }
                    else
                    {            
                        codeAssignText.AppendFormat(assignCodeFmt, name, path, luaName);
                    }
                }
            }

            codeAssignText.Append(methodEnd);
            codeAssignText.Append(regionEnd);
            //Debug.Log(codeAssignText.ToString());
        }


        private Dictionary<Transform, string> GetChildrenPaths(GameObject rootGo)
        {
            Dictionary<Transform, string> pathDic = new Dictionary<Transform, string>();
            string path = string.Empty;
            Transform[] tfArray = rootGo.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < tfArray.Length; i++)
            {
                Transform node = tfArray[i];

                string str = node.name;
                while (node.parent != null && node.gameObject != rootGo && node.parent.gameObject != rootGo)
                {                  
                    str = string.Format("{0}/{1}", node.parent.name, str);
                    node = node.parent;
                }
                path += string.Format("{0}\n", str);

                if (!pathDic.ContainsKey(tfArray[i]))
                {
                    pathDic.Add(tfArray[i], str);
                }
            }
            //Debug.Log(path);

            return pathDic;
        }

    }
}
