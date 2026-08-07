using UnityEngine;
using Mirror;
using Epic.OnlineServices.Lobby;
using System.Collections.Generic;

public class LobbyController : EOSLobby
{
    private NetworkManager netManager;

    public override void Start()
    {
        base.Start();
        netManager = GetComponent<NetworkManager>();
    }
    private void OnEnable()
    {
        CreateLobbySucceeded += OnCreateLobbySuccess;
        JoinLobbySucceeded += OnJoinLobbySuccess;
        FindLobbiesSucceeded += OnFindLobbiesSuccess;
    }
    private void OnDisable()
    {
        CreateLobbySucceeded -= OnCreateLobbySuccess;
        JoinLobbySucceeded -= OnJoinLobbySuccess;
        FindLobbiesSucceeded -= OnFindLobbiesSuccess;
    }
    public void Button_CreateRoom()
    {
        Debug.Log("กำลังส่งคำสั่งสร้างห้อง...");
        CreateLobby(4, LobbyPermissionLevel.Publicadvertised, false);
    }
    public void Button_FindRooms()
    {
        Debug.Log("กำลังค้นหาห้อง...");
        FindLobbies();
    }
    private void OnCreateLobbySuccess(List<Attribute> attributes)
    {
        Debug.Log("เปิดห้องสำเร็จ! สั่ง Mirror เริ่มโฮสต์เกม");
        netManager.StartHost();
    }
    private void OnJoinLobbySuccess(List<Attribute> attributes)
    {
        Debug.Log("เข้าห้องสำเร็จ! กำลังดึง PUID เพื่อเชื่อมต่อ...");
        netManager.networkAddress = attributes.Find((x) => x.Data.Key == hostAddressKey).Data.Value.AsUtf8;
        netManager.StartClient();
    }
    private void OnFindLobbiesSuccess(List<LobbyDetails> lobbiesFound)
    {
        Debug.Log($"เจอห้องทั้งหมด {lobbiesFound.Count} ห้อง");
    }
}