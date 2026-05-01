using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central lookup for prompt definitions by stable PromptId.
/// </summary>
[CreateAssetMenu(menuName = "ROC/Prompts/Prompt Catalog")]
public class PromptCatalog : ScriptableObject
{
    [SerializeField] private PromptDefinition[] prompts;

    private readonly Dictionary<string, PromptDefinition> _lookup = new();

    public IReadOnlyList<PromptDefinition> Prompts => prompts;

    private void OnEnable()
    {
        RebuildLookup();
    }

    private void OnValidate()
    {
        RebuildLookup();
    }

    public bool TryGetDefinition(string promptId, out PromptDefinition definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(promptId))
        {
            return false;
        }

        if (_lookup.Count == 0)
        {
            RebuildLookup();
        }

        return _lookup.TryGetValue(NormalizeId(promptId), out definition) && definition != null;
    }

    private void RebuildLookup()
    {
        _lookup.Clear();

        if (prompts == null)
        {
            return;
        }

        for (int i = 0; i < prompts.Length; i++)
        {
            PromptDefinition prompt = prompts[i];

            if (prompt == null)
            {
                continue;
            }

            string promptId = NormalizeId(prompt.PromptId);

            if (string.IsNullOrWhiteSpace(promptId))
            {
                continue;
            }

            if (_lookup.ContainsKey(promptId))
            {
                Debug.LogWarning($"[PromptCatalog] Duplicate prompt id '{promptId}' in catalog '{name}'.", this);
                continue;
            }

            _lookup.Add(promptId, prompt);
        }
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
