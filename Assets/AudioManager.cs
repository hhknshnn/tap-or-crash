using UnityEngine;
using UnityEngine.UI; // YENİ — Image componenti için gerekli
using System.Collections.Generic;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;

    [Header("Ses Efektleri")]
    public AudioClip launchClip;
    public AudioClip landClip;
    public AudioClip crashClip;
    public AudioClip asteroidHitClip;

    [Header("Arka Plan Müziği")]
    public AudioClip musicClip;

    [Header("Ses Seviyeleri")]
    [Range(0f, 1f)] public float sfxVolume = 0.8f;
    [Range(0f, 1f)] public float musicVolume = 0.4f;

    // YENİ — Ses butonu için gerekli alanlar
    [Header("Ses Butonu")]
    public Sprite soundOnSprite;        // Ses açık ikonu
    public Sprite soundOffSprite;       // Ses kapalı ikonu
    public List<Image> soundButtonImages; // YENİ — birden fazla butonu destekler

    private AudioSource sfxSource;
    private AudioSource landSource;
    private AudioSource launchSource;
    private AudioSource musicSource;

    // YENİ — Sesin açık mı kapalı mı olduğunu tutar
    private bool isMuted;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.volume = sfxVolume;

        landSource = gameObject.AddComponent<AudioSource>();
        landSource.playOnAwake = false;
        landSource.volume = sfxVolume;

        launchSource = gameObject.AddComponent<AudioSource>();
        launchSource.playOnAwake = false;
        launchSource.volume = sfxVolume;

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.volume = musicVolume;
        musicSource.clip = musicClip;

        // YENİ — Kaydedilen ses tercihini yükle (0 = açık, 1 = kapalı)
        isMuted = PlayerPrefs.GetInt("Muted", 0) == 1;
        AudioListener.pause = isMuted; // Tüm sesleri buna göre ayarla
    }

    void Start()
    {
        PlayMusic();
        UpdateIcon(); // YENİ — Başlangıçta ikonu doğru göster
    }

    // YENİ — Ses butonuna bağlanacak fonksiyon
    public void ToggleSound()
    {
        isMuted = !isMuted;                          // Durumu tersine çevir
        AudioListener.pause = isMuted;               // Tüm sesleri aç/kapat
        PlayerPrefs.SetInt("Muted", isMuted ? 1 : 0); // Tercihi kaydet
        UpdateIcon();                                // İkonu güncelle
    }

    // YENİ — Butondaki ikonu mevcut duruma göre günceller
    void UpdateIcon()
    {
        if (soundButtonImages == null) return;
        foreach (Image img in soundButtonImages)
        {
            if (img != null)
                img.sprite = isMuted ? soundOffSprite : soundOnSprite;
        }
    }

    public void PlayLaunch()
    {
        if (launchClip == null) return;
        launchSource.clip = launchClip;
        launchSource.volume = sfxVolume;
        launchSource.loop = true;
        launchSource.Play();
    }

    public void StopLaunch()
    {
        launchSource.loop = false;
        launchSource.Stop();
    }

    public void PlayLand(float pitch = 1f)
    {
        if (landClip == null) return;
        landSource.pitch = Mathf.Clamp(pitch, 0.8f, 1.25f);
        landSource.PlayOneShot(landClip, sfxVolume);
    }

    public void PlayCrash()
    {
        if (crashClip == null) return;
        sfxSource.PlayOneShot(crashClip, 1f); // sfxVolume yerine 1f — maksimum ses
    }

    public void PlayAsteroidHit()
    {
        if (asteroidHitClip == null) return;
        sfxSource.PlayOneShot(asteroidHitClip, sfxVolume);
    }

    public void PlayMusic()
    {
        if (musicClip == null) return;
        if (musicSource.isPlaying) return;
        musicSource.Play();
    }
}
