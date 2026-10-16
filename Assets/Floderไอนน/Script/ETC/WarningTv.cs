using Mirror;
using UnityEngine;
using UnityEngine.Video;

// บังคับให้ Game Object นี้ต้องมี AudioSource และ VideoPlayer
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(VideoPlayer))]
public class WarningTV : InteractableBase
{
    private AudioSource audioSource;
    private VideoPlayer videoPlayer;

    protected override void Awake()
    {
        // เรียก Awake ของคลาสแม่ (InteractableBase) เพื่อให้ระบบ Highlight ทำงานได้ปกติ
        base.Awake();

        // ดึง Component ที่อยู่บน GameObject เดียวกันมาเก็บไว้ในตัวแปร
        audioSource = GetComponent<AudioSource>();
        videoPlayer = GetComponent<VideoPlayer>();

        // ปิดการเล่นอัตโนมัติตอนเริ่มเกม
        audioSource.playOnAwake = false;
        videoPlayer.playOnAwake = false;
    }

    /// <summary>
    /// ฟังก์ชันนี้ทำงานบน Server เมื่อมีผู้เล่นมากด Interact
    /// เราจะสั่งให้ Client ทุกคนเล่นภาพและเสียง
    /// </summary>
    protected override void OnInteracted(GameObject interactor)
    {
        // สั่ง Rpc ไปยัง Client ทุกเครื่องให้เริ่มเล่นสื่อ
        RpcPlayMedia();
    }

    /// <summary>
    /// ฟังก์ชันนี้จะถูกเรียกจาก Server ให้ไปทำงานบนเครื่อง Client ทุกคน
    /// เพื่อให้ภาพและเสียงเล่นพร้อมกันทุกเครื่อง
    /// </summary>
    [ClientRpc]
    private void RpcPlayMedia()
    {
        // ตรวจสอบว่ามีคลิปวิดีโอใส่ไว้หรือไม่ แล้วกดเล่น
        if (videoPlayer.clip != null || !string.IsNullOrEmpty(videoPlayer.url))
        {
            videoPlayer.Play();
        }
        else
        {
            Debug.LogWarning($"[{name}] ยังไม่ได้ใส่ Video Clip ใน VideoPlayer!");
        }

        // ตรวจสอบว่ามีคลิปเสียงใส่ไว้หรือไม่ แล้วกดเล่น
        if (audioSource.clip != null)
        {
            audioSource.Play();
        }
        else
        {
            Debug.LogWarning($"[{name}] ยังไม่ได้ใส่ Audio Clip ใน AudioSource!");
        }
    }
}