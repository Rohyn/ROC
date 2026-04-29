using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runtime lookup table for authored/static interactables.
///
/// Purpose:
/// - Allows clients to request interaction by stable authored ID.
/// - Allows the server to resolve that ID inside the player's current logical area.
/// - Avoids requiring static scene interactables/NPCs to be spawned NetworkObjects.
/// </summary>
public static class InteractableRegistry
{
    private readonly struct RegistryKey : IEquatable<RegistryKey>
    {
        public readonly string SceneName;
        public readonly string InteractableId;

        public RegistryKey(string sceneName, string interactableId)
        {
            SceneName = sceneName ?? string.Empty;
            InteractableId = interactableId ?? string.Empty;
        }

        public bool Equals(RegistryKey other)
        {
            return string.Equals(SceneName, other.SceneName, StringComparison.Ordinal) &&
                   string.Equals(InteractableId, other.InteractableId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is RegistryKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(SceneName);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(InteractableId);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{SceneName}:{InteractableId}";
        }
    }

    private static readonly Dictionary<RegistryKey, List<GenericInteractable>> _interactablesByKey = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _interactablesByKey.Clear();
    }

    public static void Register(GenericInteractable interactable)
    {
        if (interactable == null)
        {
            return;
        }

        string sceneName = GetSceneName(interactable);
        string interactableId = interactable.InteractableId;

        if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(interactableId))
        {
            return;
        }

        RegistryKey key = new RegistryKey(sceneName, interactableId);

        if (!_interactablesByKey.TryGetValue(key, out List<GenericInteractable> list))
        {
            list = new List<GenericInteractable>();
            _interactablesByKey.Add(key, list);
        }

        CleanupList(list);

        if (!list.Contains(interactable))
        {
            list.Add(interactable);
        }

        if (list.Count > 1)
        {
            Debug.LogWarning(
                $"[InteractableRegistry] Duplicate interactable id '{interactableId}' in scene '{sceneName}'. " +
                "Server interaction lookup will use the first active enabled match.");
        }
    }

    public static void Unregister(GenericInteractable interactable)
    {
        if (interactable == null)
        {
            return;
        }

        string sceneName = GetSceneName(interactable);
        string interactableId = interactable.InteractableId;

        if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(interactableId))
        {
            return;
        }

        RegistryKey key = new RegistryKey(sceneName, interactableId);

        if (!_interactablesByKey.TryGetValue(key, out List<GenericInteractable> list))
        {
            return;
        }

        list.Remove(interactable);
        CleanupList(list);

        if (list.Count == 0)
        {
            _interactablesByKey.Remove(key);
        }
    }

    public static bool TryGet(
        string sceneName,
        string interactableId,
        out GenericInteractable interactable)
    {
        interactable = null;

        if (string.IsNullOrWhiteSpace(sceneName) || string.IsNullOrWhiteSpace(interactableId))
        {
            return false;
        }

        RegistryKey key = new RegistryKey(sceneName, interactableId);

        if (!_interactablesByKey.TryGetValue(key, out List<GenericInteractable> list))
        {
            return false;
        }

        CleanupList(list);

        for (int i = 0; i < list.Count; i++)
        {
            GenericInteractable candidate = list[i];

            if (candidate == null)
            {
                continue;
            }

            if (!candidate.isActiveAndEnabled)
            {
                continue;
            }

            interactable = candidate;
            return true;
        }

        return false;
    }

    private static void CleanupList(List<GenericInteractable> list)
    {
        if (list == null)
        {
            return;
        }

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null)
            {
                list.RemoveAt(i);
            }
        }
    }

    private static string GetSceneName(GenericInteractable interactable)
    {
        if (interactable == null)
        {
            return string.Empty;
        }

        Scene scene = interactable.gameObject.scene;

        if (scene.IsValid() && !string.IsNullOrWhiteSpace(scene.name))
        {
            return scene.name;
        }

        return SceneManager.GetActiveScene().name;
    }
}