using System.Collections.Generic;
using UnityEngine;

namespace ROC.Statuses
{
    /// <summary>
    /// Shared lookup asset that maps status IDs to StatusDefinition assets.
    ///
    /// WHY THIS EXISTS:
    /// - The server should replicate lightweight status identifiers, not ScriptableObject references.
    /// - Clients need a way to resolve those identifiers back into local authored definitions.
    ///
    /// Create one catalog asset and add all currently used statuses to it.
    /// Assign that catalog to StatusManager.
    /// </summary>
    [CreateAssetMenu(
        fileName = "StatusCatalog",
        menuName = "ROC/Statuses/Status Catalog")]
    public class StatusCatalog : ScriptableObject
    {
        [SerializeField] private StatusDefinition[] definitions;

        private Dictionary<string, StatusDefinition> _byId;

        public IReadOnlyList<StatusDefinition> Definitions => definitions;

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        public bool TryGetDefinition(string statusId, out StatusDefinition definition)
        {
            if (_byId == null)
            {
                RebuildLookup();
            }

            if (string.IsNullOrWhiteSpace(statusId))
            {
                definition = null;
                return false;
            }

            return _byId.TryGetValue(statusId, out definition);
        }

        public StatusDefinition GetDefinition(string statusId)
        {
            TryGetDefinition(statusId, out StatusDefinition definition);
            return definition;
        }

        private void RebuildLookup()
        {
            _byId = new Dictionary<string, StatusDefinition>();

            if (definitions == null)
            {
                return;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                StatusDefinition definition = definitions[i];
                if (definition == null)
                {
                    continue;
                }

                string statusId = definition.StatusId;
                if (string.IsNullOrWhiteSpace(statusId))
                {
                    Debug.LogWarning($"[StatusCatalog] Definition '{definition.name}' has an empty StatusId.", this);
                    continue;
                }

                if (_byId.ContainsKey(statusId))
                {
                    Debug.LogWarning($"[StatusCatalog] Duplicate StatusId '{statusId}' found. Keeping the first entry.", this);
                    continue;
                }

                _byId.Add(statusId, definition);
            }
        }
    }
}