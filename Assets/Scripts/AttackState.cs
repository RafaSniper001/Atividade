using UnityEngine;

public class AttackState : IEnemyState
{
    private readonly EnemyBrain enemy;

    public AttackState(EnemyBrain enemy) { this.enemy = enemy; }

    public void Enter()
    {
        enemy.movement.Stop();
        enemy.movement.agent.velocity = Vector3.zero;
    }

    public void Update()
    {
        if (enemy.sensor == null || enemy.sensor.player == null)
        {
            enemy.ChangeState(new SearchState(enemy));
            return;
        }
        Vector3 direction = enemy.sensor.player.position - enemy.transform.position;
        direction.y = 0;
        if (direction.sqrMagnitude > 0.001f)
            enemy.transform.rotation = Quaternion.LookRotation(direction);

        if (!enemy.CanSeePlayer())
            enemy.ChangeState(new SearchState(enemy));
        else if (enemy.PlayerDistance > enemy.attackRange + 0.5f)
            enemy.ChangeState(new ChaseState(enemy));
    }

    public void Exit() { enemy.movement.agent.isStopped = false; }
}
