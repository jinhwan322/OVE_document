using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Shared.Regulation;

/// <summary>
/// 캐릭터별 스킬 쿨타임 관리자. 매 고정 틱마다 쿨타임을 줄이고, 다 차면 IdleOwner에 사용 플래그를 세운다.
/// - 일반 공격: 쿨타임마다 자동 공격
/// - 액티브 스킬(필살기): 쿨타임이 SP 게이지가 되고, 게이지가 차면 스킬 대기열에 올라간다.
///   한 번에 한 캐릭터만 사용하며, 오토 모드거나 적군이면 자동으로 대기열에 넣고 수동 모드에서는 슬롯을 눌러 사용한다.
/// - 패시브: 게임 시작 / 체력 조건형은 바로 발동 대기, 그 외는 쿨타임마다 발동
/// </summary>
public class SkillObserver : MonoBehaviour
{
    private IdleOwner skillUser;
    private CharacterSkillDataRow baseSkill;
    private CharacterSkillDataRow activeSkill;
    private CharacterSkillDataRow passiveSkill;
    private CharacterSkillDataRow specialSkill;

    private CharacterParamDataRow characterData;
    private Regulation regulation => IdleGameManager.instance.regulation;

    // 스킬별 최대 쿨타임 (초)
    private float baseCoolTime;
    public float activeCoolTime;
    private float passiveCoolTime;
    private float specialCoolTime;

    // 스킬별 남은 쿨타임 (초)
    private float baCoolTime = 0.0f;
    public float acCoolTime = 0.0f;
    private float paCoolTime = 0.0f;
    private float spCoolTime = 0.0f;

    // 기획 데이터의 쿨타임 단위(0.1초)를 초로 바꾸는 값
    private const float CoolTimeUnit = 0.1f;

    bool initialize = false;

    /// <summary>
    /// 캐릭터 데이터의 스킬 목록(0: 일반 공격, 1: 액티브, 2: 패시브, 3: 스페셜)으로 쿨타임을 설정한다.
    /// 시작할 때 쿨타임이 가득 찬 상태에서 시작한다.
    /// </summary>
    public void InitializeSkillObserver(IdleOwner user)
    {
        if (initialize)
            return;

        InitData();

        skillUser = user;

        characterData = regulation.FindCharacter(skillUser.GetId());
        if (characterData != null && characterData.Skill != null)
        {
            List<string> skills = characterData.Skill;

            baseSkill = FindSkill(skills, 0, out baseCoolTime);
            baCoolTime = baseCoolTime;

            activeSkill = FindSkill(skills, 1, out activeCoolTime);
            acCoolTime = activeCoolTime;

            passiveSkill = FindSkill(skills, 2, out passiveCoolTime);
            paCoolTime = passiveCoolTime;

            specialSkill = FindSkill(skills, 3, out specialCoolTime);
            spCoolTime = specialCoolTime;
        }
        initialize = true;
    }

    /// <summary>스킬 목록의 index번째 스킬과 쿨타임(초)을 찾는다. 없으면 null, 쿨타임 0.</summary>
    private CharacterSkillDataRow FindSkill(List<string> skills, int index, out float coolTime)
    {
        coolTime = 0.0f;
        if (index >= skills.Count)
            return null;

        CharacterSkillDataRow skillData = regulation.FindSkill(skills[index]);
        if (skillData != null)
            coolTime = skillData.CooldownTime * CoolTimeUnit;
        return skillData;
    }

    /// <summary>고정 틱마다 호출. 일반 공격 → 액티브 → 패시브 순서로 쿨타임을 진행한다.</summary>
    public void SkillUpdate()
    {
        if (skillUser.GetDie)
            return;

        StartBaseSkill();

        if (activeSkill != null && activeCoolTime > 0)
            StartActiveSkill();

        if (passiveSkill != null)
            StartPassiveSkill();
    }

    void InitData()
    {
        baseSkill = null;
        specialSkill = null;
        passiveSkill = null;
        activeSkill = null;

        baseCoolTime = 0.0f;
        activeCoolTime = 0.0f;
        passiveCoolTime = 0.0f;
        specialCoolTime = 0.0f;
    }

