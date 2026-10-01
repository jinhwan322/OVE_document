using System.Collections.Generic;
using UnityEngine;
using Shared.Regulation;

/// <summary>캐릭터 능력치 묶음. 기본 능력치와 버프를 합산할 수 있도록 +, - 연산자를 제공한다.</summary>
[System.Serializable]
public struct sStatusInfo
{
    public int hp;
    public float moveSpeed;
    public int atk;
    public int spAtk;
    public float atkSpeed;
    public float atkRange;
    public int def;
    public int spDef;
    public float avoidance;
    public float criticalPer;
    public float criticalDamage;

    // 속성별 데미지 증감 (주는 피해 / 받는 피해, 10000 = 100%)
    public int damageToTheFire;
    public int damageToTheWater;
    public int damageToTheWind;
    public int damageToTheDark; 
    public int damageToTheLight;
    public int damageByFire;   
    public int damageByWater;  
    public int damageByWind;
    public int damageByDark;
    public int damageByLight;

    public float healCorrection;

    public static sStatusInfo operator +(sStatusInfo lhs, sStatusInfo rhs)
    {
        sStatusInfo returnStruct = new sStatusInfo();

        returnStruct.hp         = lhs.hp + rhs.hp;
        returnStruct.spAtk      = lhs.spAtk + rhs.spAtk;
        returnStruct.moveSpeed  = lhs.moveSpeed + rhs.moveSpeed;
        returnStruct.atk        = lhs.atk + rhs.atk;
        returnStruct.atkSpeed   = lhs.atkSpeed + rhs.atkSpeed;
        returnStruct.atkRange   = lhs.atkRange + rhs.atkRange;
        returnStruct.def        = lhs.def + rhs.def;
        returnStruct.spDef      = lhs.spDef + rhs.spDef;
        returnStruct.avoidance  = (lhs.avoidance + rhs.avoidance);
        returnStruct.criticalPer        = (lhs.criticalPer + rhs.criticalPer);
        returnStruct.criticalDamage     = (lhs.criticalDamage + rhs.criticalDamage);
        returnStruct.damageToTheFire    = lhs.damageToTheFire + rhs.damageToTheFire;
        returnStruct.damageToTheWater   = lhs.damageToTheWater + rhs.damageToTheWater;
        returnStruct.damageToTheWind    = lhs.damageToTheWind + rhs.damageToTheWind;
        returnStruct.damageToTheDark    = lhs.damageToTheDark + rhs.damageToTheDark;
        returnStruct.damageToTheLight   = lhs.damageToTheLight + rhs.damageToTheLight;
        returnStruct.damageByWater  = lhs.damageByWater + rhs.damageByWater;
        returnStruct.damageByFire   = lhs.damageByFire + rhs.damageByFire;
        returnStruct.damageByWind   = lhs.damageByWind + rhs.damageByWind;
        returnStruct.damageByDark   = lhs.damageByDark + rhs.damageByDark;
        returnStruct.damageByLight  = lhs.damageByLight + rhs.damageByLight;
        returnStruct.healCorrection = (lhs.healCorrection + rhs.healCorrection);

        return returnStruct;
    }

    public static sStatusInfo operator -(sStatusInfo lhs, sStatusInfo rhs)
    {
        sStatusInfo returnStruct = new sStatusInfo();

        returnStruct.hp         = lhs.hp - rhs.hp;
        returnStruct.spAtk      = lhs.spAtk - rhs.spAtk;
        returnStruct.moveSpeed  = lhs.moveSpeed - rhs.moveSpeed;
        returnStruct.atk        = lhs.atk - rhs.atk;
        returnStruct.atkSpeed   = lhs.atkSpeed - rhs.atkSpeed;
        returnStruct.atkRange   = lhs.atkRange - rhs.atkRange;
        returnStruct.def        = lhs.def - rhs.def;
        returnStruct.spDef      = lhs.spDef - rhs.spDef;
        returnStruct.avoidance  = (lhs.avoidance - rhs.avoidance);
        returnStruct.criticalPer        = (lhs.criticalPer - rhs.criticalPer);
        returnStruct.criticalDamage     = (lhs.criticalDamage - rhs.criticalDamage);
        returnStruct.damageToTheFire    = lhs.damageToTheFire - rhs.damageToTheFire;
        returnStruct.damageToTheWater   = lhs.damageToTheWater - rhs.damageToTheWater;
        returnStruct.damageToTheWind    = lhs.damageToTheWind - rhs.damageToTheWind;
        returnStruct.damageToTheDark    = lhs.damageToTheDark - rhs.damageToTheDark;
        returnStruct.damageToTheLight   = lhs.damageToTheLight - rhs.damageToTheLight;
        returnStruct.damageByWater  = lhs.damageByWater - rhs.damageByWater;
        returnStruct.damageByFire   = lhs.damageByFire - rhs.damageByFire;
        returnStruct.damageByWind   = lhs.damageByWind - rhs.damageByWind;
        returnStruct.damageByDark   = lhs.damageByDark - rhs.damageByDark;
        returnStruct.damageByLight  = lhs.damageByLight - rhs.damageByLight;
        returnStruct.healCorrection = (lhs.healCorrection - rhs.healCorrection);

        return returnStruct;
    }
}

