using UnityEngine;

public class EnemySensor : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Vision")]
    public Transform eyePoint;
    public float viewDistance = 12f;
    [Range(0, 360)]
    public float viewAngle = 90f;

    [Header("Optional SphereCast")]
    public float sphereRadius = 0.35f;

    [Header("Layers")]
    public LayerMask obstacleMask;

    public Vector3 LastKnownPosition { get; private set; }

    public bool CanSeePlayer()
    {
        if (player == null)
            return false;

        Vector3 origin = eyePoint != null
            ? eyePoint.position
            : transform.position + Vector3.up;

        Vector3 direction = player.position - origin;

        float distance = direction.magnitude;

        if (distance > viewDistance)
            return false;

        float angle = Vector3.Angle(
            transform.forward,
            direction.normalized);

        if (angle > viewAngle / 2f)
            return false;

        // Confirma se existe obstáculo entre o inimigo e o jogador.
        if (Physics.Linecast(
            origin,
            player.position,
            out RaycastHit hit,
            obstacleMask, QueryTriggerInteraction.Ignore) &&
            hit.transform != player && !hit.transform.IsChildOf(player) &&
            hit.transform != transform && !hit.transform.IsChildOf(transform))
        {
            return false;
        }

        LastKnownPosition = player.position;
        return true;
    }

    public bool SphereDetectPlayer()
    {
        if (player == null)
            return false;
      Vector3 origin = eyePoint != null
            ? eyePoint.position
            : transform.position + Vector3.up;

        Vector3 direction =
            transform.forward;

        if (Physics.SphereCast(
            origin,
            sphereRadius,
            direction,
            out RaycastHit hit,
            viewDistance))
        {
            if (hit.transform.CompareTag("Player"))
            {
                LastKnownPosition = player.position;
                return true;
            }
        }

        return false;
    }

    public bool RaycastDetectPlayer()
    {
        if (player == null)
            return false;

        Vector3 origin = eyePoint != null
            ? eyePoint.position
            : transform.position + Vector3.up;

        if (Physics.Raycast(
            origin,
            transform.forward,
            out RaycastHit hit,
            viewDistance))
        {
            if (hit.transform.CompareTag("Player"))
            {
                LastKnownPosition = player.position;
                return true;
            }
        }

        return false;
    }
}
