#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GrassPainV4))]
public class GrassPainToolV4 : Editor
{
    public bool isPainting = false;
    private GrassPainV4 tool;
    private void OnEnable()
    {
        tool = (GrassPainV4)target;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        GUILayout.Space(10);

        isPainting = GUILayout.Toggle(isPainting, "Paint Grass", "Button", GUILayout.Height(30));

        GUILayout.Space(20);
        GUILayout.Label("Clear All Grass", EditorStyles.boldLabel);
        
        // 清除按钮
        EditorGUI.BeginDisabledGroup(tool.grassList.Count == 0);
        if (GUILayout.Button("CLEAR ALL GRASS", GUILayout.Height(30)))
        {
            // 简单确认对话框
            if (EditorUtility.DisplayDialog("Clear All Grass", 
                "Are you sure you want to remove all grass?", 
                "Clear All", "Cancel"))
            {
                tool.ClearAllGrass();
            }
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.Space(10);
        GUILayout.Label("Baking Tools", EditorStyles.boldLabel);
        
        // 创建新资源按钮
        if (GUILayout.Button("Create New Grass Data Asset"))
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Grass Data", 
                "GrassData.asset", 
                "asset", 
                "Save grass data asset");
                
            if (!string.IsNullOrEmpty(path))
            {
                GrassDataAsset asset = ScriptableObject.CreateInstance<GrassDataAsset>();
                AssetDatabase.CreateAsset(asset, path);
                tool.grassDataAsset = asset;
            }
        }
        
        // 烘焙按钮
        EditorGUI.BeginDisabledGroup(tool.grassDataAsset == null);
        if (GUILayout.Button("Bake Grass Data"))
        {
            tool.BakeToAsset();
        }
        EditorGUI.EndDisabledGroup();
    }

    public void OnSceneGUI(SceneView sceneView)
    {
        if (isPainting == false || tool == null)
        {
            return;
        }

        Event e = Event.current;
        if(e.type == EventType.MouseDrag || e.type == EventType.MouseDown){
            if (e.control)
            {
                HandleErase(e);
                e.Use();
            }
            else
            {
                PaintGrass(e);
                e.Use();
            }
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, tool.paintLayer)){
            if (e.control)
            {
                Handles.color = Color.red;

            }
            else
            {
                Handles.color = Color.green;
            }

            Handles.DrawSolidDisc(hit.point, hit.normal, tool.brushRadius);

            Handles.color = Color.green;
            Handles.DrawWireDisc(hit.point, hit.normal, tool.brushRadius);
        }
    }

    private void PaintGrass(Event e)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, tool.paintLayer))
        {
            int points = Mathf.CeilToInt(tool.Density);
            if(points > 0)
            {
                for (int i = 0; i < points; i++)
                {
                    Vector2 randomCircle = Random.insideUnitCircle * tool.brushRadius;
                    Vector3 randomOffset = new Vector3(
                        randomCircle.x,
                        0,
                        randomCircle.y
                    );

                    Vector3 worldPos = hit.point + randomOffset;

                    if (Physics.Raycast(
                        worldPos + Vector3.up * 10,
                        Vector3.down,
                        out RaycastHit groundHit,
                        20,
                        tool.paintLayer))
                    {
                        tool.AddPosition(groundHit.point);
                    }
                }

            }
        }
        //Debug.Log("PaintGrass");
    }

    private void HandleErase(Event e)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, tool.paintLayer))
        {
            int points = Mathf.CeilToInt(tool.Density);
            if(points > 0)
            {
                for (int i = 0; i < points; i++)
                {
                    Vector2 randomCircle = Random.insideUnitCircle * tool.brushRadius;
                    Vector3 randomOffset = new Vector3(
                        randomCircle.x,
                        0,
                        randomCircle.y
                    );

                    Vector3 worldPos = hit.point + randomOffset;

                    if (Physics.Raycast(
                        worldPos + Vector3.up * 10,
                        Vector3.down,
                        out RaycastHit groundHit,
                        20,
                        tool.paintLayer))
                    {
                        tool.RemoveGrassAtPosition(groundHit.point);
                    }
                }

            }
        }
        
        Debug.Log("HandleErase");
    }
}
#endif