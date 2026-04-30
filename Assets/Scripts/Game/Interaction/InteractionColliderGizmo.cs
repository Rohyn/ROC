using UnityEngine;

[DisallowMultipleComponent]
public class InteractionColliderGizmo : MonoBehaviour
{
    [SerializeField] private Color color = new Color(0.2f, 0.8f, 1f, 0.25f);
    [SerializeField] private bool drawWhenNotSelected = true;

    private Collider _collider;

    private void OnValidate()
    {
        _collider = GetComponent<Collider>();
    }

    private void Awake()
    {
        _collider = GetComponent<Collider>();
    }

    private void OnDrawGizmos()
    {
        if (drawWhenNotSelected)
        {
            Draw();
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawWhenNotSelected)
        {
            Draw();
        }
    }

    private void Draw()
    {
        if (_collider == null)
        {
            _collider = GetComponent<Collider>();
        }

        if (_collider == null)
        {
            return;
        }

        Gizmos.color = color;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        if (_collider is BoxCollider box)
        {
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (_collider is SphereCollider sphere)
        {
            Gizmos.DrawSphere(sphere.center, sphere.radius);
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }

        Gizmos.matrix = oldMatrix;
    }
}