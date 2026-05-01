using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Owner-local notification queue for progress, inventory, and system notices.
/// 
/// This is presentation-only. Bridge components observe gameplay state and enqueue
/// small messages here.
/// </summary>
[DisallowMultipleComponent]
public class PlayerNotificationController : MonoBehaviour
{
    [Header("Views")]
    [SerializeField] private NotificationToastView progressToastView;
    [SerializeField] private NotificationToastView inventoryToastView;
    [SerializeField] private NotificationToastView generalToastView;

    [Header("Defaults")]
    [SerializeField] private float defaultProgressDisplaySeconds = 3.5f;
    [SerializeField] private float defaultInventoryDisplaySeconds = 2.0f;
    [SerializeField] private float defaultGeneralDisplaySeconds = 2.5f;
    [SerializeField] private float secondsBetweenNotifications = 0.15f;

    [Header("Inventory Batching")]
    [Tooltip("If true, rapid inventory notices are grouped into one expanding multi-line toast.")]
    [SerializeField] private bool batchInventoryNotices = true;

    [Tooltip("How long to wait for more inventory notices before displaying the batch.")]
    [SerializeField] private float inventoryBatchWindowSeconds = 0.12f;

    [Tooltip("Maximum number of inventory lines shown in a single toast before the rest roll into the next toast.")]
    [SerializeField] private int maxInventoryLinesPerToast = 6;

    [Tooltip("Title used when multiple inventory notices are shown together.")]
    [SerializeField] private string inventoryBatchTitle = "Inventory";

    [Tooltip("If true, a single inventory notice still uses the old title/body format.")]
    [SerializeField] private bool preserveSingleInventoryNoticeFormatting = true;

    [Header("Debug")]
    [SerializeField] private bool verboseLogging = false;

    private readonly Dictionary<NotificationChannel, List<QueuedNotification>> _queuesByChannel = new();
    private readonly Dictionary<NotificationChannel, Coroutine> _routinesByChannel = new();

    public void EnqueueProgressNotice(string title, string body, int priority = 500, float displaySeconds = -1f)
    {
        Enqueue(NotificationChannel.Progress, title, body, priority, displaySeconds);
    }

    public void EnqueueInventoryNotice(string title, string body, int priority = 250, float displaySeconds = -1f)
    {
        Enqueue(NotificationChannel.Inventory, title, body, priority, displaySeconds);
    }

    public void EnqueueGeneralNotice(string title, string body, int priority = 100, float displaySeconds = -1f)
    {
        Enqueue(NotificationChannel.General, title, body, priority, displaySeconds);
    }

    public void Enqueue(
        NotificationChannel channel,
        string title,
        string body,
        int priority = 100,
        float displaySeconds = -1f)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        NotificationToastView view = GetView(channel);

        if (view == null)
        {
            if (verboseLogging)
            {
                Debug.LogWarning($"[PlayerNotificationController] No toast view assigned for channel '{channel}'.", this);
            }

            return;
        }

        List<QueuedNotification> queue = GetOrCreateQueue(channel);

        float resolvedDisplaySeconds = displaySeconds > 0f
            ? displaySeconds
            : GetDefaultDisplaySeconds(channel);

        queue.Add(new QueuedNotification(
            title,
            body,
            priority,
            resolvedDisplaySeconds,
            Time.realtimeSinceStartup));

        queue.Sort(CompareQueuedNotifications);

        if (!_routinesByChannel.ContainsKey(channel) || _routinesByChannel[channel] == null)
        {
            if (channel == NotificationChannel.Inventory && batchInventoryNotices)
            {
                _routinesByChannel[channel] = StartCoroutine(DisplayBatchedInventoryRoutine(channel));
            }
            else
            {
                _routinesByChannel[channel] = StartCoroutine(DisplayRoutine(channel));
            }
        }

