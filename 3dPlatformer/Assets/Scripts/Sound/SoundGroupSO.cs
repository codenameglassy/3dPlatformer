using UnityEngine;

public enum SoundType { SFX, Music, Voice }

[CreateAssetMenu(fileName = "SoundGroup", menuName = "Audio/Sound Group")]
public class SoundGroupSO : ScriptableObject
{
    public SoundItem[] soundItems;
}

[System.Serializable]
public class SoundItem
{
    public string soundName;

    [Header("Clips (one or many)")]
    public AudioClip[] clips;

    [Header("Sound Type")]
    public SoundType soundType = SoundType.SFX;

    [Header("Volume")]
    public float volume = 1f;

    [Header("Pitch Range")]
    public float minPitch = 1f;
    public float maxPitch = 1f;

    public bool loop = false;

    // Random pitch
    public float GetRandomPitch()
    {
        return Random.Range(minPitch, maxPitch);
    }

    // Random clip
    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Length == 0) return null;
        if (clips.Length == 1) return clips[0];
        return clips[Random.Range(0, clips.Length)];
    }
}