/// <summary>
/// 전투 유닛 베이스. 능력치, 데미지 계산, 실드, 버프 / 상태이상을 관리한다.
/// - 최종 능력치 = 기본 능력치 + 적용 중인 버프 합계
/// - 버프는 능력치·지속형(_skillBuff)과 상태이상·실드·면역(_conditionBuff)으로 나눠 관리하고,
///   같은 효과가 겹치면 수치가 높거나 지속시간이 긴 쪽만 남긴다
/// - 화면 표시(데미지 숫자, 버프 아이콘 / 이펙트)는 virtual로 두고 IdleOwner에서 구현한다
/// </summary>
public class ObjectBase : MonoBehaviour 
{
    const string NORMAL_DAMAGE_BUBBLE_NAME      = "Bubble";
    protected const string DODGE_BUBBLE_NAME    = "Bubble_M";
    protected const string HEAL_BUBBLE_NAME     = "Bubble_H";
    const string UP_DAMAGE_BUBBLE_NAME          = "Bubble_U";
    const string DOWN_DAMAGE_BUBBLE_NAME        = "Bubble_D";

    Regulation regulation => IdleGameManager.instance.regulation;
    
    [SerializeField]
    public ETeamType    _teamType;      // 팀 타입 (디팬스 모드에서는 디팬스가 아군 모험모드에선 어태커가 아군)

    private bool _isInvincibility;   // 무적인가?
    protected bool IsInvincibility => _isInvincibility;
    
    protected bool _isDie;             // 죽었는가?
    public bool GetDie => _isDie;
    
    protected bool _isStun;            // 스턴에 걸렸는가?
    public bool IsStun => _isStun;
    
    protected bool _isSilence;         // 침묵에 걸렸는가?
    public bool IsSilence => _isSilence;
    
    public int _shieldTypeAmount   = 0; // 횟수형 실드
    public int _shieldTypeHp       = 0; // 체력형 실드
    
    protected bool _isProvocation;          // 도발
    protected int  _provocationSlotNumber;          // 도발을 건 상대

    [SerializeField]
    public List<ObjectBase> _targetList = new List<ObjectBase>();            // 현재 타겟.
    [SerializeField]
    protected sStatusInfo   _originStatus;       // 캐릭터의 오리지날 스테이터스.
    private sStatusInfo _resultStatus => _originStatus + _buffState;    // 장비, 버프등을 포함한 최종 스테이터스
    public sStatusInfo GetResultStatus => _resultStatus;

    private int _hp; // 현재 Hp
    private int _maxHp;
    public int    GetMaxHp => _maxHp;// 총 Hp

    public RoCharacter      _roCharacter = null;
    protected int           _slotPos = -1;
    public int slotPos => _slotPos;
    public Chronos.Timeline _timeLine;          // 시간 컨트롤용 타임라인.
    
    // 스킬 데이터
    protected Dictionary<ESkillSlotType, CharacterSkillDataRow> _skillsDic = new Dictionary<ESkillSlotType, CharacterSkillDataRow>();

    // 버프 목록
    private List<BuffInfo> _skillBuff = new List<BuffInfo>();        // 능력치 / 지속 데미지 / 지속 회복 (1초 간격으로 적용)
    private List<BuffInfo> _conditionBuff = new List<BuffInfo>();    // 상태이상, 실드, 면역 등
    protected List<BuffInfo> _equipBuff = new List<BuffInfo>();      // 장비 효과: 일반 공격 시 확률로 대상에게 거는 효과 (자신에게는 적용하지 않음)
    private List<BuffInfo> _equipSkillBuff = new List<BuffInfo>();   // 장비 효과: 자신에게 상시 적용되는 속성 데미지 증감
    sStatusInfo _buffState;                                           // 적용 중인 능력치 버프 합계

    private float GetGameTime => IdleGameManager.instance.GetGameTime;
    protected bool IsViewSkillAnimation;

    /// <summary>액티브 스킬 연출 중 시간 제어: 유닛 타임라인을 Root 클럭에 연결한다.</summary>
    public virtual void ActiveSkill()
    {
        _timeLine.globalClockKey = "Root"; 
    }

