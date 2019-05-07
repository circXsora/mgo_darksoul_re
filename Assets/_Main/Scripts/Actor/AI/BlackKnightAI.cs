﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiquidState;
using LiquidState.Synchronous.Core;
using UnityEngine;

public class BlackKnightAI : MonoBehaviour
{

    private ActorManager am;
    public float speed = 1.0f;
    public bool run = true;

    [SerializeField]
    private float canAttackDistance = 1.4f;
    void Start()
    {
        if (am == null)
        {
            am = GetComponent<ActorManager>();
        }
        Configure();
        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            fsm.Fire(BlackKnightTrigger.Patrol);
        }
    }
    void Update()
    {
        if (playerTansform != null && fsm != null)
        {
            if (Vector3.Distance(this.transform.position, playerTansform.position) < canAttackDistance && fsm.CurrentState == BlackKnightState.Following)
            {
                fsm.Fire(BlackKnightTrigger.Touch);
            }
            else 
            if(Vector3.Distance(this.transform.position, playerTansform.position) >= canAttackDistance && fsm.CurrentState == BlackKnightState.Attcking)
            {
                fsm.Fire(BlackKnightTrigger.UnTouch);
            }
        }
    }
    #region State Machine
    enum BlackKnightState
    {
        Idle,
        Patroling,
        Attcking,
        Following
    }
    enum BlackKnightTrigger
    {
        Patrol,
        Found,
        FollowFail,
        Touch,
        UnTouch
    }
    [ContextMenu("Partrol")]
    public void Patrol()
    {
        fsm.Fire(BlackKnightTrigger.Patrol);
    }
    [ContextMenu("Stop Follow")]
    public void FollowFail()
    {
        fsm.Fire(BlackKnightTrigger.FollowFail);
    }
    private IStateMachine<BlackKnightState, BlackKnightTrigger> fsm;
    void Configure()
    {
        var config = StateMachineFactory.CreateConfiguration<BlackKnightState, BlackKnightTrigger>();

        config.ForState(BlackKnightState.Idle).Permit(BlackKnightTrigger.Patrol, BlackKnightState.Patroling);

        config.ForState(BlackKnightState.Patroling).OnEntry(() =>
        {
            StartFind();
            StartPatrol();
        })
        .OnExit(() =>
        {
            StopFind();
            StopPatrol();
        })
        .Permit(BlackKnightTrigger.Found, BlackKnightState.Following);

         config.ForState(BlackKnightState.Following).OnEntry(() =>
        {
            StartFollow();
        })
        .OnExit(() =>
        {
            StopFollow();
        })
        .Permit(BlackKnightTrigger.FollowFail, BlackKnightState.Patroling)
        .Permit(BlackKnightTrigger.Touch, BlackKnightState.Attcking);

        config.ForState(BlackKnightState.Attcking).OnEntry(()=>{
            StartAutoAttack();
            //Attack();
        }).OnExit(()=>{
            StopAutoAttack();
            //UnAttack();
        })
        .Permit(BlackKnightTrigger.UnTouch, BlackKnightState.Patroling)
        ;
        fsm = StateMachineFactory.Create(BlackKnightState.Idle, config);
    }
    #endregion

    #region Patrol
    public Transform[] patrolPoints;

    public float waitTime;
    [ContextMenu("Start Patrol Task")]
    public async void StartPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length <= 0)
        {
            Debug.Log("patrol points is not set");
            return;
        }
        partrolTaskCTS?.Cancel();
        partrolTaskCTS = new CancellationTokenSource();
        var ct = partrolTaskCTS.Token;
        using (var partrolTask = PatrolTask(true, ct))
        {
            await partrolTask;
        }
    }
    CancellationTokenSource partrolTaskCTS;
    [ContextMenu("Stop Patrol Task")]
    public void StopPatrol()
    {
        if (partrolTaskCTS != null)
        {
            partrolTaskCTS.Cancel();
        }
    }
    private async Task PatrolTask(bool loop, CancellationToken cancellationToken)
    {

        do
        {
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    loop = false;
                    return;
                }
                await MoveToThePointAsync(patrolPoints[i], cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(waitTime));
            }
        } while (loop);

    }
    #endregion

    #region Find Player
    public Transform playerTansform;
    public CancellationTokenSource findCTS;
    [ContextMenu("Start Find")]
    public async void StartFind()
    {
        findCTS?.Cancel();
        findCTS = new CancellationTokenSource();
        var ct = findCTS.Token;
        using (var task = FindTaskAsync(ct))
        {
            await task;
        }
    }

    private async Task FindTaskAsync(CancellationToken ct)
    {
        GameObject player = null;
        while (player == null)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }
            player = CheckFoundPlayer();
            await Task.Delay((int)(Time.deltaTime * 1000));
        }
        partrolTaskCTS?.Cancel();
        playerTansform = player.transform;
        fsm.Fire(BlackKnightTrigger.Found);
        //await MoveToThePointAsync(playerTansform, ct);
    }
    [ContextMenu("Stop Find")]
    public void StopFind()
    {
        findCTS.Cancel();
    }
    #endregion

    #region Follow Player
    public CancellationTokenSource followCTS;
    public async void StartFollow()
    {
        followCTS?.Cancel();
        followCTS = new CancellationTokenSource();
        var ct = followCTS.Token;
        using (var task = FollowTaskAsync(ct))
        {
            await task;
        }
    }

    private async Task FollowTaskAsync(CancellationToken ct)
    {
        await MoveToThePointAsync(playerTansform, ct);
    }
    [ContextMenu("Stop Find")]
    public void StopFollow()
    {
        followCTS?.Cancel();
    }
    #endregion

    #region Attack Auto
    public CancellationTokenSource autoAttackCTS;
    [ContextMenu("Start Attak")]
    public async void StartAutoAttack()
    {
        autoAttackCTS?.Cancel();
        autoAttackCTS = new CancellationTokenSource();
        await AutoAttackTask(autoAttackCTS.Token);
    }

    public async Task AutoAttackTask(CancellationToken ct)
    {
        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                UnAttack();
                return;
            }
            var canAttack = CheckCanAttackPlayer();
            if (canAttack)
            {
                am.ac.model.transform.LookAt(playerTansform);
                Attack();
            }
            else
            {
                UnAttack();
            }
            await Task.Delay((int)(1000 * Time.deltaTime));
        }
    }

    [ContextMenu("Stop Attack")]
    public void StopAutoAttack()
    {
        UnAttack();
        autoAttackCTS?.Cancel();
    }
    #endregion

    private async Task MoveToThePointAsync(Transform transform, CancellationToken cancellationToken)
    {

        while (!IsTouched(this.transform, transform))
        {

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            Vector3 dir = transform.position - this.transform.position;
            dir.Normalize();
            this.transform.LookAt(dir);
            Move(dir, speed, run);
            await Task.Delay((int)(Time.deltaTime * 1000));
            if (this == null)
            {
                return;
            }
        }
        Debug.Log("OK");
        Reset();
        // if (fsm != null&& fsm.CurrentState == BlackKnightState.Following)
        // {
        //     fsm?.Fire(BlackKnightTrigger.Touch);
        // }
    }

    private GameObject CheckFoundPlayer()
    {
        var cols = Physics.OverlapBox(this.transform.position, new Vector3(0.5f, 0.5f, 5), am.ac.model.transform.rotation, LayerMask.GetMask("Player"));
        if (cols.Length > 0)
        {
            return cols[0].gameObject;
        }
        else return null;
    }

    private bool CheckCanAttackPlayer()
    {
        var cols = Physics.OverlapBox(this.transform.position, new Vector3(0.5f, 0.5f, 1), am.ac.model.transform.rotation, LayerMask.GetMask("Player"));
        if (cols.Length > 0)
        {
            return true;
        }
        else return false;
    }

    public bool IsTouched(Transform personTrans, Transform targetTrans)
    {
        var personPos = personTrans.position;
        var targetPos = targetTrans.position;
        Vector2 pplanePos = new Vector2(personPos.x, personPos.z);
        Vector2 tplanePos = new Vector2(targetPos.x, targetPos.z);
        //        Debug.Log( Vector2.Distance(pplanePos, tplanePos));
        if (Vector2.Distance(pplanePos, tplanePos) < 0.5f)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public void Move(Vector3 direction, float speed = 0.7f, bool run = false)
    {
        var ac = am.ac as EnemyAC;
        ac.playerInput.Dmag = speed;
        ac.playerInput.Dforward = direction;
        ac.playerInput.run = run;
    }
    public void Attack()
    {
        var ac = am.ac as EnemyAC;
        ac.playerInput.rb = true;
    }

    public void UnAttack()
    {
        var ac = am.ac as EnemyAC;
        ac.playerInput.rb = false;
    }

    public void Roll(Vector3 direction)
    {

    }

    public void Defense()
    {
        var ac = am.ac as EnemyAC;
        ac.playerInput.lb = true;
    }

    public void Reset()
    {
        var ac = am.ac as EnemyAC;
        ac.playerInput.Dmag = 0;
        ac.playerInput.Dforward = Vector3.zero;
        ac.playerInput.run = false;
        ac.playerInput.rb = false;
        ac.playerInput.lb = false;
    }


    void OnDestroy()
    {
        partrolTaskCTS?.Cancel();
        findCTS?.Cancel();
    }
}