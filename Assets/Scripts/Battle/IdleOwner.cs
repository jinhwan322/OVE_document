using System.Collections;
using System.Collections.Generic;
using Chronos;
using Shared.Regulation;
using Spine.Unity;
using UnityEngine;

/// <summary>
/// 전투 유닛(아군 / 적군 공통). 프리코네 방식의 실시간 사이드 뷰 오토 배틀에서 캐릭터 하나를 담당한다.
/// - 매 고정 틱(BattleUpdate)마다 SkillObserver로 쿨타임을 진행하고, 사용할 스킬(액티브 > 패시브 > 일반 공격)을 고른다.
/// - 사거리 안에 타겟이 없으면 한 칸씩 전진하고, 있으면 타겟 조건으로 정렬해 공격 / 스킬 상태로 전환한다.
/// - 실제 피격 판정은 StateMachine이 Spine "Hit" 이벤트 시점에 TargetHit()을 호출해 처리한다.
/// 진형 좌표: 라인 1~3, 위치 1~12 (아군 1~6, 적군 7~12에서 시작)
/// </summary>
public class IdleOwner : ObjectBase
{
    public MeshRenderer _meshRenderer = null;
    public float _sp;
    public float _moveSpeed = 3.6f;
    public EElement _attributeType;
    public SkeletonAnimation _skAnimation;
    [HideInInspector]
    public bool _isDestroy;
    protected EOwnerType _ownerType;
    public StateMachine<IdleOwner> _state = null;

    public int SlotPos => _slotPos;
    protected UIHud _hud;

    // Spine 애니메이션 이름
    private const string IDLE = "01_idle";
    private const string ATTACK = "02_attack";
    private const string MOVE = "03_move";
    private const string HIT = "04_hit";
    private const string DIE = "05_die";
    private const string WIN = "06_victory";
    private const string SKILL = "08_skill_start";
    private const string PASSIVE = "12_passive1";
    private const string TOTAL_DAMAGE_BUBBLE_NAME = "Bubble_T";
    private const string MISS_DAMAGE_BUBBLE_NAME = "Bubble_M";

    public UIIdleBattle idleBattle => idleGameMgr.idleBattleUI;
    private string effectName = string.Empty;
    protected Spine.Bone _bone;
    protected Spine.Bone _projectileBone;
    
    private Color _originSpineColor;

    public int _lineNumber;

    private int _curPosNumber;
    public int curPosNumber
    {
        get => _curPosNumber;
        set => _curPosNumber = value;
    }
    private EPosType posType;

    protected Regulation regulation => idleGameMgr.regulation;
    // 속성 상성: good = 내가 유리한 상대 속성, bad = 내가 불리한 상대 속성
    protected EElement good = EElement.None;
    protected EElement bad = EElement.None;
    public ObjectBase hitter;
    public ESkillSlotType skillSlotType;            // 현재 사용 중인 스킬 슬롯
    public bool activePassiveSkill = false;         // 게임 시작형 / 체력 조건형 패시브를 이미 발동했는지
    // SkillObserver가 쿨타임이 차면 세우는 사용 플래그
    public bool usePassiveSkill = false;
    public bool useSpecialSkill = false;
    public bool useActiveSkill = false;
    public bool isNormalAttack = false;
    public bool flyingProjecTile => projectileList.Count > 0;
    private List<IdleOwner> realTargetList = new List<IdleOwner>();     // 타겟 조건으로 골라낸 실제 타겟
    public SkillObserver skillObserver;
    protected CharacterSkillDataRow activeSkill => _skillsDic[skillSlotType];   // 현재 사용 중인 스킬 데이터
    private int MaxHitCount = 1;    // 이번 공격의 총 타수
    private int hitCount = 1;       // 현재 몇 번째 타격인지
    [HideInInspector]
    public bool isUseSkill = false;
    [HideInInspector]
    public Timeline timeline;

    // 진형 범위
    private const int MaxLineNumber = 3;
    private const int MinPosNumber = 1;
    private const int MaxPosNumber = 12;
    // 슬롯 번호(0~9)별 판정 테이블 크기
    private const int MaxSlotCount = 10;

    public IdleGameManager idleGameMgr;
    List<ProjectileSetting> projectileList = new List<ProjectileSetting>();
    public Dictionary<ESkillSlotType, Transform> projectilePosition = new Dictionary<ESkillSlotType, Transform>();
    Dictionary<ESkillSlotType, Vector3> projectileOriginalPosition = new Dictionary<ESkillSlotType, Vector3>();
    Dictionary<BuffInfo, GameObject> effectList = new Dictionary<BuffInfo, GameObject>(); //버프 이펙트 리스트
    Shared.Battle.Random random => idleGameMgr.random;  // 시드 고정 난수 (같은 입력이면 같은 전투 결과)

    // 타겟 슬롯별 판정 결과. 공격 시작 시 치명타 / 회피를 미리 굴려 두고, 타격마다 데미지를 누적한다.
    Dictionary<int, bool> criticalState;
    Dictionary<int, bool> dodgeState;
    Dictionary<int, List<int>> attackValue;

    public Material material;

    /// <summary>
    /// 베이스의 액티브 스킬 처리(유닛 타임라인 클럭 전환)를 막는다.
    /// 전투 유닛의 액티브 스킬은 NewActiveTarget()에서 컷신 / 즉시 발동으로 따로 처리한다.
    /// </summary>
    public override void ActiveSkill()
    {
    }

    /// <summary>캐릭터 데이터로 유닛을 생성한다. 스탯 / 스킬 / Spine / 상성 / 진형 위치 / 장비 버프를 세팅하고 Idle 상태로 시작한다.</summary>
    public void Create(RoCharacter roCharacter, EOwnerType eOwnerType, int slotNum)
    {
        if (roCharacter == null)
            return;
        _roCharacter = roCharacter;
        if (_state == null)
            _state = new StateMachine<IdleOwner>();
        idleGameMgr = IdleGameManager.instance;
        material.shader.maximumLOD = 100;
        Init();     // 스탯 / 스킬 데이터 초기화 (InitSkillData 포함)
        InitProp();
        SetSpineComponent();
        InitAdvantageElement();
        SetOwnerType(eOwnerType);
        _state.Init(this, State_Idle.Instance);
        _attributeType = roCharacter.characterParamDataRow.Element;
        _slotPos = slotNum;
        posType = roCharacter._positionType;
        curPosNumber = roCharacter.posNum;
        SetProjectilePosition();
        _teamType = _ownerType == EOwnerType.Player ? ETeamType.Attack : ETeamType.Defence;
        SetEquipBuff();
    }

    private void InitProp()
    {
        _isDie = false;
        _isDestroy = false;

        if (criticalState == null)
        {
            criticalState = new Dictionary<int, bool>();
            dodgeState = new Dictionary<int, bool>();
            attackValue = new Dictionary<int, List<int>>();
            for (int i = 0; i < MaxSlotCount; i++)
            {
                criticalState.Add(i, false);
                dodgeState.Add(i, false);
                attackValue.Add(i, new List<int>());
            }
        }
    }