    public int Hp
    {   // hp 외부에서 접근할 때 쓰이는 프로퍼티
        get => _hp;
        private set
        {
            if (value > GetMaxHp)
                value = GetMaxHp;
            else if (value < 0)
                value = 0;
            _hp = value;
        }
    }

    protected void Init()           // 초기화
    {
        IsViewSkillAnimation = false;
        StatusInit();
        InitSkillData();

        RefreshHp();
    }

    private void SetOriginStatus(sStatusInfo status)
    {
        _originStatus = status;
    }

    protected void StatusInit()
    {
        OwnerStatusInit(_roCharacter);
    }

    private void OwnerStatusInit(RoCharacter roCharacter)
    {
        sStatusInfo statusInfo  = new sStatusInfo();

        statusInfo.hp           = roCharacter.ResultHP;
        statusInfo.atk          = roCharacter.ResultAtk;
        statusInfo.spAtk        = roCharacter.ResultSp;
        statusInfo.def          = roCharacter.ResultAtkDef;
        statusInfo.spDef        = roCharacter.ResultSpDef;
        statusInfo.avoidance    = roCharacter.ResultDodgeRate;
        statusInfo.criticalPer  = roCharacter.ResultCriticalRate;
        statusInfo.criticalDamage = roCharacter.ResultCriticalDamage;
        statusInfo.atkSpeed     = 1;

        SetOriginStatus(statusInfo);
    }

    protected void InitSkillData()
    {   // 스킬 셋업.
        if (_roCharacter == null || _roCharacter.characterParamDataRow == null)
            return;

        RemoveBuff_All();

        List<string> Skills = _roCharacter.characterParamDataRow.Skill;

        if (Skills == null)
            return;

        _skillsDic.Clear();

        for (int i = 0; i < Skills.Count; i++)
        {
            if (Skills[i] == "0")
                continue;
                
            CharacterSkillDataRow skillDb = regulation.characterSkillDtbl[Skills[i]];

            if (skillDb != null)
                _skillsDic.Add((ESkillSlotType)i, skillDb);
        }
    }

    public virtual float Heal(DamageNode damageNode)
    {
        Hp = Mathf.Min(GetMaxHp, Hp + damageNode.damage);
        float healAmount = damageNode.damage;
        SetDamageBubble(damageNode);

        return healAmount;
    }

    public virtual void Damaged(DamageNode damageNode)
    {   // 피격 - 맞았을 때
        if (_isInvincibility)
            return;
        
        Hp -= GetDamage(damageNode);
        ActiveEquipBuff(damageNode);
        SetDamageBubble(damageNode);
    }

    public void DamagedSkillCard(DamageNode damageNode)
    {   // 피격 - 맞았을 때 (스킬카드, 타일카드)
        if (_isInvincibility)
            return;

        Hp -= GetDamage(damageNode);
        ActiveEquipBuff(damageNode);
        SetDamageBubble(NORMAL_DAMAGE_BUBBLE_NAME, (int)damageNode.accDamage, damageNode.isCritical, damageNode.isHeal);
    }

    public void ActiveEquipBuff(DamageNode damageNode)
    {
        // 피격 시 발동하는 장비 효과 자리 (현재 미사용)
    }

    /// <summary>속성 상성에 따라 데미지 숫자 종류(유리 / 불리 / 보통)를 다르게 띄운다. (상성 값 10000 = 보정 없음)</summary>
    private void SetDamageBubble(DamageNode damageNode)
    {
        if (damageNode.isHeal)
        {
            SetDamageBubble(HEAL_BUBBLE_NAME, damageNode.damage, damageNode.isCritical, damageNode.isHeal);
            return;
        }
        
        if (_roCharacter == null)
            return;
        
        if (damageNode.attackRoCharacter == null)
            return;
        if (damageNode.attackRoCharacter.characterElementDataRow == null)
            return;
        List<int> elementValueList = damageNode.attackRoCharacter.characterElementDataRow.GetElementValue();
        int value = elementValueList[int.Parse(_roCharacter.characterElementDataRow.Idx) - 1];
        if (value > 10000)
            SetDamageBubble(UP_DAMAGE_BUBBLE_NAME, (int)damageNode.accDamage, damageNode.isCritical, damageNode.isHeal);
        else if (value < 10000)
            SetDamageBubble(DOWN_DAMAGE_BUBBLE_NAME, (int)damageNode.accDamage, damageNode.isCritical, damageNode.isHeal);
        else
            SetDamageBubble(NORMAL_DAMAGE_BUBBLE_NAME, (int)damageNode.accDamage, damageNode.isCritical, damageNode.isHeal);

    }

    protected void Hit(ObjectBase target)
    {   // 타격 - 내가 상대를 때릴 때
        DamageNode damageNode           = new DamageNode();

        damageNode.damage               = _resultStatus.atk;
        damageNode.attackObject         = this;
        damageNode.teamType             = _teamType;
        damageNode.attackRoCharacter    = _roCharacter;
        damageNode.equipBuff            = _equipBuff;

        target.Damaged(damageNode);
    }

