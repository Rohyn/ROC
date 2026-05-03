using UnityEngine;

/// <summary>
/// Optional scene-name override used by InteractableRegistry.
/// 
/// This is useful for dynamically spawned NetworkObjects that visually/physically
/// belong to a streamed area, but should not be moved into that manually-loaded
/// Unity scene because doing so can interfere with late-join/dynamic spawn
/// presentation.
/// </summary>
[DisallowMultipleComponent]
public sealed class InteractableRegistrySceneOverride : MonoBehaviour
{
    [SerializeField] private string sceneNameOverride;

    public string SceneNameOverride => sceneNameOverride;

    public void SetSceneNameOverride(string sceneName)
    {
        sceneNameOverride = sceneName ?? string.Empty;
    }
}
