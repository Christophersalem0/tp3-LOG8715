using Unity.Netcode;
using UnityEngine;

public class NetworkUtility
{
    // Fetches universally synchronized server ticks (fixes desyncs)
    public static int GetServerTick()
    {
        return NetworkManager.Singleton.ServerTime.Tick;
    }

    public static int GetLocalTick()
    {
        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
    }

    public static uint GetLocalTickRate()
    {
        return NetworkManager.Singleton.NetworkTickSystem.TickRate;
    }

    public static ulong GetCurrentRtt(ulong clientId)
    {
        return NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId);
    }
}