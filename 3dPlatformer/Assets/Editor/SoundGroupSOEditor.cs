using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SoundGroupSO))]
public class SoundGroupSOEditor : Editor
{
    private AudioSource tempSource;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SoundGroupSO soundGroup = (SoundGroupSO)target;

        if (soundGroup.soundItems == null || soundGroup.soundItems.Length == 0)
            return;

        GUILayout.Space(10);
        GUILayout.Label("Play Sounds in Editor", EditorStyles.boldLabel);

        foreach (var item in soundGroup.soundItems)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(item.soundName);

            if (GUILayout.Button("Play"))
            {
                PlayClip(item.GetRandomClip());
            }

            if (GUILayout.Button("Stop"))
            {
                StopClip();
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null) return;

        // Create temporary AudioSource if needed
        if (tempSource == null)
        {
            GameObject tempGO = GameObject.Find("EditorAudioTempSource");
            if (tempGO == null)
            {
                tempGO = new GameObject("EditorAudioTempSource");
                tempGO.hideFlags = HideFlags.HideAndDontSave;
                tempSource = tempGO.AddComponent<AudioSource>();
            }
            else
            {
                tempSource = tempGO.GetComponent<AudioSource>();
                if (tempSource == null)
                    tempSource = tempGO.AddComponent<AudioSource>();
            }
        }

        tempSource.clip = clip;
        tempSource.loop = false;
        tempSource.Play();
    }

    private void StopClip()
    {
        if (tempSource != null)
            tempSource.Stop();
    }
}
