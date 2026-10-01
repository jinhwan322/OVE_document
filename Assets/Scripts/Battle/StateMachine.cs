using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 FSM.
/// 상태 전환(Exit → Enter)과 함께, 공격 / 스킬 애니메이션의 Spine 이벤트("Hit")를 고정 틱 시간으로 추적해
/// 이벤트 시점에 맞춰 피격 판정(owner.TargetHit)을 호출한다.
/// 렌더링 프레임이 아닌 FixedUpdate 시간으로 판정해서, 배속이나 프레임 저하에도 전투 결과가 같게 나온다.
/// </summary>
public class StateMachine<T> where T : IdleOwner
{
    private T owner;
    private FSM_State<T> currState;
    private FSM_State<T> prevState;

    // 현재 재생 중인 애니메이션의 길이와 Hit 이벤트 목록
    private float duration;
    private Spine.Animation animation;
    private List<HitData> hitTime = new List<HitData>();
    private int hitIndex = 0;
    private float time = 0;

    public void Awake()
    {
        currState = null;
        prevState = null;
    }

    public void ChangeState(FSM_State<T> _newState)
    {
        prevState = currState;

        if (currState != null)
            currState.ExitState(owner);

        currState = _newState;

        if (currState != null)
            currState.EnterState(owner);
    }

    public void Init(T _owner, FSM_State<T> _initState)
    {
        owner = _owner;
        ChangeState(_initState);
    }

    public void Update()
    {
        if (currState == null)
            return;

        currState.UpdateState(owner);
    }

    /// <summary>직전 상태로 되돌린다.</summary>
    public void StateRevert()
    {
        if (prevState != null)
            ChangeState(prevState);
    }

    public FSM_State<T> GetCurState()
    {
        return currState;
    }

    /// <summary>재생할 애니메이션을 등록하고, 애니메이션에 들어 있는 이벤트(이름, 시간)를 모아 둔다.</summary>
    public void SetAnimation(Spine.Animation animation)
    {
        this.animation = animation;
        duration = this.animation.duration;
        time = 0.0f;
        hitTime.Clear();
        hitIndex = 0;

        foreach (Spine.Timeline timeline in this.animation.Timelines)
        {
            var eventTimeline = timeline as Spine.EventTimeline;
            if (eventTimeline == null)
                continue;

            for (int i = 0; i < eventTimeline.Events.Length; i++)
            {
                Spine.Event sEvent = eventTimeline.Events[i];
                HitData data = new HitData();
                data.name = sEvent.data.name;
                data.time = sEvent.time;
                hitTime.Add(data);
            }
        }
    }

    /// <summary>
    /// 고정 틱마다 애니메이션 시간을 진행하고, 이벤트 시간에 도달하면 처리한다. ("Hit" 이벤트만 피격 판정)
    /// </summary>
    /// <returns>애니메이션이 끝났으면 true</returns>
    public bool SetFixedTime()
    {
        if (animation == null)
            return false;

        time += Time.fixedDeltaTime;

        if (hitIndex < hitTime.Count && time >= hitTime[hitIndex].time)
        {
            string eventName = hitTime[hitIndex].name;
            if (eventName == "Hit" || eventName == "hit")
                owner.TargetHit();

            // Hit 외 이벤트도 지나간 것으로 처리해야 다음 이벤트로 넘어간다
            hitIndex++;
        }

        return time >= duration;
    }

    class HitData
    {
        public string name;
        public float time;
    }
}
