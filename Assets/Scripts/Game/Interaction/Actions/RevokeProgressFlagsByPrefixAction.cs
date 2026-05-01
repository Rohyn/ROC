using UnityEngine;

/// <summary>
/// Server-side interaction action that revokes all player progress flags
/// matching a prefix, but only if optional progress-flag conditions pass.
///
/// Examples:
/// - Revoke intro.* flags after the player has a durable "left intro" marker.
/// - Revoke ritual.first_flame.* flags once the ritual is complete.
/// - Revoke cauldron.first_potion.* flags after the finished potion is claimed.
/// </summary>
public class RevokeProgressFlagsByPrefixAction : InteractableAction
{
    [Header("Target")]
    [Tooltip("All player progress flags beginning with this prefix will be revoked. Example: intro.")]
    [SerializeField] private string prefix = "intro.";

    [Header("Required Progress Flags")]
    [Tooltip("If populated, the player must have all of these flags before this action runs.")]
    [SerializeField] private string[] requiredProgressFlags;

    [Header("Blocked Progress Flags")]
    [Tooltip("If the player has any of these flags, this action will not run.")]
    [SerializeField] private string[] blockedProgressFlags;

    [Header("Flow Control")]
    [Tooltip("If true, later actions on the same interactable will not run after this action executes.")]
    [SerializeField] private bool stopFurtherActionsAfterRevoke = false;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;

    public override bool CanExecute(InteractionContext context)
    {
        if (context == null)
        {
            LogBlocked("context was null.");
            return false;
        }

        PlayerProgressState progressState = ResolveProgressState(context);

        if (progressState == null)
        {
            LogBlocked("interactor has no PlayerProgressState.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            LogBlocked("prefix is empty.");
            return false;
        }

        if (!HasAllRequiredFlags(progressState))
        {
            LogBlocked("required progress flag check failed.");
            return false;
        }

        if (HasAnyBlockedFlag(progressState))
        {
            LogBlocked("blocked progress flag check failed.");
            return false;
        }

        return true;
    }

    public override void Execute(InteractionContext context)
    {
        if (context == null)
        {
            return;
        }

        PlayerProgressState progressState = ResolveProgressState(context);

        if (progressState == null)
        {
            return;
        }

        string normalizedPrefix = prefix.Trim();
        int revokedCount = progressState.RevokeFlagsByPrefix(normalizedPrefix);

        if (verboseLogging)
        {
            Debug.Log(
                $"[RevokeProgressFlagsByPrefixAction] Revoked {revokedCount} flag(s) with prefix '{normalizedPrefix}'.",
                this);
        }

        if (stopFurtherActionsAfterRevoke)
        {
            context.StopFurtherActions = true;
        }
    }

    private static PlayerProgressState ResolveProgressState(InteractionContext context)
    {
        if (context == null)
        {
            return null;
        }

        if (context.InteractorProgressState != null)
        {
            return context.InteractorProgressState;
        }

        if (context.InteractorObject != null)
        {
            return context.InteractorObject.GetComponent<PlayerProgressState>();
        }

        return null;
    }

    private bool HasAllRequiredFlags(PlayerProgressState progressState)
    {
        if (progressState == null)
        {
            return false;
        }

        if (requiredProgressFlags == null || requiredProgressFlags.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < requiredProgressFlags.Length; i++)
        {
            string flagId = NormalizeFlag(requiredProgressFlags[i]);

            if (string.IsNullOrWhiteSpace(flagId))
            {
                continue;
            }

            if (!progressState.HasFlag(flagId))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasAnyBlockedFlag(PlayerProgressState progressState)
    {
        if (progressState == null)
        {
            return false;
        }

        if (blockedProgressFlags == null || blockedProgressFlags.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < blockedProgressFlags.Length; i++)
        {
            string flagId = NormalizeFlag(blockedProgressFlags[i]);

            if (string.IsNullOrWhiteSpace(flagId))
            {
                continue;
            }

            if (progressState.HasFlag(flagId))
            {
                return true;
            }
        }

        return false;
    }

    private void LogBlocked(string reason)
    {
        if (!verboseLogging)
        {
            return;
        }

        Debug.Log(
            $"[RevokeProgressFlagsByPrefixAction] Cannot execute: {reason}",
            this);
    }

    private static string NormalizeFlag(string flagId)
    {
        return string.IsNullOrWhiteSpace(flagId) ? string.Empty : flagId.Trim();
    }
}