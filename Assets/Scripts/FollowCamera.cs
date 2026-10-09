using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    [Tooltip("Desligado: a câmera fica onde foi colocada na cena. Ligado: acompanha o jogador.")]
    public bool follow = false;

    public Transform target;
    public Vector3 offset = new Vector3(0f, 15f, -5f);
    [Min(0)] public float smoothTime = 0.15f;

    private Vector3 velocity;

    void LateUpdate()
    {
        if (!follow || target == null)
            return;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            target.position + offset,
            ref velocity,
            smoothTime);
    }
}
