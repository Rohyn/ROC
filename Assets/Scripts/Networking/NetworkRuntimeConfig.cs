using UnityEngine;

/// <summary>
/// Simple runtime networking settings that both server and client builds can read.
/// 
/// Why use a ScriptableObject here?
/// - It keeps "tweakable" network values out of code.
/// - You can create multiple config assets later (local dev, LAN test, production, etc.).
/// - It is easy to assign in the Inspector.
/// </summary>
[CreateAssetMenu(
    fileName = "NetworkRuntimeConfig",
    menuName = "Ruins of Crestil/Networking/Network Runtime Config")]
public class NetworkRuntimeConfig : ScriptableObject
{
    [Header("Local Testing")]
    [Tooltip("Client builds should connect to this address. For local testing on the same machine, use 127.0.0.1.")]
    public string serverAddress = "127.0.0.1";

    [Tooltip("The UDP port used by Unity Transport.")]
    public ushort port = 7777;

    [Tooltip("The address the dedicated server listens on. 0.0.0.0 means all local interfaces.")]
    public string listenAddress = "0.0.0.0";

    [Header("Simple Limits")]
    [Tooltip("Basic connection cap for early testing.")]
    [Min(1)]
    public int maxPlayers = 16;
}