        if (verboseLogging)
        {
            Debug.Log($"[PlayerNotificationController] Queued {channel} notice: {title} {body}", this);
        }
    }

    private List<QueuedNotification> GetOrCreateQueue(NotificationChannel channel)
    {
        if (!_queuesByChannel.TryGetValue(channel, out List<QueuedNotification> queue) || queue == null)
        {
            queue = new List<QueuedNotification>();
            _queuesByChannel[channel] = queue;
        }

        return queue;
    }

    private IEnumerator DisplayRoutine(NotificationChannel channel)
    {
        NotificationToastView view = GetView(channel);

        if (view == null)
        {
            _routinesByChannel[channel] = null;
            yield break;
        }

        while (_queuesByChannel.TryGetValue(channel, out List<QueuedNotification> queue) &&
               queue != null &&
               queue.Count > 0)
        {
            QueuedNotification notification = queue[0];
            queue.RemoveAt(0);

            view.Show(notification.Title, notification.Body);

            yield return new WaitForSecondsRealtime(notification.DisplaySeconds);

            view.Hide();

            if (secondsBetweenNotifications > 0f)
            {
                yield return new WaitForSecondsRealtime(secondsBetweenNotifications);
            }
        }

        _routinesByChannel[channel] = null;
    }

    private IEnumerator DisplayBatchedInventoryRoutine(NotificationChannel channel)
    {
        NotificationToastView view = GetView(channel);

        if (view == null)
        {
            _routinesByChannel[channel] = null;
            yield break;
        }

        while (_queuesByChannel.TryGetValue(channel, out List<QueuedNotification> queue) &&
               queue != null &&
               queue.Count > 0)
        {
            float batchDelay = Mathf.Max(0f, inventoryBatchWindowSeconds);

            if (batchDelay > 0f)
            {
                yield return new WaitForSecondsRealtime(batchDelay);
            }
            else
            {
                yield return null;
            }

            if (queue.Count <= 0)
            {
                continue;
            }

            int maxLines = Mathf.Max(1, maxInventoryLinesPerToast);
            int batchCount = Mathf.Min(maxLines, queue.Count);

            List<QueuedNotification> batch = new List<QueuedNotification>(batchCount);

            for (int i = 0; i < batchCount; i++)
            {
                QueuedNotification notification = queue[0];
                queue.RemoveAt(0);
                batch.Add(notification);
            }

            if (batch.Count == 1 && preserveSingleInventoryNoticeFormatting)
            {
                QueuedNotification single = batch[0];

                view.Show(single.Title, single.Body);

                yield return new WaitForSecondsRealtime(single.DisplaySeconds);
            }
            else
            {
                string title = string.IsNullOrWhiteSpace(inventoryBatchTitle)
                    ? "Inventory"
                    : inventoryBatchTitle;

                string body = BuildInventoryBatchBody(batch, queue.Count);
                float displaySeconds = GetBatchDisplaySeconds(batch);

                view.Show(title, body);

                yield return new WaitForSecondsRealtime(displaySeconds);
            }

            view.Hide();

            if (secondsBetweenNotifications > 0f)
            {
                yield return new WaitForSecondsRealtime(secondsBetweenNotifications);
            }
        }

        _routinesByChannel[channel] = null;
    }

    private static string BuildInventoryBatchBody(List<QueuedNotification> batch, int remainingQueuedCount)
    {
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < batch.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
            }

            builder.Append(FormatInventoryLine(batch[i]));
        }

        if (remainingQueuedCount > 0)
        {
            builder.AppendLine();
            builder.Append($"+{remainingQueuedCount} more...");
        }

        return builder.ToString();
    }

    private static string FormatInventoryLine(QueuedNotification notification)
    {
        if (notification == null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(notification.Title))
        {
            return notification.Body;
        }

        if (string.IsNullOrWhiteSpace(notification.Body))
        {
            return notification.Title;
        }

        if (notification.Title.StartsWith("+") || notification.Title.StartsWith("-"))
        {
            return $"{notification.Title} {notification.Body}";
        }

        return $"{notification.Title}: {notification.Body}";
    }

    private static float GetBatchDisplaySeconds(List<QueuedNotification> batch)
    {
        if (batch == null || batch.Count <= 0)
        {
            return 0.25f;
        }

        float longestDisplaySeconds = 0.25f;

        for (int i = 0; i < batch.Count; i++)
        {
            if (batch[i] == null)
            {
                continue;
            }

            longestDisplaySeconds = Mathf.Max(longestDisplaySeconds, batch[i].DisplaySeconds);
        }

        return Mathf.Max(0.25f, longestDisplaySeconds + ((batch.Count - 1) * 0.35f));
    }

    private NotificationToastView GetView(NotificationChannel channel)
    {
        switch (channel)
        {
            case NotificationChannel.Progress:
                return progressToastView != null ? progressToastView : generalToastView;

            case NotificationChannel.Inventory:
                return inventoryToastView != null ? inventoryToastView : generalToastView;

            case NotificationChannel.System:
            case NotificationChannel.General:
            default:
                return generalToastView != null ? generalToastView : progressToastView;
        }
    }

    private float GetDefaultDisplaySeconds(NotificationChannel channel)
    {
        switch (channel)
        {
            case NotificationChannel.Progress:
                return Mathf.Max(0.25f, defaultProgressDisplaySeconds);

            case NotificationChannel.Inventory:
                return Mathf.Max(0.25f, defaultInventoryDisplaySeconds);

            case NotificationChannel.System:
            case NotificationChannel.General:
            default:
                return Mathf.Max(0.25f, defaultGeneralDisplaySeconds);
        }
    }

    private static int CompareQueuedNotifications(QueuedNotification a, QueuedNotification b)
    {
        int priorityCompare = b.Priority.CompareTo(a.Priority);

        if (priorityCompare != 0)
        {
            return priorityCompare;
        }

        return a.QueuedTime.CompareTo(b.QueuedTime);
    }

    private sealed class QueuedNotification
    {
        public string Title { get; }
        public string Body { get; }
        public int Priority { get; }
        public float DisplaySeconds { get; }
        public float QueuedTime { get; }

        public QueuedNotification(
            string title,
            string body,
            int priority,
            float displaySeconds,
            float queuedTime)
        {
            Title = title ?? string.Empty;
            Body = body ?? string.Empty;
            Priority = priority;
            DisplaySeconds = Mathf.Max(0.25f, displaySeconds);
            QueuedTime = queuedTime;
        }
    }
}