    /// <summary>발사체 생성 위치(p_01: 일반 공격, p_03: 패시브, p_04: 스페셜)를 캐싱한다.</summary>
    public void SetProjectilePosition()
    {
        if (projectilePosition.Count > 0)
            return;

        CacheProjectilePosition(ESkillSlotType.Damage, "p_01");
        CacheProjectilePosition(ESkillSlotType.Passive, "p_03");
        CacheProjectilePosition(ESkillSlotType.Special, "p_04");
    }

    private void CacheProjectilePosition(ESkillSlotType slotType, string nodeName)
    {
        Transform node = transform.Find(nodeName);
        if (node == null)
            return;

        projectileOriginalPosition.Add(slotType, node.localPosition);
        projectilePosition.Add(slotType, node);
    }

    /// <summary>스킬 슬롯의 발사체 생성 위치. 좌우 반전(flipX) 상태면 로컬 x를 뒤집는다.</summary>
    public Vector3 GetProjectilePosition(ESkillSlotType slotType)
    {
        if (!projectilePosition.ContainsKey(slotType) || projectilePosition[slotType] == null)
            return transform.position;

        int flip = _skAnimation.skeleton.flipX ? -1 : 1;
        Vector3 origin = projectileOriginalPosition[slotType];
        Transform slotTrans = projectilePosition[slotType];
        slotTrans.localPosition = new Vector3(origin.x * flip, origin.y, origin.z);
        return slotTrans.position;
    }

    /// <summary>속성 상성: Fire > Wind > Aqua > Fire, Light <-> Dark</summary>
    private void InitAdvantageElement()
    {
        switch (GetElementType())
        {
            case EElement.None:
                break;
            case EElement.Fire:
                good = EElement.Wind;
                bad = EElement.Aqua;
                break;
            case EElement.Aqua:
                good = EElement.Fire;
                bad = EElement.Wind;
                break;
            case EElement.Wind:
                good = EElement.Aqua;
                bad = EElement.Fire;
                break;
            case EElement.Dark:
                good = EElement.Light;
                break;
            case EElement.Light:
                good = EElement.Dark;
                break;
        }
    }
    private void SetSpineComponent()
    {
        SkeletonRenderer skRenderer = _skAnimation.GetComponent<SkeletonRenderer>();
        if(skRenderer != null)
        {
            _bone = skRenderer.skeleton.FindBone("Head");
            _projectileBone = skRenderer.skeleton.FindBone("ProjectileHit");
        }
        _originSpineColor = _skAnimation.skeleton.GetColor();
    }
    /// <summary>전투 시작 시 배정된 진형 위치까지 걸어간다.</summary>
    public IEnumerator MoveInitPos()
    {
        UnitPosition posInfo = idleGameMgr.posObserver.GetOccupyInfo(_lineNumber, curPosNumber);
        ChangeState(State_Move.Instance);
        if (posInfo != null)
        {
            while (this.transform.position != posInfo.transform.position)
            {
                this.transform.position = Vector3.MoveTowards(this.transform.position, posInfo.transform.position, _moveSpeed * Time.fixedDeltaTime);
                SetDepth();
                yield return new WaitForFixedUpdate();
            }
        }
        ChangeState(State_Idle.Instance);
    }

    /// <summary>
    /// 현재 스킬의 1번 효과 타겟을 찾는다.
    /// 사거리 안에 타겟이 없으면 전진하고, 있으면 스턴 / 침묵(일반 공격은 제외) 여부를 확인한 뒤 공격 또는 액티브 스킬을 시작한다.
    /// </summary>
    private void NewFindTarget()
    {
        if (activeSkill == null)
            return;

        List<IdleOwner> basicTargetList = FindAttackAbleList(activeSkill);
        TargetCondition(basicTargetList);
        if (realTargetList.Count == 0)
        {
            StartCoroutine(NewMoveTarget());
            return;
        }

        if (_isStun || (_isSilence && skillSlotType != ESkillSlotType.Damage))
        {
            ChangeState(State_Idle.Instance);
            return;
        }

        if (skillSlotType == ESkillSlotType.Active)
            StartCoroutine(NewActiveTarget());
        else
            NewAttackTarget();
    }