    /// <summary>
    /// 스킬 데미지 계산.
    /// 1) 스킬 계수: 기준 능력치(최대 HP / 공격력 / 방어력 / 지능 / 저항) × (계수 + 레벨 보너스 × 스킬 레벨) / 10000, 또는 고정값
    /// 2) 방어 감산: 지능 기반 스킬은 대상의 저항, 그 외는 대상의 방어력을 뺀다
    /// 3) 속성 보정: 속성 상성표 + 버프로 증감 (10000 = 100%)
    /// 4) 치명타: 치명타 데미지 배율만큼 추가
    /// 5) 다단 히트 스킬은 타수로 나눠 한 타의 데미지를 만든다 (최소 1)
    /// </summary>
    protected int GetAttackDamage(ESkillSlotType slotType, ObjectBase target, bool isCritical, bool isUsingHitCount, int effectNum = 1)
    {
        CharacterSkillDataRow skillData = _skillsDic[slotType];
        EEffect eEffect = effectNum == 1 ? skillData.Effect1 : skillData.Effect2;
        int hitCount = effectNum == 1 ? skillData.EffectDetail1 : skillData.EffectDetail2;
        int effectFigure = effectNum == 1 ? skillData.EffectFigure1 : skillData.EffectFigure2;
        int levelBonus = effectNum == 1 ? skillData.LvBonus1 : skillData.LvBonus2;

        // 1) 스킬 계수
        int atkValue = 0;
        int baseStat;
        if (eEffect == EEffect.Integer)
        {
            atkValue = effectFigure / 100;
        }
        else if (TryGetSkillBaseStat(eEffect, out baseStat))
        {
            int coefficient = effectFigure + levelBonus * _roCharacter.skillDataList[(int)slotType].level;
            atkValue = (baseStat * coefficient) / 10000;
        }

        // 2) 방어 감산
        int defValue = eEffect == EEffect.Int_Per ? target._resultStatus.spDef : target._resultStatus.def;
        atkValue -= defValue;

        // 3) 속성 보정 (원본 상성표가 바뀌지 않도록 복사본에 버프를 더한다)
        List<int> elementValueList = new List<int>(_roCharacter.characterElementDataRow.GetElementValue());
        elementValueList[0] += _buffState.damageToTheFire;
        elementValueList[1] += _buffState.damageToTheWater;
        elementValueList[2] += _buffState.damageToTheWind;
        elementValueList[3] += _buffState.damageToTheDark;
        elementValueList[4] += _buffState.damageToTheLight;

        int elementValue = elementValueList[int.Parse(target._roCharacter.characterElementDataRow.Idx) - 1];
        elementValue += target.GetDamageTakenBonus(_roCharacter.characterParamDataRow.Element);
        atkValue = (atkValue * elementValue) / 10000;

        // 4) 치명타
        if (isCritical)
        {
            int criDamage = CommonUtil.RoundToInt(GetCriticalDamageRate() * 10000f);
            atkValue += (atkValue * criDamage) / 10000;
        }

        // 5) 다단 히트
        if (isUsingHitCount && hitCount > 1)
            atkValue /= hitCount;

        return Mathf.Max(atkValue, 1);
    }

    /// <summary>스킬 계수가 참조하는 기준 능력치. 능력치 비례형 효과가 아니면 false.</summary>
    private bool TryGetSkillBaseStat(EEffect effect, out int baseStat)
    {
        switch (effect)
        {
            case EEffect.MaxHp_Per:      baseStat = _resultStatus.hp;    return true;
            case EEffect.Atk_Per:        baseStat = _resultStatus.atk;   return true;
            case EEffect.AdDef_Per:      baseStat = _resultStatus.def;   return true;
            case EEffect.Int_Per:        baseStat = _resultStatus.spAtk; return true;
            case EEffect.Resistance_Per: baseStat = _resultStatus.spDef; return true;
            default:                     baseStat = 0;                   return false;
        }
    }

    /// <summary>공격자 속성에 대해 이 유닛이 받는 피해 증감 버프</summary>
    private int GetDamageTakenBonus(EElement attackerElement)
    {
        switch (attackerElement)
        {
            case EElement.Fire:  return _buffState.damageByFire;
            case EElement.Aqua:  return _buffState.damageByWater;
            case EElement.Wind:  return _buffState.damageByWind;
            case EElement.Dark:  return _buffState.damageByDark;
            case EElement.Light: return _buffState.damageByLight;
            default:             return 0;
        }
    }

    public virtual void TargetHit()
    {

    }

