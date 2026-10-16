using Mirror;

/// <summary>Legacy scripted power API. Lighting is operated by scene switches, with no keyboard shortcut.</summary>
public class LightController : NetworkBehaviour
{
    public static void ServerSetLights(bool on)
    {
        if (BuildingLightManager.Instance != null) BuildingLightManager.Instance.SetLights(on);
    }

    public static void ServerSetZoneLights(string zoneId, bool on)
    {
        if (BuildingLightManager.Instance != null) BuildingLightManager.Instance.SetZoneLights(zoneId, on);
    }
}
