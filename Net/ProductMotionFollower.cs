using Mirror;
using UnityEngine;

namespace SweepersHeaven.Net;

internal sealed class ProductMotionFollower : MonoBehaviour
{
    private StolenProductSpawn product;
    private Rigidbody body;
    private bool originalKinematic;
    private bool following;
    private bool hasSnapshot;
    private Vector3 from;
    private Quaternion fromRotation;
    private ProductMotion snapshot;
    private float receivedAt;

    internal void Initialize(StolenProductSpawn value)
    {
        product = value;
        body = GetComponent<Rigidbody>();
        originalKinematic = body && body.isKinematic;
    }

    internal void Receive(ProductMotion value)
    {
        from = transform.position;
        fromRotation = transform.rotation;
        snapshot = value;
        receivedAt = Time.unscaledTime;
        hasSnapshot = true;
    }

    private void LateUpdate()
    {
        if (NetworkServer.active || !Plugin.Instance || !Plugin.Instance.SynchronizedHost)
        {
            Restore();
            return;
        }
        if (!hasSnapshot || !product || !product.isFinished) return;
        if (body && !following)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            following = true;
        }
        float t = Mathf.Clamp01((Time.unscaledTime - receivedAt) / 0.1f);
        transform.SetPositionAndRotation(Vector3.Lerp(from, snapshot.Position, t),
            Quaternion.Slerp(fromRotation, snapshot.Rotation, t));
    }

    internal void Restore()
    {
        if (following && body) body.isKinematic = originalKinematic;
        following = false;
    }
    private void OnDisable() { Restore(); }
}