    /// <summary>
    /// 실드를 반영한 최종 피해량. 횟수형 실드는 1회 피해를 통째로 막고, 체력형 실드는 실드량만큼 흡수한다.
    /// accDamage에는 실드 적용 전 피해량을 남겨 데미지 숫자 표시에 쓴다.
    /// </summary>
    private int GetDamage(DamageNode damageNode)
    {
        damageNode.accDamage = damageNode.damage;
        if (_shieldTypeAmount > 0)
        {
            damageNode.damage = 0;
            _shieldTypeAmount--;
        }
        else if (_shieldTypeHp > 0)
        {
            if (_shieldTypeHp >= damageNode.damage)
            {
                _shieldTypeHp -= damageNode.damage;
                damageNode.damage = 0;
            }
            else
            {
                damageNode.damage -= _shieldTypeHp;
                _shieldTypeHp = 0;
            }
        }
        return Mathf.Abs(damageNode.damage);
    }

    protected float GetDodgeRate()
    {
        return _resultStatus.avoidance;
    }

    public float GetSpDefence()
    {
        return _resultStatus.spDef;
    }

    protected float GetCriticalRate()
    {
        return _resultStatus.criticalPer;
    }

    private float GetCriticalDamageRate()
    {
        return _resultStatus.criticalDamage;
    }

    protected virtual void SetDamageBubble(string poolName, int damageValue, bool isCritical, bool isHeal)
    {
    }

    // 전투 중에는 IdleGameManager가 고정 틱마다 BattleUpdate()에서 버프를 갱신하므로, 매니저가 없을 때만 직접 갱신한다
    public virtual void FixedUpdate()
    {
        if (IdleGameManager.Instance != null)
            return;

        if (Hp <= 0)
            return;
        
        BuffUpdate();
    }

    #region 버프

    /// <summary>1초 간격으로 지속형 버프(데미지, 회복 등)와 면역 효과를 적용하고, 끝난 버프를 정리한다.</summary>
    protected void BuffUpdate()
    {
        if (_isDie)
            return;

        for (int i = 0; i < _skillBuff.Count; i++)
        {
            if (_skillBuff[i]._lastActiveTime + 1f <= GetGameTime)
            {
                _skillBuff[i]._lastActiveTime = GetGameTime;
                _skillBuff[i].ActiveBuff(this);
                if (_isDie)
                    return;
            }
        }
        for (int i = 0; i < _conditionBuff.Count; i++)
        {
            if (_conditionBuff[i]._lastActiveTime + 1f <= GetGameTime)
            {
                _conditionBuff[i]._lastActiveTime = GetGameTime;
                _conditionBuff[i].ActiveBuff(this);
                if (_isDie)
                    return;
            }
            else
            {
                if (_conditionBuff[i].EffectType == EEffectType.Immune)
                    ImmuneBuff(_conditionBuff[i]);
                if (_isDie)
                    return;
            }
        }

        BuffEndCheck();
    }

    /// <summary>스킬로 버프를 건다. 겹치는 버프 규칙을 통과하면 적용하고 아이콘을 띄운다.</summary>
    protected void AddSkillBuff(BuffInfo buff, ObjectBase useBuffObject)
    {
        BuffInfo createBuff = new BuffInfo(buff);

        createBuff.Set(_originStatus, GetMaxHp, useBuffObject);

        if (AddAbleBuffCheck(createBuff, _skillBuff) == false)
            return;

        if (buff.EffectType == EEffectType.Damage ||
            buff.EffectType == EEffectType.Heal ||
            buff.EffectType == EEffectType.Buff ||
            buff.EffectType == EEffectType.Dot)
            _skillBuff.Add(createBuff);
        else
            _conditionBuff.Add(createBuff);
            
        ActiveBuff(createBuff);
        AddBuffIcon(createBuff);
    }

    private void RefreshHp()
    {
        _maxHp = _resultStatus.hp;
        Hp = _resultStatus.hp;
    }

