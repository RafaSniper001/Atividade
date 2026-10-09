using UnityEngine;

public class EnemyBrain : MonoBehaviour
{
    public float patrolSpeed = 2f;
    public float chaseSpeed = 4f;
    [Min(0)] public float patrolWaitTime = 2f;
    [Min(0.5f)] public float attackRange = 2f;
    public float PlayerDistance
    {
        get
        {
            if (sensor == null || sensor.player == null) return float.PositiveInfinity;
            Vector3 offset = sensor.player.position - transform.position;
            offset.y = 0;
            return offset.magnitude;
        }
    }
    public string CurrentStateName => currentState?.GetType().Name;
    [Header("Systems")]
    public EnemySensor sensor;
    public EnemyMovement movement;
    public EnemyAnimator enemyAnimator;
    public EnemyCommunication communication;

    [Header("Behavior")]
    public bool isPatroller = true;
    public float searchTime = 5f;

    private IEnemyState currentState;

    private Vector3 lastKnownPosition;
    private bool hasNoise;
    private bool hasAlert;

    void Start()
    {
        if (sensor != null && sensor.player == null)
        {
            GameObject playerObject =
                GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
                sensor.player = playerObject.transform;
        }

        if (isPatroller)
            ChangeState(new PatrolState(this));
        else
            ChangeState(new WanderState(this));
    }

    void Update()
    {
        currentState?.Update();

        if (enemyAnimator != null && movement != null)
        {
            enemyAnimator.SetSpeed(
                movement.agent.velocity.magnitude);
        }
    }

    public void ChangeState(IEnemyState newState)
    {
        if (movement != null && movement.agent != null)
            movement.agent.speed = newState is ChaseState ? chaseSpeed : patrolSpeed;
        if (enemyAnimator != null)
        {
            enemyAnimator.SetAlert(newState is ChaseState);
            enemyAnimator.SetAttacking(newState is AttackState);
        }
        currentState?.Exit();

        currentState = newState;

        currentState?.Enter();
    }

    public bool CanSeePlayer()
    {
        bool visible = sensor != null && sensor.CanSeePlayer();
        if (visible) lastKnownPosition = sensor.LastKnownPosition;
        return visible;
    }

    public Vector3 GetLastKnownPosition()
    {
        return lastKnownPosition;
    }

    public void ReceiveNoise(Vector3 position)
    {
        lastKnownPosition = position;
        hasNoise = true;
    }

    public void ReceiveAlert(Vector3 position)
    {
        lastKnownPosition = position;
        hasAlert = true;
    }

    public bool ConsumeNoise(out Vector3 position)
    {
        position = lastKnownPosition;

        if (!hasNoise)
            return false;

        hasNoise = false;
        return true;
    }

    public bool ConsumeAlert(out Vector3 position)
    {
        position = lastKnownPosition;

        if (!hasAlert)
            return false;

        hasAlert = false;
        return true;
    }

    public void PlayerSeen()
    {
        lastKnownPosition = sensor.player.position;

        if (communication != null)
        {
            communication.AlertNearbyEnemies(
                lastKnownPosition);
        }

        if (enemyAnimator != null)
            enemyAnimator.SetAlert(true);
    }

    public float SearchDuration()
    {
        return searchTime;
    }
}   