    /// <summary>다음 웨이브 등에서 다시 초기화할 수 있게 한다.</summary>
    public void ReInitialize()
    {
        initialize = false;
    }

    private void StartBaseSkill()
    {
        if (skillUser.isNormalAttack || baseCoolTime <= 0)
            return;

        if (baCoolTime <= 0)
            skillUser.isNormalAttack = true;

        baCoolTime -= Time.fixedDeltaTime;
    }

    /// <summary>
    /// 액티브 스킬 쿨타임(SP 게이지) 진행.
    /// 게이지가 차면 같은 편에서 스킬을 쓰는 중인 캐릭터가 없을 때 대기열에 올라가고,
    /// 대기열 맨 앞 차례가 되면 사용 플래그를 세운다.
    /// </summary>
    private void StartActiveSkill()
    {
        if (skillUser.useActiveSkill || activeCoolTime <= 0)
            return;

        if (acCoolTime <= 0 && IdleGameManager.instance.IsPossibleUseSkill(skillUser.GetOwnerType()))
        {
            IdleOwner queueOwner = IdleGameManager.instance.UseSkillOwner();
            if (queueOwner != null)
            {
                // 대기열 맨 앞이 자신이면 스킬 사용 (그 사이 죽었으면 대기열에서 제거)
                if (queueOwner == skillUser)
                {
                    if (skillUser.GetDie)
                    {
                        skillUser.RemoveActiveSkillIcon();
                        return;
                    }
                    skillUser.useActiveSkill = true;
                }
            }
            else if (CanAutoQueueActiveSkill())
            {
                skillUser.idleGameMgr.AddSkillQueue(skillUser);
            }
        }

        acCoolTime -= Time.fixedDeltaTime;
        SetSPSlider();
    }

    /// <summary>
    /// 자동으로 스킬 대기열에 넣을 수 있는지: 오토 모드거나 적군(방어 팀)이고, 침묵 / 스턴이 아니며 대기 상태일 때
    /// </summary>
    private bool CanAutoQueueActiveSkill()
    {
        bool isAutoControl = skillUser.idleGameMgr.isAuto || skillUser._teamType == ETeamType.Defence;
        return isAutoControl
            && !skillUser.IsSilence
            && !skillUser.IsStun
            && skillUser.GetOwnerState().GetCurState() == State_Idle.Instance;
    }

    /// <summary>아군이면 하단 슬롯의 SP 게이지를 갱신한다.</summary>
    void SetSPSlider()
    {
        if (skillUser.GetOwnerType() == EOwnerType.Player)
            skillUser.idleBattle._slots[skillUser.SlotPos].SetSpSlider(activeCoolTime - acCoolTime);
    }

    /// <summary>스킬 사용이 끝나면 해당 스킬의 쿨타임을 다시 채우고 사용 플래그를 내린다.</summary>
    public void EndSkill(ESkillSlotType type)
    {
        switch (type)
        {
            case ESkillSlotType.Damage:
                baCoolTime = baseCoolTime;
                skillUser.isNormalAttack = false;
                break;
            case ESkillSlotType.Active:
                acCoolTime = activeCoolTime;
                SetSPSlider();
                skillUser.useActiveSkill = false;
                break;
            case ESkillSlotType.Passive:
                paCoolTime = passiveCoolTime;
                skillUser.usePassiveSkill = false;
                break;
            case ESkillSlotType.Special:
                spCoolTime = specialCoolTime;
                skillUser.useSpecialSkill = false;
                break;
        }
    }

    /// <summary>패시브: 게임 시작형 / 체력 조건형은 항상 발동 대기, 그 외는 쿨타임마다 발동</summary>
    private void StartPassiveSkill()
    {
        if (passiveSkill.Type == ESkillType.GameStart || passiveSkill.Type == ESkillType.PlayerMaxHp_Per)
        {
            skillUser.usePassiveSkill = true;
            return;
        }

        if (skillUser.usePassiveSkill)
            return;

        if (paCoolTime <= 0)
            skillUser.usePassiveSkill = true;

        paCoolTime -= Time.fixedDeltaTime;
    }
}