    /// <summary>
    /// 버프를 추가할 수 있는지 판단한다.
    /// - 무적 상태에서는 데미지 / 해로운 효과를 받지 않는다
    /// - 같은 효과가 이미 있으면 수치가 더 높거나(같으면 지속시간이 더 길면) 기존 버프를 지우고 교체한다
    /// - 실드는 실드량(횟수)이 더 많을 때만 교체한다
    /// </summary>
    private bool AddAbleBuffCheck(BuffInfo buff, List<BuffInfo> buffList)
    {
        if (_isInvincibility)
        {
            if (buff.EffectType == EEffectType.Damage ||
                buff.EffectType == EEffectType.Dot ||
                buff.EffectType == EEffectType.ReleaseImmunity)
                return false;
            
            if(buff.EffectType == EEffectType.CC || buff.EffectType == EEffectType.Immune)
            {
                if (buff.Effect != EEffect.Invincibility)
                    return false;
            }
            else if(buff.EffectType == EEffectType.Buff)
            {
                if (buff.EffectDetail == 0)
                    return false;
            }
        }


        // 추가가 가능한 버프인가.
        if (buff.EffectType == EEffectType.Damage ||
            buff.EffectType == EEffectType.Heal ||
            buff.EffectType == EEffectType.Buff ||
            buff.EffectType == EEffectType.Dot)
        {
            for (int i = 0; i < buffList.Count; i++)
            {
                if (buff.EffectType != buffList[i].EffectType)
                    continue;

                if (buff.Effect != buffList[i].Effect)
                    continue;

                if (buff.EffectType == EEffectType.Buff)
                {   // 버프일 경우
                    if (buff.EffectDetail != buffList[i].EffectDetail)
                        continue;   // 다른 버프이면 패스.
                }

                if (buff.returnValue > buffList[i].returnValue)
                {   // 같은 버프인데 추가할 버프의 능력치가 높은경우.
                    RemoveBuff(buffList[i]);    // 있던버프를 삭제하고
                    return true;                // 버프 추가 가능하다라고 리턴함.
                }
                else if(buff.returnValue == buffList[i].returnValue)
                {  // 같은 버프인데 추가할 버프의 능력치가 같을 경우
                    if (buff.Duration > buffList[i]._dotTic)
                    {   // 제한 시간이 더 길경우
                        RemoveBuff(buffList[i]);    // 있던버프를 삭제하고
                        return true;                // 버프 추가 가능하다라고 리턴함.
                    }
                }
                return false;
            }
        }
        else
        {
            for (int i = 0; i < _conditionBuff.Count; i++)
            {
                if (buff.EffectType != _conditionBuff[i].EffectType ||  // 버프타입이 다르거나
                    buff.Effect != _conditionBuff[i].Effect)            // 버프 효과가 다르면 체크안한다.
                    continue;

                if (buff.EffectType == EEffectType.Shild)
                {   // 실드일경우
                    if (buff.Effect == EEffect.ShieldType_Hp)
                    {   // hp 형 실드일 경우
                        if (buff.returnValue <= _shieldTypeHp)  // 현재 hp 실드량보다 추가할 실드량이 더 적으면
                            return false;                       // 추가하지 않는다
                        else
                        {   // 추가하려는 실드량이 지금 가지고있는 실드량이 많으면
                            RemoveBuff(_conditionBuff[i]);  // 있던 버프를 삭제하고
                            return true;                    // 추가한다.
                        }
                    }
                    
                    if (buff.Effect == EEffect.ShieldType_Num)
                    {   // 횟수형 실드일 경우
                        if (buff.returnValue <= _shieldTypeAmount)  // 현재 횟수형 실드량보다 추가할 실드량이 더 적으면
                            return false;                           // 추가하지 않는다
                  
                         // 추가하려는 실드량이 지금 가지고있는 실드량이 많으면
                        RemoveBuff(_conditionBuff[i]);  // 있던 버프를 삭제하고
                        return true;                    // 추가한다.
                    }
                }

                if (buff.Duration > _conditionBuff[i]._dotTic)
                {   // 그 외 컨디션 버프(스턴, 무적 등) 추가하려는 버프의 시간이 더 길경우
                    RemoveBuff(_conditionBuff[i]);   // 있던 버프를 삭제하고
                    return true;                     // 추가한다.
                }
                return false;
            }
        }
        return true;
    }

    /// <summary>지속시간이 끝난 버프를 정리한다. 실드는 실드가 남아 있으면 유지한다.</summary>
    private void BuffEndCheck()
    {
        List<BuffInfo> removeBuff = new List<BuffInfo>();

        for (int i = 0; i < _skillBuff.Count; i++)
        {
            if (_skillBuff[i]._dotTic <= 0)
            {
                removeBuff.Add(_skillBuff[i]);
            }
        }
        for (int i = 0; i < _conditionBuff.Count; i++)
        {
            if (_conditionBuff[i]._dotTic <= 0)
            {
                if (_conditionBuff[i].EffectType == EEffectType.Shild)
                {
                    if(_conditionBuff[i].Effect == EEffect.ShieldType_Hp)
                    {
                        if (_shieldTypeHp > 0)
                            continue;
                    }
                    else if(_conditionBuff[i].Effect == EEffect.ShieldType_Num)
                    {
                        if (_shieldTypeAmount > 0)
                            continue;
                    }
                }
                removeBuff.Add(_conditionBuff[i]);
            }
        }
        for (int i = 0; i < removeBuff.Count; i++)
        {
            RemoveBuff(removeBuff[i]);
        }
    }

