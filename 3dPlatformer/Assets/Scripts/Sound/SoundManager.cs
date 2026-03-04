using UnityEngine;
using UnityEngine.Audio;
using System.Collections.Generic;
using System.Collections;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance;

    [Header("Sound Groups (Scriptable Objects)")]
    public SoundGroupSO[] soundGroups;

    [Header("Mixer")]
    public AudioMixer masterMixer;
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup voiceGroup;

    [Header("Pool Settings")]
    public int poolSize = 20;
    private List<AudioSource> pool;

    private Dictionary<string, SoundItem> lookup = new Dictionary<string, SoundItem>();

    private void Awake()
    {
        // Singleton
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildLookupTable();
        CreatePool();
    }

    // Build dictionary for instant sound lookup by name
    private void BuildLookupTable()
    {
        lookup.Clear();

        foreach (var group in soundGroups)
        {
            foreach (var item in group.soundItems)
            {
                if (!lookup.ContainsKey(item.soundName))
                {
                    lookup.Add(item.soundName, item);
                }
                else
                {
                    Debug.LogWarning("Duplicate sound name found: " + item.soundName);
                }
            }
        }
    }

    // Create audio pool
    private void CreatePool()
    {
        pool = new List<AudioSource>();

        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("AudioSource_" + i);
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            pool.Add(src);
        }
    }

    private AudioSource GetFreeSource()
    {
        foreach (var src in pool)
        {
            if (!src.isPlaying)
            {
                return src;
            }
        }

        // Expand pool if needed (optional)
        var newSrcObj = new GameObject("AudioSource_Extra");
        newSrcObj.transform.SetParent(transform);
        var newSrc = newSrcObj.AddComponent<AudioSource>();
        pool.Add(newSrc);

        return newSrc;
    }

    // -------------------------
    // PLAY SYSTEM
    // -------------------------
    public void Play(string soundName)
    {
        if (IsSoundPlaying(soundName))
            return; // <-- Prevent duplicate playback

        if (!lookup.TryGetValue(soundName, out SoundItem item))
        {
            Debug.LogWarning("Sound not found: " + soundName);
            return;
        }

        AudioSource src = GetFreeSource();
        AudioClip clip = item.GetRandomClip();

        if (clip == null)
            return;

        switch (item.soundType)
        {
            case SoundType.SFX: src.outputAudioMixerGroup = sfxGroup; break;
            case SoundType.Music: src.outputAudioMixerGroup = musicGroup; break;
            case SoundType.Voice: src.outputAudioMixerGroup = voiceGroup; break;
        }

        src.clip = clip;
        src.volume = item.volume;
        src.pitch = item.GetRandomPitch();
        src.loop = item.loop;

        src.Play();
    }

    public void PlayOneShot(string soundName)
    {
        if (!lookup.TryGetValue(soundName, out SoundItem item))
        {
            Debug.LogWarning("Sound not found: " + soundName);
            return;
        }

        AudioSource src = GetFreeSource();
        AudioClip clip = item.GetRandomClip();

        if (clip == null) return;

        switch (item.soundType)
        {
            case SoundType.SFX:
                src.outputAudioMixerGroup = sfxGroup;
                break;
            case SoundType.Music:
                src.outputAudioMixerGroup = musicGroup;
                break;
            case SoundType.Voice:
                src.outputAudioMixerGroup = voiceGroup;
                break;
        }

        src.clip = clip;                // important
        src.volume = item.volume;
        src.pitch = item.GetRandomPitch();
        src.loop = false;

        src.Play();
    }
    public void PlayOneShotLimited(string soundName, int maxInstances = 5)
    {
        if (!lookup.TryGetValue(soundName, out SoundItem item))
        {
            Debug.LogWarning("Sound not found: " + soundName);
            return;
        }

        // 🔒 Limit simultaneous instances
        if (GetActiveCount(item) >= maxInstances)
            return;

        AudioSource src = GetFreeSource();
        AudioClip clip = item.GetRandomClip();

        if (clip == null) return;

        switch (item.soundType)
        {
            case SoundType.SFX:
                src.outputAudioMixerGroup = sfxGroup;
                break;
            case SoundType.Music:
                src.outputAudioMixerGroup = musicGroup;
                break;
            case SoundType.Voice:
                src.outputAudioMixerGroup = voiceGroup;
                break;
        }

        src.clip = clip;
        src.volume = item.volume;
        src.pitch = item.GetRandomPitch();
        src.loop = false;

        src.Play();
    }

    // Helper

    private int GetActiveCount(SoundItem item)
    {
        int count = 0;

        foreach (var src in pool)
        {
            if (!src.isPlaying || src.clip == null)
                continue;

            foreach (var clip in item.clips)
            {
                if (src.clip == clip)
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    // -------------------------
    // STOP / PAUSE / RESUME
    // -------------------------

    public void StopSound(string soundName)
    {
        if (!lookup.TryGetValue(soundName, out SoundItem item))
        {
            Debug.LogWarning("Sound not found: " + soundName);
            return;
        }
        Debug.Log("stopped playing " + soundName);
        foreach (var src in pool)
        {
            // Stop only sources playing one of this SoundItem's clips
            if (src.isPlaying && src.clip != null)
            {
                foreach (var clip in item.clips)
                {
                    if (clip != null && src.clip == clip)
                    {
                        src.Stop();
                    }
                }
            }
        }
    }

    public void StopAll()
    {
        foreach (var src in pool)
        {
            src.Stop();
        }
    }

    public void PauseAll()
    {
        foreach (var src in pool)
        {
            if (src.isPlaying)
                src.Pause();
        }
    }

    public void ResumeAll()
    {
        foreach (var src in pool)
        {
            if (src.clip != null)
                src.UnPause();
        }
    }

    // -------------------------
    // MIXER CONTROLS
    // -------------------------
    public void SetMasterVolume(float value)
    {
        masterMixer.SetFloat("MasterVolume", Mathf.Log10(value) * 20);
    }

    public void SetMusicVolume(float value)
    {
        masterMixer.SetFloat("MusicVolume", Mathf.Log10(value) * 20);
    }

    public void SetSFXVolume(float value)
    {
        masterMixer.SetFloat("SFXVolume", Mathf.Log10(value) * 20);
    }

    public void MuteMusic(bool isMuted)
    {
        masterMixer.SetFloat("MusicVolume", isMuted ? -80f : 0f);
    }

    public void MuteSFX(bool isMuted)
    {
        masterMixer.SetFloat("SFXVolume", isMuted ? -80f : 0f);
    }

    public bool IsSoundPlaying(string soundName)
    {
        if (!lookup.TryGetValue(soundName, out SoundItem item))
            return false;

        foreach (var src in pool)
        {
            if (src.isPlaying && src.clip != null)
            {
                foreach (var clip in item.clips)
                {
                    if (src.clip == clip)
                        return true;
                }
            }
        }

        return false;
    }

    // -------------------------
    // FADE IN PLAY
    // -------------------------
    public void FadeInPlay(string soundName, float duration = 1f)
    {
        if (!lookup.TryGetValue(soundName, out SoundItem item))
        {
            Debug.LogWarning("Sound not found: " + soundName);
            return;
        }

        AudioSource src = GetFreeSource();
        AudioClip clip = item.GetRandomClip();

        if (clip == null) return;

        switch (item.soundType)
        {
            case SoundType.SFX: src.outputAudioMixerGroup = sfxGroup; break;
            case SoundType.Music: src.outputAudioMixerGroup = musicGroup; break;
            case SoundType.Voice: src.outputAudioMixerGroup = voiceGroup; break;
        }

        src.clip = clip;
        src.pitch = item.GetRandomPitch();
        src.loop = item.loop;

        src.volume = 0f;
        src.Play();

        StartCoroutine(FadeInCoroutine(src, item.volume, duration));
    }

    private IEnumerator FadeInCoroutine(AudioSource src, float targetVolume, float duration)
    {
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            src.volume = Mathf.Lerp(0f, targetVolume, time / duration);
            yield return null;
        }

        src.volume = targetVolume;
    }


    // -------------------------
    // FADE OUT STOP
    // -------------------------
    public void FadeOutStop(string soundName, float duration = 1f)
    {
        if (!lookup.TryGetValue(soundName, out SoundItem item))
        {
            Debug.LogWarning("Sound not found: " + soundName);
            return;
        }

        foreach (var src in pool)
        {
            if (src.isPlaying && src.clip != null)
            {
                foreach (var clip in item.clips)
                {
                    if (src.clip == clip)
                    {
                        StartCoroutine(FadeOutCoroutine(src, duration));
                    }
                }
            }
        }
    }

    private IEnumerator FadeOutCoroutine(AudioSource src, float duration)
    {
        float startVolume = src.volume;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            src.volume = Mathf.Lerp(startVolume, 0f, time / duration);
            yield return null;
        }

        src.Stop();
        src.volume = startVolume; // reset for reuse
    }

}
