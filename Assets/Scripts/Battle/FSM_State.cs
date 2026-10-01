using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FSM 상태 베이스. 캐릭터 상태(Idle, Move, Attack, ActiveSkill, Passive, Damage, Die 등)는 이 클래스를 상속해
/// 진입 / 매 프레임 / 종료 시점의 동작을 구현한다. 상태 객체는 싱글톤으로 두고 owner를 인자로 받아 공유한다.
/// </summary>
abstract public class FSM_State<T>
{
    virtual public void EnterState(T _Owner) { }
    virtual public void UpdateState(T _Owner) { }
    virtual public void ExitState(T _Owner) { }
}