    /// <summary>버프 효과를 적용한다. (능력치 합산, 상태이상 플래그, 실드량 설정, 즉시 데미지 / 회복)</summary>
    private void ActiveBuff(BuffInfo buff)
    {
        if (buff.EffectType == EEffectType.Damage)
        {
            buff.ActiveBuff(this);
            buff._lastActiveTime = GetGameTime;
            return;
        }
        
        if (buff.EffectType == EEffectType.Buff)
        {
            _buffState += buff.buffSatus;
        }
        else if (buff.EffectType == EEffectType.Heal)
        {
            buff.ActiveBuff(this);
            buff._lastActiveTime = GetGameTime;
        }
        else if (buff.EffectType == EEffectType.CC)
        {
            if (buff.Effect == EEffect.Stun)
            {
                _isStun = true;
            }
            else if (buff.Effect == EEffect.Silence)
            {
                _isSilence = true;
            }
            else if (buff.Effect == EEffect.Provocation)
            {
                _isProvocation = true;
                _provocationSlotNumber = buff._useBuffUnit.slotPos;
            }

            else if (buff.Effect == EEffect.Invincibility)
                _isInvincibility = true;

            buff._lastActiveTime = GetGameTime;
        }
        else if (buff.EffectType == EEffectType.Shild)
        {
            if (buff.Effect == EEffect.ShieldType_Hp)
                _shieldTypeHp       = buff.returnValue;
            else if (buff.Effect == EEffect.ShieldType_Num)
                _shieldTypeAmount   = buff.returnValue;
        }
        else if (buff.EffectType == EEffectType.Immune || buff.EffectType == EEffectType.ReleaseImmunity)
        {
            buff.ActiveBuff(this);
            buff._lastActiveTime = GetGameTime;
        }
    }

    /// <summary>버프 효과를 되돌리고 목록과 아이콘에서 제거한다.</summary>
    private void RemoveBuff(BuffInfo buff)
    {
        if (buff.EffectType == EEffectType.Buff)
        {
            _buffState      -= buff.buffSatus;
        }
        else if (buff.EffectType == EEffectType.CC)
        {
            if (buff.Effect == EEffect.Stun)
                _isStun = false;
            else if (buff.Effect == EEffect.Silence)
                _isSilence = false;
            else if (buff.Effect == EEffect.Provocation)
                _isProvocation = false;
            else if (buff.Effect == EEffect.Invincibility)
                _isInvincibility = false;
        }
        else if(buff.EffectType == EEffectType.Shild)
        {
            if (buff.Effect == EEffect.ShieldType_Hp)
                _shieldTypeHp = 0;
            else if (buff.Effect == EEffect.ShieldType_Num)
                _shieldTypeAmount = 0;
        }

        if (_skillBuff.Contains(buff))
            _skillBuff.Remove(buff);
        else if (_conditionBuff.Contains(buff))
            _conditionBuff.Remove(buff);

        RemoveBuffIcon(buff);
    }

    /// <summary>면역: 같은 효과의 걸려 있는 버프를 지운다. 면역 해제: 걸려 있는 면역 효과를 지운다.</summary>
    public void ImmuneBuff(BuffInfo buff)
    {
        BuffInfo currentBuff = null;
        if (buff.EffectType == EEffectType.Immune)
        {
            for (int i = 0; i < _skillBuff.Count; i++)
            {
                BuffInfo skillBuff = _skillBuff[i];
                if (skillBuff.EffectType == EEffectType.Buff || 
                    skillBuff.EffectType == EEffectType.Dot)
                {
                    if(skillBuff.Effect == buff.Effect && skillBuff.EffectDetail == buff.EffectDetail)
                        currentBuff = skillBuff;
                }
            }

            for (int i = 0; i < _conditionBuff.Count; i++)
            {
                BuffInfo skillBuff = _conditionBuff[i];
                if (skillBuff.EffectType == EEffectType.CC || skillBuff.EffectType == EEffectType.Shild)
                {
                    if (skillBuff.Effect == buff.Effect && skillBuff.EffectDetail == buff.EffectDetail)
                        currentBuff = skillBuff;
                }
            }
        }
        else if(buff.EffectType == EEffectType.ReleaseImmunity)
        {
            for (int i = 0; i < _conditionBuff.Count; i++)
            {
                BuffInfo skillBuff = _conditionBuff[i];
                if (skillBuff.EffectType == EEffectType.Immune)
                {
                    if (skillBuff.Effect == buff.Effect && skillBuff.EffectDetail == buff.EffectDetail)
                        currentBuff = skillBuff;
                }
            }
        }

        if (currentBuff != null)
        {
            RemoveBuff(currentBuff);
        }
    }

    protected virtual void AddBuffIcon(BuffInfo buff)
    {
        BuffEffect(buff);
    }

