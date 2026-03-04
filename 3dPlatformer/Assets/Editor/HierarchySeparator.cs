using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class HierarchySeparator : MonoBehaviour
{
#if UNITY_EDITOR
    [MenuItem("GameObject/Create Separator", false, 0)]
    private static void CreateSeparator()
    {
        GameObject separator = new GameObject("―――――― Separator ――――――");
        separator.tag = "EditorOnly";
        separator.hideFlags = HideFlags.None;

        if (Selection.activeTransform != null)
        {
            separator.transform.SetParent(Selection.activeTransform.parent);
            separator.transform.SetSiblingIndex(
                Selection.activeTransform.GetSiblingIndex() + 1
            );
        }

        Undo.RegisterCreatedObjectUndo(separator, "Create Separator");
        Selection.activeGameObject = separator;
    }
#endif
}