    /// <summary>
    /// 마지막 타격 후 2번 효과(추가 데미지 / 버프 등)를 적용한다.
    /// 타겟 타입이 1번 효과와 다르면 타겟을 다시 고르고, 같으면 1번 효과의 타겟을 그대로 쓴다.
    /// </summary>
    private void NewFindTarget2()
    {
        if (activeSkill == null)
            return;

        if (activeSkill.TargetType2 == ETargetType.None)
            return;
        
        if(activeSkill.TargetType1 != activeSkill.TargetType2)
        {
            List<IdleOwner> basicTargetList = FindAttackAbleList(activeSkill, 2);
            TargetCondition(basicTargetList, 2);
        }
        for (int i = 0; i < realTargetList.Count; i++)
        {
            IdleOwner target = realTargetList[i];
            HitPoint(target, skillSlotType, 2);
        }
    }
    /// <summary>일반 공격의 마지막 타격 후, 장비에 붙은 버프를 확률(만분율)에 따라 타겟에 건다.</summary>
    private void ActiveEquipBuff()
    {
        for (int i = 0; i < realTargetList.Count; i++)
        {
            IdleOwner target = realTargetList[i];
            for(int j = 0; j < _equipBuff.Count; j++)
            {
                BuffInfo buffInfo = _equipBuff[j];

                int buffRandom = random.Next(0, 10000);
                if (buffInfo.EffectRate >= buffRandom)
                    target.AddSkillBuff(buffInfo, this);
            }
        }
    }
    /// <summary>
    /// 스킬 타겟 타입에 맞는 후보 목록. 자신 / 아군 전체 / 적군 전체이거나,
    /// 그 외에는 현재 위치 ± 사거리(AtkRange) 칸 안에 있는 아군 또는 적군 유닛.
    /// </summary>
    private List<IdleOwner> FindAttackAbleList(CharacterSkillDataRow skillData, int effect = 1)
    {
        List<IdleOwner> list = new List<IdleOwner>();
        ETargetType targetType = effect == 1 ? skillData.TargetType1 : skillData.TargetType2;
        EOwnerType player = _ownerType == EOwnerType.Player ? EOwnerType.Player : EOwnerType.Enemy;
        EOwnerType enemy = _ownerType == EOwnerType.Player ? EOwnerType.Enemy : EOwnerType.Player;
        int maxRange = Mathf.Min(curPosNumber + skillData.AtkRange, MaxPosNumber);
        int minRange = Mathf.Max(curPosNumber - skillData.AtkRange, MinPosNumber);
        if (targetType == ETargetType.Ones)
            list.Add(this);
        else if (targetType == ETargetType.OurForce)
            list = idleGameMgr.posObserver.GetIdleOwners(player);
        else if (targetType == ETargetType.EnemyAll)
            list = idleGameMgr.posObserver.GetIdleOwners(enemy);
        else
        {
            for (int i = 1; i <= MaxLineNumber; ++i)
            {
                for (int j = minRange; j <= maxRange; ++j)
                {
                    UnitPosition unitPos = idleGameMgr.posObserver.GetOccupyInfo(i, j);
                    if (unitPos != null)
                    {
                        for (int ow = 0; ow < unitPos.OccupyOwner.Count; ++ow)
                        {
                            IdleOwner owner = unitPos.OccupyOwner[ow];
                            if (owner == null)
                                continue;
                            if (!list.Contains(owner) && !owner._isDie)
                            {
                                if ((int)targetType >= (int)ETargetType.OurForceFMB && (int)targetType <= (int)ETargetType.OurForceBMF)
                                {
                                    if (player == owner._ownerType)
                                    {
                                        list.Add(owner);
                                    }
                                }
                                else if ((int)targetType >= (int)ETargetType.EnemyFMB && (int)targetType <= (int)ETargetType.EnemyBMF)
                                {
                                    if (enemy == owner._ownerType)
                                    {
                                        list.Add(owner);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        return list;
    }
    /// <summary>
    /// 패시브 발동 조건 확인. 사거리 안에 살아 있는 유닛이 하나라도 있을 때만 조건을 본다.
    /// - Enemy / PlayerCondition: 타겟 후보 중 해당 상태(보호막, 스턴, 침묵, 도발, 무적, 도트)를 가진 유닛이 있으면 발동
    /// - Damage: 바로 발동
    /// - GameStart: 아직 발동하지 않았으면 발동
    /// - PlayerMaxHp_Per: 아직 발동하지 않았고, 체력 비율이 기준(만분율) 이하면 발동
    /// </summary>
    bool SkillActiveAbleList(ESkillSlotType slotType)
    {
        if (!_skillsDic.ContainsKey(ESkillSlotType.Passive))
            return false;

        CharacterSkillDataRow skillDataRow = _skillsDic[slotType];
        if (!HasLivingUnitInRange(skillDataRow.AtkRange))
            return false;

        switch (skillDataRow.Type)
        {
            case ESkillType.EnemyCondition:
            case ESkillType.PlayerCondition:
                List<IdleOwner> basicTargetList = FindAttackAbleList(_skillsDic[ESkillSlotType.Passive]);
                EEffect condition = (EEffect)skillDataRow.TypeDetail;
                return basicTargetList.Exists(target => HasCondition(target, condition));

            case ESkillType.Damage:
                return true;

            case ESkillType.GameStart:
                return slotType == ESkillSlotType.Passive && !activePassiveSkill;

            case ESkillType.PlayerMaxHp_Per:
                if (slotType == ESkillSlotType.Passive && activePassiveSkill)
                    return false;
                return GetHpRate() <= skillDataRow.TypeDetail * 0.0001f;
        }
        return false;
    }

    /// <summary>현재 위치 ± range 칸 안(라인 1~3 전체)에 살아 있는 유닛이 있는지</summary>
    private bool HasLivingUnitInRange(int range)
    {
        int maxRange = Mathf.Min(curPosNumber + range, MaxPosNumber);
        int minRange = Mathf.Max(curPosNumber - range, MinPosNumber);
        for (int line = 1; line <= MaxLineNumber; ++line)
        {
            for (int pos = minRange; pos <= maxRange; ++pos)
            {
                UnitPosition unitPos = idleGameMgr.posObserver.GetOccupyInfo(line, pos);
                if (unitPos == null)
                    continue;

                for (int ow = 0; ow < unitPos.OccupyOwner.Count; ++ow)
                {
                    IdleOwner owner = unitPos.OccupyOwner[ow];
                    if (owner != null && !owner._isDie)
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>대상이 해당 상태(보호막 / 스턴 / 침묵 / 도발 / 무적 / 그 외 버프·도트)를 가지고 있는지</summary>
    private bool HasCondition(IdleOwner target, EEffect effect)
    {
        switch (effect)
        {
            case EEffect.ShieldType_Num: return target._shieldTypeAmount > 0;
            case EEffect.ShieldType_Hp: return target._shieldTypeHp > 0;
            case EEffect.Stun: return target.IsStun;
            case EEffect.Silence: return target._isSilence;
            case EEffect.Provocation: return target._isProvocation;
            case EEffect.Invincibility: return target.IsInvincibility;
            default: return target.ExistBuff(effect);
        }
    }
    /// <summary>
    /// 사거리 안에 타겟이 없을 때 적 방향으로 한 칸 전진한다. (아군은 위치 +1, 적군은 위치 -1)
    /// 앞 칸을 같은 편이 차지하고 있으면 빌 때까지 기다리고, 스킬 연출 / 스턴 중에는 멈춘다.
    /// 도착하면 다시 타겟을 찾는다.
    /// </summary>
    public IEnumerator NewMoveTarget()
    {
        ChangeState(State_Move.Instance);
        int endNum;
        if (GetOwnerType() == EOwnerType.Player)
            endNum = Mathf.Min(curPosNumber + 1, MaxPosNumber - 1);
        else
            endNum = Mathf.Max(curPosNumber - 1, MinPosNumber);

        // 이미 끝 칸이면 더 갈 곳이 없다
        if (curPosNumber == endNum)
            yield break;

        UnitPosition unitPos = idleGameMgr.posObserver.GetOccupyInfo(_lineNumber, endNum);

        if (unitPos.isOccupy && unitPos.GetOwnerType() == GetOwnerType())
            yield return new WaitUntil(() => unitPos.isOccupy == false || unitPos.GetOwnerType() != GetOwnerType());

        Vector3 endPos = new Vector3(unitPos.transform.position.x, this.transform.position.y, this.transform.position.z);
        while (true)
        {
            if (GetDie || idleGameMgr.isBattleEnd)
                yield break;

            // 이동을 시작하면 다음 칸을 바로 점유해서 뒤따르는 아군과 겹치지 않게 한다
            idleGameMgr.posObserver.RemoveOccupy(_lineNumber, curPosNumber, this);
            idleGameMgr.posObserver.SetOccupy(_lineNumber, endNum, this);

            if (idleGameMgr.isPlaying && !idleGameMgr.isUsingSkill && !_isStun)
            {
                this.transform.position = Vector3.MoveTowards(this.transform.position, endPos, _moveSpeed * Time.fixedDeltaTime);
                if (this.transform.position == endPos)
                {
                    curPosNumber = endNum;
                    NewFindTarget();
                    SetDepth();
                    yield break;
                }
            }
            yield return new WaitForFixedUpdate();
        }
    }

    /// <summary>일반 공격 / 패시브 시작: 타겟별 치명타 / 회피를 미리 굴리고 공격 이펙트를 낸 뒤 해당 상태로 전환한다.</summary>
    public void NewAttackTarget()
    {
        if (_isDie)
        {
            StopAllCoroutines();
            return;
        }

        for (int i = 0; i < realTargetList.Count; i++)
        {
            IdleOwner target = realTargetList[i];
            if (target._isDie)
                continue;

            RollCriticalAndDodge(target);
            SetAttackEffect(target);
        }

        if (skillSlotType == ESkillSlotType.Damage)
            ChangeState(State_Attack.Instance);
        else if (skillSlotType == ESkillSlotType.Passive)
            ChangeState(State_Passive.Instance);
    }

    /// <summary>
    /// 치명타 / 회피 판정 (만분율, 시드 고정 난수).
    /// 같은 시드에서 같은 결과가 나오도록 난수 호출 순서(치명타 → 회피)를 유지한다.
    /// </summary>
    private void RollCriticalAndDodge(IdleOwner target)
    {
        int criticalRate = (int)(GetCriticalRate() * 10000);
        criticalState[target.SlotPos] = criticalRate >= random.Next(0, 10000);

        int dodgeRate = (int)(target.GetDodgeRate() * 10000);
        dodgeState[target.SlotPos] = dodgeRate >= random.Next(0, 10000);
    }

    /// <summary>
    /// 액티브 스킬(필살기) 발동. 전투를 멈추고 유닛 / 이펙트 클럭을 스킬 연출용으로 바꾼다.
    /// - 일반 배속: 스킬 컷신을 재생하고, 타격 / 종료 처리는 컷신 쪽에서 진행한다.
    /// - 스킵 배속: 컷신 없이 모든 타격을 즉시 처리하고 1초 뒤 전투를 재개한다.
    /// </summary>
    IEnumerator NewActiveTarget()
    {
        if (_isDie)
        {
            StopAllCoroutines();
            yield break;
        }

        if (activeSkill.NormalSkillMotionEffect == "0")
        {
            ChangeState(State_Idle.Instance);
            yield break;
        }

        for (int i = 0; i < realTargetList.Count; i++)
            RollCriticalAndDodge(realTargetList[i]);

        IdleGameManager.instance.GameStart(false);
        GlobalClockManager.Instance.SetUnitClock(true);
        GlobalClockManager.Instance.SetEffectClock(true);

        idleGameMgr.isUsingSkill = true;
        isUseSkill = true;

        if (Time.timeScale < DefineValue.SkipTimeScale)
        {
            SetTargetsIdle(realTargetList);

            GameObject cutSceneObject = PrefabLoadManager.Instance.GetSkillEffectObject(activeSkill.NormalSkillMotionEffect);
            idleGameMgr.SetCutscene(this, realTargetList, cutSceneObject);

            RemoveActiveSkillIcon();
            skillObserver.EndSkill(ESkillSlotType.Active);
            yield break;
        }

        // 스킵 배속: 연출 없이 즉시 처리
        RemoveActiveSkillIcon();
        skillObserver.EndSkill(ESkillSlotType.Active);

        List<IdleOwner> targetActors = new List<IdleOwner>(realTargetList);
        SetTargetsIdle(targetActors);

        for (int i = 0; i < MaxHitCount; i++)
            TargetHit();

        float time = 0.0f;
        while (time < 1.0f)
        {
            time += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        isUseSkill = false;
        IdleGameManager.instance.isUsingSkill = false;
        ChangeState(State_Idle.Instance);
        SetTargetsIdle(targetActors);

        IdleGameManager.instance.GameStart(true);

        // 스킬 사용 중에는 Die()가 보류되므로, 재개 후 체력이 0 이하인 타겟을 사망 처리한다
        for (int i = 0; i < targetActors.Count; ++i)
        {
            IdleOwner target = targetActors[i];
            if (target != null && target.Hp <= 0 && !target.GetDie)
                target.Die(this);
        }
        GlobalClockManager.Instance.SetUnitClock(false);
        GlobalClockManager.Instance.SetEffectClock(false);
    }

    private static void SetTargetsIdle(List<IdleOwner> targets)
    {
        for (int i = 0; i < targets.Count; ++i)
            targets[i].ChangeState(State_Idle.Instance);
    }

    /// <summary>스킬 사용(또는 사망)으로 SP 게이지를 비운다. 적군은 스킬 대기열에서 뺀다.</summary>
    public void RemoveActiveSkillIcon()
    {
        if (_ownerType != EOwnerType.Player)
        {
            idleGameMgr.RemoveSkillQueue(this);
            return;
        }

        UISlot slot = idleBattle._slots[SlotPos];
        if(slot != null)
        {
            slot.SetSpSlider(0);
            slot.ActiveSkill(false);
        }
    }
    /// <summary>
    /// 사망 처리. 액티브 스킬 연출 중에는 보류하고(연출이 끝난 뒤 다시 호출된다),
    /// 진형 칸을 비우고 슬롯 / 버프를 정리한 뒤 승패를 갱신한다.
    /// </summary>
    public void Die(IdleOwner _hitter)
    {
        hitter = _hitter;
        RemoveActiveSkillIcon();
        if (idleGameMgr.isUsingSkill)
            return;

        _isDie = true;
        HudReturnPool();
        idleGameMgr.posObserver.SetOccupy(_lineNumber, curPosNumber, null);
        if (_ownerType == EOwnerType.Player)
            idleBattle._slots[SlotPos].DefeatUnit();
        ChangeState(State_Die.Instance);
        idleGameMgr.RefreshGameStatus(this);
        RemoveAllBuffEffect();
    }

    /// <summary>스킬 컷신 종료 시 호출. 연출 중 보류된 타겟의 사망을 처리한다.</summary>
    public void EndCutScene()
    {
        for(int i = 0; i < realTargetList.Count; i++)
        {
            IdleOwner target = realTargetList[i];
            if (target.Hp <= 0 && !target.GetDie)
                target.Die(this);
        }
    }
    
    public void Hide()
    {
        gameObject.SetActive(false);
    }
    
    public void ChangeState(FSM_State<IdleOwner> state)
    {
        _state.ChangeState(state);
    }
    
    /// <summary>IdleGameManager가 FixedUpdate마다 호출: 쿨타임 → 스킬 선택 → 발사체 → 버프 → FSM 순서로 진행</summary>
    public void BattleUpdate()
    {
        skillObserver.SkillUpdate();
        Attack();
        ProjectileUpdate();
        BuffUpdate();
        if (_state != null && isActiveAndEnabled)
            _state.Update();
    }

    private void ProjectileUpdate()
    {
        // 명중한 발사체는 리스트에서 빠지므로 뒤에서부터 순회
        for (int i = projectileList.Count - 1; i >= 0; i--)
        {
            if (i > projectileList.Count - 1)
                return;

            ProjectileSetting projectile = projectileList[i];
            if (projectile != null)
                projectile.ProjecTileUpdate();
        }
    }

    public Vector3 GetCurPosition()
    {
        return this.transform.position;
    }
    /// <summary>
    /// Spine "Hit" 이벤트마다 호출: 타겟별로 즉시 판정하거나 발사체를 만든다.
    /// 마지막 타격이면 일반 공격은 장비 버프를, 스킬은 2번 효과를 적용한다.
    /// </summary>
    public override void TargetHit()
    {
        for (int i = 0; i < realTargetList.Count;++i)
        {
            if (activeSkill.Projectile == "0")
                HitPoint(realTargetList[i], skillSlotType);
            else
                ProjectileCreate(realTargetList[i]);
            base.TargetHit();
        }
        if (hitCount == MaxHitCount)
        {
            if(skillSlotType == ESkillSlotType.Damage)
                ActiveEquipBuff();
            else
                NewFindTarget2();
        }
        hitCount += 1;
    }

    /// <summary>발사체 이펙트를 만들어 타겟에게 날린다. 명중 시 ProjectileSetting이 HitPoint를 호출한다.</summary>
    void ProjectileCreate(IdleOwner target)
    {
        if (target.GetDie)
            return;
        
        ProjectileDataRow projectileParam = regulation.projectileDtbl[activeSkill.Projectile];
        GameObject projectile = PrefabLoadManager.Instance.GetSkillEffectObject(projectileParam.NormalProjectileEffect);

        if (projectile == null)
            return;

        projectile.transform.parent = idleGameMgr.EffectRoot.transform;
        SetProjecTileEffectScale(projectile, 100, target.GetDepth());
        ProjectileSetting projectileSetting = projectile.GetComponent<ProjectileSetting>();
        if (projectileSetting == null)
            projectileSetting = projectile.AddComponent<ProjectileSetting>();
        projectileSetting.CreateProjectile(this, target, skillSlotType, projectileParam);
        projectileList.Add(projectileSetting);
    }

    /// <summary>
    /// 타겟 한 명에게 스킬 효과(1번 / 2번)를 적용한다.
    /// - 데미지: 미리 굴린 치명타 / 회피로 데미지를 계산해 적용 (회피 / 무적이면 0)
    /// - 그 외: BuffInfo를 만들어 확률(만분율)에 따라 버프 / 디버프를 건다
    /// 다단히트 스킬은 마지막 타격에서 총 데미지를 표시한다.
    /// </summary>
    public void HitPoint(IdleOwner target, ESkillSlotType skillSlotType, int effectNum = 1, bool isProjectile = false)
    {
        if (target == null)
            return;

        bool isFirst = effectNum == 1;
        EEffectType effectType = isFirst ? activeSkill.EffectType1 : activeSkill.EffectType2;
        if (effectType == EEffectType.None)
            return;

        if (effectType == EEffectType.Damage)
        {
            int slot = target.SlotPos;
            int attackVal = GetAttackDamage(skillSlotType, target, criticalState[slot], true, effectNum);
            if (dodgeState[slot] || target.IsInvincibility)
                attackVal = 0;

            Hit(target, attackVal, criticalState[slot]);
            attackValue[slot].Add(attackVal);
        }
        else
        {
            int skillLevel = _roCharacter.skillDataList[(int)skillSlotType].level;
            int effectRate = isFirst ? activeSkill.EffectRate1 : activeSkill.EffectRate2;
            int lvBonus = isFirst ? activeSkill.LvBonus1 : activeSkill.LvBonus2;

            BuffInfo info = new BuffInfo();
            info.EffectType = effectType;
            info.Effect = isFirst ? activeSkill.Effect1 : activeSkill.Effect2;
            info.EffectDetail = isFirst ? activeSkill.EffectDetail1 : activeSkill.EffectDetail2;
            // CC / 면역 / 면역 해제는 스킬 레벨이 오르면 발동 확률도 오른다
            bool rateScalesWithLevel = effectType == EEffectType.CC || effectType == EEffectType.Immune || effectType == EEffectType.ReleaseImmunity;
            info.EffectRate = rateScalesWithLevel ? effectRate + lvBonus * skillLevel : effectRate;
            info.EffectFigure = (isFirst ? activeSkill.EffectFigure1 : activeSkill.EffectFigure2) + lvBonus * skillLevel;
            info.Duration = isFirst ? activeSkill.Duration1 : activeSkill.Duration2;
            info.effectName = isFirst ? activeSkill.AddEffect1 : activeSkill.AddEffect2;
            info.EffectIcon = isFirst ? activeSkill.EffectIcon1 : activeSkill.EffectIcon2;
            info.EffectIcon1Atlas = isFirst ? activeSkill.EffectIcon1Atlas : activeSkill.EffectIcon2Atlas;

            if (info.EffectRate >= random.Next(0, 10000))
                target.AddSkillBuff(info, this);
        }

        // 다단히트 스킬은 모든 타격이 끝나면 총 데미지를 표시
        List<int> damages = attackValue[target.SlotPos];
        if (MaxHitCount > 1 && damages.Count == MaxHitCount)
        {
            int totalDamage = 0;
            for (int i = 0; i < damages.Count; i++)
                totalDamage += damages[i];
            target.SetTotalDamageBubble(totalDamage);
        }
    }

    public void Hit(IdleOwner target, int damage, bool isCritical)
    {
        DamageNode damageNode = new DamageNode();
        damageNode.damage = damage;
        damageNode.attackObject = this;
        damageNode.isCritical = isCritical;
        damageNode.teamType = _teamType;
        damageNode.attackRoCharacter = _roCharacter;
        target.Damaged(damageNode);

        if (idleBattle != null)
            idleBattle.UpdateHpProgress();
    }

    public override void Damaged(DamageNode damageNode)
    {
        base.Damaged(damageNode);
        SetDamage(damageNode.attackObject as IdleOwner);
    }

    /// <summary>피격 후처리: HP 표시 갱신, 피격 상태 전환, 사망 판정, 피격 이펙트</summary>
    private void SetDamage(IdleOwner attacker)
    {
        Player player = this as Player;
        if (player != null)
        {
            player.SetHp();
            player.TargetChangeHitState();
            if (Hp <= 0 && !GetDie)
                Die(attacker);
        }
        SetSingleTargetDamageEffect();
    }

    /// <summary>
    /// 매 틱 사용할 스킬을 고른다. 스킬 연출 중이 아니고 Idle / 피격 상태일 때만 새 행동을 시작한다.
    /// 우선순위: 액티브(침묵이 아닐 때) > 패시브(발동 조건 충족 시) > 일반 공격
    /// 스턴이면 행동하지 않고, 스턴 / 침묵이면 스킬 대기열에서 빠진다.
    /// </summary>
    private void Attack()
    {
        if (_isDie || idleGameMgr.isBattleEnd)
            return;

        if ((_isSilence || _isStun) && idleGameMgr.UseSkillOwner() == this)
            idleGameMgr.RemoveSkillQueue(this);

        if (_isStun)
        {
            if (_state.GetCurState() != State_Idle.Instance)
                ChangeState(State_Idle.Instance);
            return;
        }

        if (idleGameMgr.isUsingSkill)
            return;

        FSM_State<IdleOwner> curState = _state.GetCurState();
        if (curState != State_Idle.Instance && curState != State_Damage.Instance)
            return;

        hitCount = 1;
        if (useActiveSkill && !_isSilence)
        {
            StartSkill(ESkillSlotType.Active);
        }
        else if (usePassiveSkill && SkillActiveAbleList(ESkillSlotType.Passive) && !_isSilence)
        {
            StartSkill(ESkillSlotType.Passive);
            activePassiveSkill = true;
        }
        else if (isNormalAttack)
        {
            StartSkill(ESkillSlotType.Damage);
        }
    }

    /// <summary>스킬 슬롯을 정하고 타수(EffectDetail1, 최소 1)를 세팅한 뒤 타겟을 찾는다.</summary>
    private void StartSkill(ESkillSlotType slotType)
    {
        skillSlotType = slotType;
        ResetAttackValue();
        MaxHitCount = Mathf.Max(1, activeSkill.EffectDetail1);
        NewFindTarget();
    }
    /// <summary>피격 시 Idle / 피격 상태면 피격 모션으로 바꾸고 잠깐 붉게 깜빡인다. (스킬 사용 중에는 무시)</summary>
    private void TargetChangeHitState()
    {
        if (isUseSkill)
            return;
        if (Hp > 0 && !GetDie)
        {
            if (_state.GetCurState() == State_Idle.Instance || _state.GetCurState() == State_Damage.Instance)
                ChangeState(State_Damage.Instance);
            StartCoroutine(ChangeSpineColor());
        }
    }
    private IEnumerator ChangeSpineColor()
    {
        if(_skAnimation != null)
        {
            _skAnimation.skeleton.SetColor(Color.red);
            yield return new WaitForSeconds(0.2f);
            ResetColor();
        }            
    }
    public void Reset()
    {
        realTargetList.Clear();
        ResetColor();
    }

    public void ResetColor()
    {
        if(_skAnimation != null)
            _skAnimation.skeleton.SetColor(_originSpineColor);
    }
    public void SetAnimation(EOwnerState state)
    {
        switch (state)
        {
            case EOwnerState.None:
                _skAnimation.state.SetAnimation(0, IDLE, true);
                break;
            case EOwnerState.Move:
                _skAnimation.state.SetAnimation(0, MOVE, true);
                break;
            case EOwnerState.Attack:
                _skAnimation.state.SetAnimation(0, ATTACK, false);
                break;
            case EOwnerState.Skill:
                _skAnimation.state.SetAnimation(0, SKILL, false);
                break;
            case EOwnerState.Hit:
                _skAnimation.state.SetAnimation(0, HIT, false);
                break;
            case EOwnerState.Die:
                _skAnimation.state.SetAnimation(0, DIE, false);
                break;
            case EOwnerState.Win:
                _skAnimation.state.SetAnimation(0, WIN, false);
                break;
            case EOwnerState.Passive:
                _skAnimation.state.SetAnimation(0, PASSIVE, false);
                break;
        }
    }
    public EOwnerType GetOwnerType()
    {
        return _ownerType;
    }

    private void SetOwnerType(EOwnerType eOwnerType)
    {
        _ownerType = eOwnerType;
    }
    /// <summary>Spine 정렬 순서: 앞 라인일수록 위에 그리고, 아군 / 홀수 칸에 가중치를 더해 겹침을 정리한다.</summary>
    public void SetDepth()
    {
        if (_meshRenderer == null)
            return;
        
        int addDepth = 0;

        if (_ownerType == EOwnerType.Player)
            addDepth = 250;

        if (_curPosNumber % 2 == 1)
            addDepth += 500;

        if (_lineNumber == 1)
            _meshRenderer.sortingOrder = 3000;
        else if (_lineNumber == 2)
            _meshRenderer.sortingOrder = 2000;
        else if (_lineNumber == 3)
            _meshRenderer.sortingOrder = 1000;

        _meshRenderer.sortingOrder += addDepth;
    }

    /// <summary>머리 위에 데미지 / 회복 / Miss 말풍선을 띄운다. (월드 좌표 → NGUI 좌표 변환)</summary>
    protected override void SetDamageBubble(string poolName, int damageValue, bool isCritical, bool isHeal)
    {
        if (idleBattle == null)
            return;

        Vector3 screenPoint = Camera.main.WorldToScreenPoint(new Vector3(this.transform.position.x, this.transform.position.y + 2.45f, this.transform.position.z));
        Vector3 uiPos = UICamera.mainCamera.ScreenToWorldPoint(screenPoint);

        // 데미지 0은 Miss로 표시
        if (damageValue == 0)
        {
            poolName = MISS_DAMAGE_BUBBLE_NAME;
            isCritical = false;
        }

        GameObject bubble = PrefabLoadManager.Instance.GetDamageBubbleObject(poolName);
        if (bubble == null)
            return;

        bubble.SetActive(true);
        bubble.transform.parent = idleBattle.uiPool.transform;
        bubble.transform.localScale = Vector3.one;
        bubble.transform.position = new Vector3(uiPos.x, uiPos.y, 0);

        UIDamageBubble uiBubble = bubble.GetComponent<UIDamageBubble>();
        if (uiBubble != null)
        {
            string damageString = damageValue == 0 ? "" : damageValue.ToString();
            if (isHeal)
                damageString = string.Format("+{0}", damageString);
            uiBubble.PlayBubble(damageString, isCritical);
        }
    }

    /// <summary>다단히트 스킬의 총 데미지 말풍선</summary>
    private void SetTotalDamageBubble(int damageValue)
    {
        if (damageValue == 0 || idleBattle == null)
            return;

        Vector3 screenPoint = Camera.main.WorldToScreenPoint(new Vector3(this.transform.position.x, this.transform.position.y + 3.3f, this.transform.position.z));
        Vector3 uiPos = UICamera.mainCamera.ScreenToWorldPoint(screenPoint);

        GameObject bubble = PrefabLoadManager.Instance.GetDamageBubbleObject(TOTAL_DAMAGE_BUBBLE_NAME);
        if (bubble == null)
            return;

        bubble.SetActive(true);
        bubble.transform.parent = idleBattle.uiPool.transform;
        bubble.transform.localScale = Vector3.one;
        bubble.transform.position = new Vector3(uiPos.x, uiPos.y, 0);

        UIDamageBubble uiBubble = bubble.GetComponent<UIDamageBubble>();
        if (uiBubble != null)
            uiBubble.PlayTotalBubble(damageValue.ToString());
    }
    public int GetDepth()
    {
        if (_meshRenderer == null)
            return 0;
        return _meshRenderer.sortingOrder;
    }
    public string GetId()
    {
        if(_roCharacter != null)
            return _roCharacter.id;
        return null;
    }
    private void SetAttackEffect(IdleOwner target)
    {
        // 각성 상태에 따라 이펙트가 달라질 수 있어 이름을 따로 정한다
        effectName = activeSkill.NormalSkillMotionEffect;
        AttackEffect(target);
    }

    private void AttackEffect(IdleOwner target)
    {
        GameObject effect = PrefabLoadManager.Instance.GetSkillEffectObject(effectName);
        if (effect == null)
            return;

        effect.transform.parent = idleGameMgr.EffectRoot.transform;
        effect.transform.position = this.transform.position;
        SetEffectScale(effect, 50, GetDepth());
    }

    protected override void BuffEffect(BuffInfo buff)
    {
        GameObject effect = PrefabLoadManager.Instance.GetSkillEffectObject(buff.effectName);

        if (effect == null)
            return;

        effect.transform.position = this.transform.position;
        effect.transform.parent = this.transform;
        effectList.Add(buff, effect);
        bool isLooping = buff.EffectType == EEffectType.CC || buff.EffectType == EEffectType.Shild || buff.EffectType == EEffectType.Immune;
        SetEffectScale(effect, 1, GetDepth(), isLooping);
    }

    protected override void RemoveBuffEffect(BuffInfo buff)
    {
        if(effectList.ContainsKey(buff))
        {
            DestroyImmediate(effectList[buff]);
            effectList.Remove(buff);
        }
    }

    public void SetProjecTileEffectScale(GameObject effect, int addOrder, int targetOrder)
    {
        Vector3 scale;
        ParticleScaleController particleScale = PrepareParticleScale(effect, addOrder, targetOrder, out scale);
        particleScale.ShowProjecTile(scale);
    }

    private void SetEffectScale(GameObject effect, int addOrder, int targetOrder, bool isLooping = false)
    {
        Vector3 scale;
        ParticleScaleController particleScale = PrepareParticleScale(effect, addOrder, targetOrder, out scale);
        particleScale.Show(scale, isLooping);
    }

    /// <summary>파티클 정렬 순서를 맞추고, 캐릭터가 바라보는 방향(flipX)에 맞춘 스케일을 구한다.</summary>
    private ParticleScaleController PrepareParticleScale(GameObject effect, int addOrder, int targetOrder, out Vector3 scale)
    {
        ParticleScaleController particleScale = effect.GetComponent<ParticleScaleController>();
        if (particleScale == null)
            particleScale = effect.AddComponent<ParticleScaleController>();

        particleScale.SetOrder(addOrder, targetOrder);
        int flip = _skAnimation.skeleton.flipX ? -1 : 1;
        scale = new Vector3(this.transform.localScale.x * flip, this.transform.localScale.y, this.transform.localScale.z);
        return particleScale;
    }

    public void HudReturnPool()
    {
        if (_hud == null)
            return;
        _hud.RemoveBuff();

        DestroyImmediate(_hud.gameObject);
    }
    
    public EPosType GetPosType()
    {
        return posType;
    }
    public float GetHpRate()
    {
        return (float)Hp / (float)GetMaxHp;
    }
    public float GetHp()
    {
        return Hp;
    }
    public float GetAtkValue()
    {
        return _roCharacter.ResultAtk;
    }
    public float GetSpValue()
    {
        return _roCharacter.ResultSp;
    }
    public float GetAtkDef()
    {
        return _roCharacter.ResultAtkDef;
    }
    public float GetSpDef()
    {
        return _roCharacter.ResultSpDef;
    }
    public EElement GetElementType()
    {
        return _roCharacter.characterParamDataRow.Element;
    }
    /// <summary>
    /// 후보 목록을 정렬해 앞에서부터 TargetNumber명을 실제 타겟(realTargetList)으로 고른다.
    /// 정렬 우선순위: 도발한 상대 → 타겟 조건(거리 / 스탯 / 체력 / 상태) → 포지션 순서(전열 / 중열 / 후열) → 속성 상성
    /// </summary>
    public void TargetCondition(List<IdleOwner> targetList, int effect = 1)
    {
        realTargetList.Clear();
        bool isFirst = effect == 1;
        ETargetType targetType = isFirst ? activeSkill.TargetType1 : activeSkill.TargetType2;
        ETargetCondition targetCondition = isFirst ? activeSkill.TargetCondition1 : activeSkill.TargetCondition2;
        int targetConditionDetail = isFirst ? activeSkill.TargetConditionDetail1 : activeSkill.TargetConditionDetail2;
        int targetNumber = isFirst ? activeSkill.TargetNumber1 : activeSkill.TargetNumber2;

        if (targetList.Count > 1)
        {
            EPosType[] posOrder = GetPosOrder(targetType);
            targetList.Sort((target_1, target_2) =>
            {
                int state = CompareByProvocation(target_1, target_2);
                if (state != 0)
                    return state;

                state = CompareByCondition(target_1, target_2, targetCondition, targetConditionDetail);
                if (state != 0)
                    return state;

                state = CompareByPosOrder(target_1, target_2, posOrder);
                if (state != 0)
                    return state;

                return CompareByElement(target_1, target_2);
            });
        }

        int count = Mathf.Min(targetNumber, targetList.Count);
        for (int i = 0; i < count; ++i)
            realTargetList.Add(targetList[i]);
    }

    /// <summary>도발에 걸려 있으면 도발한 유닛을 가장 앞으로</summary>
    private int CompareByProvocation(IdleOwner target_1, IdleOwner target_2)
    {
        if (!_isProvocation)
            return 0;

        bool isProvoker1 = target_1.SlotPos == _provocationSlotNumber;
        bool isProvoker2 = target_2.SlotPos == _provocationSlotNumber;
        return isProvoker2.CompareTo(isProvoker1);
    }

    /// <summary>
    /// 스킬의 타겟 조건으로 비교한다.
    /// - 거리: detail 0이면 가까운 순. 위치 번호 기준이라 적군은 방향을 뒤집는다.
    /// - 스탯 / 체력: detail 0이면 높은 순, 1이면 낮은 순
    /// - 상태: 해당 상태를 가진 유닛을 앞으로
    /// </summary>
    private int CompareByCondition(IdleOwner target_1, IdleOwner target_2, ETargetCondition condition, int detail)
    {
        int order = detail == 0 ? -1 : 1;
        switch (condition)
        {
            case ETargetCondition.Distance:
                int distanceOrder = detail == 0 ? 1 : -1;
                if (_ownerType == EOwnerType.Enemy)
                    distanceOrder *= -1;
                return target_1.curPosNumber.CompareTo(target_2.curPosNumber) * distanceOrder;
            case ETargetCondition.Attack:
                return target_1.GetAtkValue().CompareTo(target_2.GetAtkValue()) * order;
            case ETargetCondition.Int:
                return target_1.GetSpValue().CompareTo(target_2.GetSpValue()) * order;
            case ETargetCondition.Defence:
                return target_1.GetAtkDef().CompareTo(target_2.GetAtkDef()) * order;
            case ETargetCondition.Resistance:
                return target_1.GetSpDef().CompareTo(target_2.GetSpDef()) * order;
            case ETargetCondition.Hp:
                return target_1.GetHp().CompareTo(target_2.GetHp()) * order;
            case ETargetCondition.HpRate:
                return target_1.GetHpRate().CompareTo(target_2.GetHpRate()) * order;
            case ETargetCondition.Condition:
                bool has1 = HasCondition(target_1, (EEffect)detail);
                bool has2 = HasCondition(target_2, (EEffect)detail);
                return has2.CompareTo(has1);
        }
        return 0;
    }

    /// <summary>타겟 타입의 포지션 우선순위(예: FMB = 전열 → 중열 → 후열). 포지션 지정이 없는 타입이면 null</summary>
    private static EPosType[] GetPosOrder(ETargetType targetType)
    {
        switch (targetType)
        {
            case ETargetType.OurForceFMB:
            case ETargetType.EnemyFMB:
                return new[] { EPosType.Front, EPosType.Middle, EPosType.Back };
            case ETargetType.OurForceFBM:
            case ETargetType.EnemyFBM:
                return new[] { EPosType.Front, EPosType.Back, EPosType.Middle };
            case ETargetType.OurForceMFB:
            case ETargetType.EnemyMFB:
                return new[] { EPosType.Middle, EPosType.Front, EPosType.Back };
            case ETargetType.OurForceMBF:
            case ETargetType.EnemyMBF:
                return new[] { EPosType.Middle, EPosType.Back, EPosType.Front };
            case ETargetType.OurForceBFM:
            case ETargetType.EnemyBFM:
                return new[] { EPosType.Back, EPosType.Front, EPosType.Middle };
            case ETargetType.OurForceBMF:
            case ETargetType.EnemyBMF:
                return new[] { EPosType.Back, EPosType.Middle, EPosType.Front };
        }
        return null;
    }

    private static int CompareByPosOrder(IdleOwner target_1, IdleOwner target_2, EPosType[] posOrder)
    {
        if (posOrder == null)
            return 0;

        int rank1 = System.Array.IndexOf(posOrder, target_1.GetPosType());
        int rank2 = System.Array.IndexOf(posOrder, target_2.GetPosType());
        if (rank1 < 0 || rank2 < 0)
            return 0;
        return rank1.CompareTo(rank2);
    }

    /// <summary>내가 유리한 속성(good)의 상대를 앞으로</summary>
    private int CompareByElement(IdleOwner target_1, IdleOwner target_2)
    {
        bool isGood1 = target_1.GetElementType() == good;
        bool isGood2 = target_2.GetElementType() == good;
        return isGood2.CompareTo(isGood1);
    }
    public StateMachine<IdleOwner> GetOwnerState()
    {
        return _state;
    }
    
    public Vector3 FindBonePosition(ECreateType createType)
    {
        if(_skAnimation != null)
        {
            switch(createType)
            {
                case ECreateType.None:
                    return this.transform.position;
                case ECreateType.Body:
                    return _skAnimation.skeleton.FindBone("bone").GetWorldPosition(this.transform);
            }
        }
        return transform.position;
    }
    /// <summary>단일 타겟 피격 이펙트: 현재 스킬의 피격 이펙트를 각 타겟 위치에 생성</summary>
    public void SetSingleTargetDamageEffect()
    {
        if (activeSkill == null || activeSkill.NormalSkillHitEffect == "0")
            return;

        string damageEffectName = activeSkill.NormalSkillHitEffect;
        for (int i = 0; i < realTargetList.Count; ++i)
            realTargetList[i].TakeDamageEffect(damageEffectName);
    }

    /// <summary>광역 피격 이펙트: 타겟들의 중심 위치에 한 번만 생성. 이름이 비어 있으면 스킬의 피격 이펙트를 쓴다.</summary>
    public void SetMultiTargetDamageEffect(string effectName)
    {
        string damageEffectName = effectName;
        if (string.IsNullOrEmpty(damageEffectName) && activeSkill != null && activeSkill.NormalSkillHitEffect != "0")
            damageEffectName = activeSkill.NormalSkillHitEffect;

        TakeMultiDamageEffect(damageEffectName, idleGameMgr.GetCenterPosition(realTargetList));
    }
    
    public void TakeDamageEffect(string effectName)
    {
        GameObject effect = PrefabLoadManager.Instance.GetSkillEffectObject(effectName);
        if (effect != null)
        {
            effect.transform.parent = idleGameMgr.EffectRoot.transform;
            effect.transform.position = this.transform.position;
            SetEffectScale(effect, 100, GetDepth());
        }
    }
    
    public void TakeMultiDamageEffect(string effectName, Vector3 position)
    {
        GameObject effect = PrefabLoadManager.Instance.GetSkillEffectObject(effectName);
        if (effect != null)
        {
            effect.transform.parent = idleGameMgr.EffectRoot.transform;
            effect.transform.position = position;
            SetEffectScale(effect, 100, GetDepth());
        }
    }

    /// <summary>전투 시작 시 호출: 게임 시작형 패시브 중 TypeDetail이 1인 것은 다시 발동할 수 있게 초기화한다.</summary>
    public void PassiveSkillCheck()
    {
        if (!_skillsDic.ContainsKey(ESkillSlotType.Passive))
            return;

        CharacterSkillDataRow skillDataRow = _skillsDic[ESkillSlotType.Passive];
        if (skillDataRow.Type == ESkillType.GameStart && skillDataRow.TypeDetail == 1)
            activePassiveSkill = false;
    }
    
    public void RemoveProjectile(ProjectileSetting projectile)
    {
        if (projectileList.Contains(projectile))
        {
            projectileList.Remove(projectile);
            DestroyImmediate(projectile.gameObject);
        }
    }

    public void RemoveAllProjectile()
    { 
        for (int i = projectileList.Count - 1; i >= 0; i--)
        {
            ProjectileSetting projectile = projectileList[i];
            RemoveProjectile(projectile);
        }
    }
    
    public void RemoveAllBuffEffect()
    {
        RemoveBuff_All();

        foreach (var effect in effectList.Values)
        {
            DestroyImmediate(effect);
        }
        effectList.Clear();
    }

    public void RemoveConditionBuffEffect()
    {
        RemoveBuff_Condition();
    }

    protected override void AddBuffIcon(BuffInfo buff)
    {
        base.AddBuffIcon(buff);
        if (_ownerType == EOwnerType.Player)
        {
            UISlot slot = idleBattle._slots[SlotPos];
            if (slot != null)
            {
                slot.AddBuff(buff);
            }
        }
        else
        {
            if(_hud != null)
                _hud.AddBuff(buff);
        }
    }

    protected override void RemoveBuffIcon(BuffInfo buff)
    {
        base.RemoveBuffIcon(buff);
        if (_ownerType == EOwnerType.Player)
        {
            UISlot slot = idleBattle._slots[SlotPos];
            if (slot != null)
                slot.RemoveBuff(buff);
        }
        else
        {
            if (_hud != null)
                _hud.RemoveBuff(buff);
        }
    }

    public void SilhouetteOn()
    {
        if (_meshRenderer != null)
            material.color = Color.black;

        for (int i = 0; i < realTargetList.Count; i++)
        {
            IdleOwner target = realTargetList[i];
            target.material.color = Color.black;
        }
    }

    public void SilhouetteOff()
    {
        if (_meshRenderer != null)
            material.color = Color.white;

        for (int i = 0; i < realTargetList.Count; i++)
        {
            IdleOwner target = realTargetList[i];
            target.material.color = Color.white;
        }
    }

    private void LateUpdate()
    {
        if (material != null && _meshRenderer != null)
            _meshRenderer.sharedMaterial = material;
    }

    /// <summary>배속(x2) 설정을 유지한 채 연출용 timeScale을 적용한다.</summary>
    public void AnimateTimeScale(float timeScale)
    {
        int baseTimeScale = idleGameMgr.isDubble ? 2 : 1;
        Time.timeScale = baseTimeScale * timeScale;
    }

    private void ResetAttackValue()
    {
        for (int i = 0; i < MaxSlotCount; i++)
            attackValue[i].Clear();
    }

    /// <summary>컷신 도중 호출: 아군이면 컷신을 멈추고 필살기 영상을 재생한다. (IsViewSkillAnimation으로 한 번만 처리)</summary>
    public void PlayMovie()
    {
        if (_ownerType == EOwnerType.Player)
        {
            if (IsViewSkillAnimation)
                return;

            IsViewSkillAnimation = true;
            idleGameMgr.StopCutScene(_roCharacter);
        }
    }

    private void OnDestroy()
    {
        if (_skAnimation != null)
        {
            _bone = null;
            _projectileBone = null;
            _skAnimation.skeletonDataAsset.Clear();
            _skAnimation.skeletonDataAsset = null;
            _skAnimation = null;
        }

        if(material != null)
        {
            DestroyImmediate(material);
            material = null;
        }
        _meshRenderer = null;
        _state = null;
        _targetList.Clear();
    }
}