    protected virtual void RemoveBuffIcon(BuffInfo buff)
    {
        RemoveBuffEffect(buff);
    }

    /// <summary>
    /// 자신에게 걸린 버프를 모두 해제한다. (스킬 버프, 상태이상, 상시 장비 효과)
    /// _equipBuff는 대상에게 거는 장비 효과 원본이라 해제 대상이 아니다.
    /// </summary>
    protected void RemoveBuff_All()
    {
        for (int i = _skillBuff.Count - 1; i >= 0; i--)
            RemoveBuff(_skillBuff[i]);

        for (int i = _conditionBuff.Count - 1; i >= 0; i--)
            RemoveBuff(_conditionBuff[i]);

        for (int i = _equipSkillBuff.Count - 1; i >= 0; i--)
            RemoveBuff(_equipSkillBuff[i]);

        // RemoveBuff는 스킬 / 상태이상 목록에서만 빼므로 상시 장비 효과 목록은 직접 비운다 (중복 차감 방지)
        _equipSkillBuff.Clear();
    }

    protected void RemoveBuff_Condition()
    {
        for (int i = _conditionBuff.Count - 1; i >= 0; i--)
        {
            BuffInfo buff = _conditionBuff[i];
            RemoveBuff(buff);
        }
    }

    /// <summary>해당 효과의 능력치 / 지속형 버프가 걸려 있는지 확인한다.</summary>
    protected bool ExistBuff(EEffect effect)
    {
        return _skillBuff.Exists(buff => buff.Effect == effect);
    }

    /// <summary>
    /// 장착 장비(머리, 몸통, 장신구, 신발, 무기)의 효과를 분류한다.
    /// 지속 데미지 / 상태이상은 공격 시 대상에게 거는 효과(_equipBuff)로, 속성 데미지 증감은 자신에게 상시 적용한다.
    /// </summary>
    protected void SetEquipBuff()
    {
        _equipBuff.Clear();

        if (_roCharacter._equipHeadArmor != null)
            AddEquipmentBuffs(_roCharacter._equipHeadArmor._buffList);
        if (_roCharacter._equipBodyArmor != null)
            AddEquipmentBuffs(_roCharacter._equipBodyArmor._buffList);
        if (_roCharacter._equipAccessory_0 != null)
            AddEquipmentBuffs(_roCharacter._equipAccessory_0._buffList);
        if (_roCharacter._equipShoes != null)
            AddEquipmentBuffs(_roCharacter._equipShoes._buffList);
        if (_roCharacter._equipWeapon != null)
            AddEquipmentBuffs(_roCharacter._equipWeapon._buffList);
    }

    private void AddEquipmentBuffs(List<BuffInfo> buffList)
    {
        for (int i = 0; i < buffList.Count; i++)
        {
            BuffInfo buff = buffList[i];
            if (AddAbleEquipBuffCheck(buff))
                _equipBuff.Add(buff);
            else
                AddEquipSkillBuff(buff);
        }
    }

    /// <summary>속성 데미지 증감 장비 효과를 자신에게 상시 적용한다.</summary>
    void AddEquipSkillBuff(BuffInfo buff)
    {
        BuffInfo createBuff = new BuffInfo(buff);

        createBuff.Set(_originStatus, GetMaxHp, this);

        if(buff.EffectType == EEffectType.Buff)
        {
            if((int)buff.Effect >= (int)EEffect.DamageToTheFire_Per && (int)buff.Effect <= (int)EEffect.DamageByLight_Per)
            {
                _equipSkillBuff.Add(createBuff);
                ActiveBuff(createBuff);
            }
        }
    }

    /// <summary>공격 시 대상에게 거는 장비 효과(지속 데미지 / 상태이상)인지 확인하고, 같은 효과는 더 강한 쪽만 남긴다.</summary>
    private bool AddAbleEquipBuffCheck(BuffInfo buff)
    {
        if (buff.EffectType == EEffectType.Dot || buff.EffectType == EEffectType.CC)
        {
            for (int i = 0; i < _equipBuff.Count; i++)
            {
                if (buff.Effect != _equipBuff[i].Effect)
                    continue;

                if (buff.EffectType == EEffectType.Dot)
                {
                    if (buff.returnValue > _equipBuff[i].returnValue)
                    {
                        _equipBuff.Remove(_equipBuff[i]);
                        return true;
                    }
                }
                else if (buff.EffectType == EEffectType.CC)
                {
                    if (buff.Duration > _equipBuff[i]._dotTic)
                    {
                        _equipBuff.Remove(_equipBuff[i]);
                        return true;
                    }
                }
            }
            return true;
        }
        
        return false;
    }

    protected virtual void BuffEffect(BuffInfo buff)
    {

    }

    protected virtual void RemoveBuffEffect(BuffInfo buff)
    {

    }

    #endregion
}