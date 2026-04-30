using UnityEngine;

/// <summary>
/// Hides or shows an authored scene object based on the local player's personal progress flags.
///
/// Use this for personal/persistent world presentation:
/// - picked-up personal key
/// - cleared personal quest debris
/// - read personal note
/// - one-player-only tutorial objects
///
/// This is presentation-only. Server-side interaction gating should still be handled
/// by InteractableAvailabilityRules using the same flags.
/// </summary>
[DisallowMultipleComponent]
public class PersonalProgressPresentationState : MonoBehaviour
{
    [Header("Hide When Any Flag Is Present")]
    [SerializeField] private string[] hideWhenAnyProgressFlagPresent;

    [Header("Hide When All Flags Are Present")]
    [SerializeField] private string[] hideWhenAllProgressFlagsPresent;

    [Header("Presentation")]
    [SerializeField] private bool disableRenderers = true;
    [SerializeField] private bool disableColliders = true;
    [SerializeField] private bool includeChildren = true;

    [Header("Binding")]
    [SerializeField] private float searchIntervalSeconds = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private PlayerProgressState _localProgressState;
    private Renderer[] _renderers;
    private Collider[] _colliders;

    private float _nextSearchTime;
    private bool _lastVisibleState = true;

    private void Awake()
    {
        CachePresentationComponents();
    }

    private void OnEnable()
    {
        TryBindLocalProgressState(force: true);
        RefreshPresentation();
    }

    private void OnDisable()
    {
        UnbindLocalProgressState();
    }

    private void Update()
    {
        if (Time.time >= _nextSearchTime)
        {
            TryBindLocalProgressState(force: false);
            _nextSearchTime = Time.time + Mathf.Max(0.1f, searchIntervalSeconds);
        }

        RefreshPresentation();
    }

    private void CachePresentationComponents()
    {
        if (includeChildren)
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }
        else
        {
            _renderers = GetComponents<Renderer>();
            _colliders = GetComponents<Collider>();
        }
    }

    private void TryBindLocalProgressState(bool force)
    {
        if (!force && _localProgressState != null)
        {
            return;
        }

        if (_localProgressState != null)
        {
            return;
        }

        PlayerProgressState[] progressStates =
            FindObjectsByType<PlayerProgressState>(FindObjectsSortMode.None);

        for (int i = 0; i < progressStates.Length; i++)
        {
            PlayerProgressState candidate = progressStates[i];

            if (candidate == null || !candidate.IsOwner)
            {
                continue;
            }

            _localProgressState = candidate;
            _localProgressState.FlagsChanged += HandleProgressFlagsChanged;

            if (verboseLogging)
            {
                Debug.Log(
                    $"[PersonalProgressPresentationState] Bound local PlayerProgressState for '{name}'.",
                    this);
            }

            RefreshPresentation();
            return;
        }
    }

    private void UnbindLocalProgressState()
    {
        if (_localProgressState != null)
        {
            _localProgressState.FlagsChanged -= HandleProgressFlagsChanged;
            _localProgressState = null;
        }
    }

    private void HandleProgressFlagsChanged()
    {
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        bool shouldHide = ShouldHideForLocalPlayer();
        bool shouldBeVisible = !shouldHide;

        if (_lastVisibleState == shouldBeVisible)
        {
            return;
        }

        _lastVisibleState = shouldBeVisible;
        ApplyPresentationState(shouldBeVisible);

        if (verboseLogging)
        {
            Debug.Log(
                $"[PersonalProgressPresentationState] '{name}' visible={shouldBeVisible}.",
                this);
        }
    }

    private bool ShouldHideForLocalPlayer()
    {
        if (_localProgressState == null)
        {
            return false;
        }

        if (hideWhenAnyProgressFlagPresent != null &&
            hideWhenAnyProgressFlagPresent.Length > 0 &&
            _localProgressState.HasAnyFlag(hideWhenAnyProgressFlagPresent))
        {
            return true;
        }

        if (hideWhenAllProgressFlagsPresent != null &&
            hideWhenAllProgressFlagsPresent.Length > 0 &&
            _localProgressState.HasAllFlags(hideWhenAllProgressFlagsPresent))
        {
            return true;
        }

        return false;
    }

    private void ApplyPresentationState(bool visible)
    {
        if (_renderers == null || _colliders == null)
        {
            CachePresentationComponents();
        }

        if (disableRenderers && _renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer rendererComponent = _renderers[i];

                if (rendererComponent != null)
                {
                    rendererComponent.enabled = visible;
                }
            }
        }

        if (disableColliders && _colliders != null)
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                Collider colliderComponent = _colliders[i];

                if (colliderComponent != null)
                {
                    colliderComponent.enabled = visible;
                }
            }
        }
    }
}