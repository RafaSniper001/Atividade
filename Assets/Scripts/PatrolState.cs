using UnityEngine;

public class PatrolState : IEnemyState
{
    private bool waiting;
    private float waitTimer;
    private EnemyBrain enemy;

    public PatrolState(EnemyBrain enemy)
    {
        this.enemy = enemy;
    }

    public void Enter()
    {
        waiting = false;
        waitTimer = 0;
        enemy.movement.GoToNextPatrolPoint();
    }

    public void Update()
    {
        if (enemy.CanSeePlayer())
        {
            enemy.PlayerSeen();
            enemy.ChangeState(
                new ChaseState(enemy));
            return;
        }

        if (enemy.ConsumeAlert(out Vector3 alertPosition))
        {
            enemy.ChangeState(
                new InvestigateState(
                    enemy,
                    alertPosition));
            return;
        }

        if (enemy.ConsumeNoise(out Vector3 noisePosition))
        {
            enemy.ChangeState(
                new InvestigateState(
                    enemy,
                    noisePosition));
            return;
        }

        if (waiting)
        {
            waitTimer += Time.deltaTime;
            if (waitTimer >= enemy.patrolWaitTime)
            {
                waiting = false;
                enemy.movement.GoToNextPatrolPoint();
            }
        }
        else if (enemy.movement.ReachedDestination())
       {
            enemy.movement.Stop();
            waiting = true;
            waitTimer = 0;
        }
    }

    public void Exit()
    {
    }
}
