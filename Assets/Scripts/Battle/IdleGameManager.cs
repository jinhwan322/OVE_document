using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Shared.Regulation;
using Spine.Unity;
using Slate;
using Chronos;
using RenderHeads.Media.AVProVideo;

/// <summary>
/// 전투 진행 관리자. 프리코네 방식의 실시간 사이드 뷰 오토 배틀을 FixedUpdate 고정 틱으로 진행한다.
/// - 서버에서 받은 BattleData(덱 / 난수 시드)로 아군 / 적군 유닛을 만들고, 포지션별 배치표로 진형을 배정한다.
/// - 모든 유닛이 진형에 도착하면 전투를 시작하고, 매 틱 유닛들의 BattleUpdate를 호출한다.
/// - 액티브 스킬은 대기열로 한 번에 한 명씩 사용하며, 사용 중에는 전투를 멈추고 컷신(SLATE) / 영상(AVPro)을 재생한다.
/// - 한쪽이 전멸하거나 시간이 다 되면 전투를 끝낸다.
/// </summary>
public class IdleGameManager : MonoBehaviour
{
    private static IdleGameManager _instance = null;
    // Instance: 등록된 인스턴스만 반환 / instance: 없으면 씬에서 찾아서 반환
    public static IdleGameManager Instance { get { return _instance; } }
    private IdleGameManager() { }

    public static IdleGameManager instance
    {
        get
        {
            if (_instance == null)
                _instance = FindObjectOfType(typeof(IdleGameManager)) as IdleGameManager;
            return _instance;
        }
    }
    private BattleData _battleData = null;
    private int curWaveCount = 0;
    public Regulation regulation => idleBattleScene.fileLoad.regulation;

    [SerializeField]
    public S03_IdleBattle idleBattleScene;
    [SerializeField]
    private GameObject unitRoot;
    [SerializeField]
    private GameObject effectRoot;

    private List<Player> defenderList = new List<Player>();
    public List<Player> GetDefenderList => defenderList;
    public RoTroop curDefenderTroop => _battleData.defenderTroop[curWaveCount];

    private List<Player> attackerList = new List<Player>();
    public List<Player> GetAttackerList => attackerList;
    public RoTroop curAttackerTroop => _battleData.attackerTroop[0];
    public bool isBattleEnd = false;
    public bool isDubble = true;
    public bool isAuto = false;

    public UnitPositionObserver posObserver;
    private List<IdleOwner> skillQueue = new List<IdleOwner>();
    public CutsceneSetting cutsceneSetting;
    public const string BATTLE_UNIT = "BattleUnit";
    public const string SKILL_UNIT = "SkillUnit";
    public const string SKILL_EFFECT = "SkillEffect";
    public const string ACTIVE_SKILL_EFFECT = "ActiveSkillEffect";

    [HideInInspector]
    public bool isUsingSkill = false;
    public Shared.Battle.Random random;

    public float _gameTime;
    public float GetGameTime { get { return _gameTime; } }
    public bool isPlaying;
    bool isBattle;
    public UIIdleBattle idleBattleUI;
    public GameObject EffectRoot { get { return effectRoot; } }

    // 필살기 영상(AVPro) 재생용
    public GameObject mediaPlayerRoot;
    public DisplayIMGUI mediaPlayer;

    private void Start()
    {
        mediaPlayer.Player.Events.AddListener(OnMediaPlayerEvent);
    }

    private void OnDestroy()
    {
        _instance = null;
        mediaPlayer.Player.Events.RemoveListener(OnMediaPlayerEvent);
    }

    public void SetGame(BattleData battleData)
    {
        Init();        
        if (battleData == null)
            return;

        random = new Shared.Battle.Random(battleData.randomSeed);
        _battleData = battleData;

        posObserver.SetPositionning();           
        SetAttacker(curAttackerTroop);        
        SetDefender(curDefenderTroop);        

        TargetSetting();
        StartGame();
    }

    void InitData()
    {
        _gameTime = 0.0f;
        curWaveCount = 0;
        isBattle = false;
        GameStart(false);
        isBattleEnd = false;
        ActiveMovie(false);
    }

    private void Init()
    {
        InitData();
        skillQueue.Clear();
        posObserver.AllClear();
        defenderList.Clear();
        attackerList.Clear();
        ResetAttackerTargetList();
    }

    private void StartGame()
    {
        isBattle = true;
        PlayerAppearanceProcess();
        EnemyAppearanceProcess();
        StartCoroutine(InitAttack());
    }

    private void PlayerAppearanceProcess()
    {
        for (int i = 0; i < attackerList.Count; ++i)
        {
            if (!attackerList[i].GetDie)
            {
                StartCoroutine(attackerList[i].MoveInitPos());
            }
        }
    }

    private void EnemyAppearanceProcess()
    {
        for (int i = 0; i < defenderList.Count; ++i)
        {
            StartCoroutine(defenderList[i].MoveInitPos());
        }
    }

    /// <summary>모든 유닛이 진형 위치에 도착해 Idle이 될 때까지 기다린 뒤 스킬 관리자를 붙이고 전투를 시작한다.</summary>
    private IEnumerator InitAttack()
    {
        // 아군은 사망 상태도 도착한 것으로 본다
        while (!IsAllIdle(attackerList, true) || !IsAllIdle(defenderList, false))
            yield return new WaitForFixedUpdate();

        SetObserver(attackerList);
        SetObserver(defenderList);
        GameStart(true);
    }

    private static bool IsAllIdle(List<Player> units, bool includeDead)
    {
        if (units.Count == 0)
            return false;

        for (int i = 0; i < units.Count; ++i)
        {
            FSM_State<IdleOwner> state = units[i].GetOwnerState().GetCurState();
            if (state == State_Idle.Instance || (includeDead && state == State_Die.Instance))
                continue;
            return false;
        }
        return true;
    }

    /// <summary>유닛마다 스킬 쿨타임 관리자(SkillObserver)를 붙이고 초기화한다.</summary>
    private static void SetObserver(List<Player> units)
    {
        for (int i = 0; i < units.Count; ++i)
        {
            Player unit = units[i];
            if (unit.skillObserver == null)
                unit.skillObserver = unit.gameObject.AddComponent<SkillObserver>();

            unit.skillObserver.InitializeSkillObserver(unit);
            unit.PassiveSkillCheck();
        }
    }

    private void SetAttacker(RoTroop attacker)
    {
        for (int i = 0; i < attacker.characterList.Count; ++i)
            CreateUnitObject(attacker.characterList[i], attacker, EOwnerType.Player, i);

        AttackerReset();
    }

    private void SetDefender(RoTroop defender)
    {
        for (int i = 0; i < defender.characterList.Count; ++i)
            CreateUnitObject(defender.characterList[i], defender, EOwnerType.Enemy, i);

        DefenderReset();
    }

    private static bool IsAllDead(List<Player> units)
    {
        for (int i = 0; i < units.Count; ++i)
        {
            if (!units[i].GetDie)
                return false;
        }
        return true;
    }

    /// <summary>유닛이 죽을 때마다 호출: 한쪽이 전멸하면 전투를 끝낸다.</summary>
    public void RefreshGameStatus(IdleOwner idleOwner)
    {
        if (!isPlaying || isBattleEnd)
            return;

        if (IsAllDead(defenderList) || IsAllDead(attackerList))
            EndBattle();
    }

    private void EndBattle()
    {
        isBattle = false;
        isBattleEnd = true;
        StopAllAttack(true);
    }

    /// <summary>제한 시간 종료: 전투를 끝내고 유닛 / 이펙트를 정리한다.</summary>
    public void BattleTimeOver()
    {
        EndBattle();
        GameStart(false);
        Reset();
    }

    void Reset()
    {
        skillQueue.Clear();
        posObserver.AllClear();
        defenderList.Clear();
        attackerList.Clear();

        DestroyChildren(unitRoot.transform);
        DestroyChildren(effectRoot.transform);
    }

    private static void DestroyChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
            DestroyImmediate(root.GetChild(i).gameObject);
    }

    /// <summary>
    /// SD Spine 프리팹으로 전투 유닛을 만든다. 처음 쓰는 프리팹이면 Player 컴포넌트와 전용 머티리얼을 붙이고,
    /// Chronos 타임라인을 BattleUnit 글로벌 클럭에 연결한다. 적군은 좌우 반전하고 머리 위 HUD를 단다.
    /// </summary>
    private void CreateUnitObject(RoCharacter roCharacter, RoTroop troop, EOwnerType ownerType, int slotNum)
    {
        GameObject unitObj = PrefabLoadManager.Instance.GetSDSpineObject(roCharacter.id);
        if (unitObj == null)
            return;

        unitObj.transform.parent = unitRoot.transform;
        unitObj.SetActive(true);

        Player player = unitObj.GetComponent<Player>();
        if (player == null)
        {
            player = unitObj.AddComponent<Player>();
            player._meshRenderer = unitObj.GetComponent<MeshRenderer>();
            // 실루엣 연출 등에서 색을 바꾸므로 유닛마다 머티리얼을 복제해 쓴다
            Material nMaterial = new Material(player._meshRenderer.sharedMaterial);
            nMaterial.name = "NewMaterial";
            player.material = nMaterial;
            player._skAnimation = unitObj.GetComponent<SkeletonAnimation>();
        }

        AnimationKeyframeEvent keyframeEvent = player.GetComponentInChildren<AnimationKeyframeEvent>();
        if (keyframeEvent == null)
            keyframeEvent = player.GetAddComponent<AnimationKeyframeEvent>();

        // 유닛 시간은 BattleUnit 글로벌 클럭을 따른다 (스킬 연출 중 정지 / 배속 제어)
        player.timeline = unitObj.GetComponent<Timeline>();
        if (player.timeline == null)
        {
            player.timeline = player.gameObject.AddComponent<Timeline>();
            player.timeline.mode = TimelineMode.Global;
            player.timeline.globalClockKey = BATTLE_UNIT;
        }

        player.Create(roCharacter, ownerType, slotNum);
        troop.objectBase[slotNum] = player;
        if (ownerType == EOwnerType.Player)
        {
            attackerList.Add(player);
            player.SetPlayerSlot(player);
        }
        else
        {
            player._skAnimation.skeleton.flipX = true;
            player.SetHud();
            defenderList.Add(player);
        }
        keyframeEvent._objBase = player;
    }

    /// <summary>아군 진형을 배정하고 화면 왼쪽 밖(x = -10)에 라인별 높이로 세운다. 이후 MoveInitPos로 걸어 들어온다.</summary>
    private void AttackerReset()
    {
        ApplyFormation(attackerList, false);
        for (int i = 0; i < attackerList.Count; ++i)
        {
            Player attacker = attackerList[i];
            if (attacker.GetDie)
                continue;

            attacker.ResetColor();
            attacker.transform.position = new Vector3(-10, -4.3f + ((attacker._lineNumber - 1) * 0.58f), -3f + ((attacker._lineNumber - 1) * 0.73f));
        }
    }

    /// <summary>적군 진형을 배정하고 화면 오른쪽 밖(x = 10)에 라인별 높이로 세운다.</summary>
    private void DefenderReset()
    {
        ApplyFormation(defenderList, true);
        for (int i = 0; i < defenderList.Count; i++)
        {
            Player defender = defenderList[i];
            defender.transform.position = new Vector3(10, -4.3f + ((defender._lineNumber - 1) * 0.58f), -3f - ((defender._lineNumber + 1) * 0.73f));
        }
    }

    /// <summary>일시정지 / 재개. 재개할 때는 배속(x1 / x2)을 Root 클럭에 다시 적용한다.</summary>
    public void SetGamePause(bool isPause)
    {
        float timeValue = isPause ? 0f : (isDubble ? 2f : 1f);
        GlobalClockManager.Instance.SetRootClock(timeValue);
        GameStart(!isPause && !isUsingSkill);
    }

    public void SetGameTimeScale()
    {
        Time.timeScale = isDubble ? 2 : 1;
    }

    /// <summary>살아 있는 아군과 모든 적군을 서로의 타겟 목록에 등록한다.</summary>
    private void TargetSetting()
    {
        ResetAttackerTargetList();
        for (int i = 0; i < attackerList.Count; ++i)
        {
            if (attackerList[i].GetDie)
                continue;

            for (int e = 0; e < defenderList.Count; ++e)
            {
                attackerList[i]._targetList.Add(defenderList[e]);
                defenderList[e]._targetList.Add(attackerList[i]);
            }
        }
    }

    private void ResetAttackerTargetList()
    {
        for (int i = 0; i < attackerList.Count; ++i)
            attackerList[i]._targetList.Clear();
    }

    /// <summary>전투를 멈추고 모든 유닛의 이동 / 발사체 / 상태이상 버프를 정리한다.</summary>
    private void StopAllAttack(bool battleEnd)
    {
        GameStart(false);
        StopUnits(attackerList, battleEnd);
        StopUnits(defenderList, battleEnd);
    }

    private static void StopUnits(List<Player> units, bool battleEnd)
    {
        for (int i = 0; i < units.Count; ++i)
        {
            Player unit = units[i];
            unit.ResetColor();
            if (battleEnd)
                unit.StopAllCoroutines();

            if (unit._state.GetCurState() == State_Move.Instance)
                unit.ChangeState(State_Idle.Instance);

            unit.Reset();
            unit.RemoveAllProjectile();
            unit.RemoveConditionBuffEffect();
        }
    }

    #region 진형 배치
    /// <summary>진형 칸 (라인 1~3, 위치). 아군 기준 좌표이며 적군은 위치를 좌우 반전해 쓴다.</summary>
    private struct FormationSlot
    {
        public int line;
        public int pos;
        public FormationSlot(int line, int pos)
        {
            this.line = line;
            this.pos = pos;
        }
    }

    private static FormationSlot S(int line, int pos) { return new FormationSlot(line, pos); }

    // 적군 위치 = 13 - 아군 위치 (아군 6 <-> 적군 7이 맞닿는 최전방)
    private const int MirrorPosNumber = 13;

    // 포지션별 배치표: [인원수 - 1][순번] = (라인, 위치). 아군은 위치 6이 가장 앞, 1이 가장 뒤.
    private static readonly FormationSlot[][] FrontFormation =
    {
        new[] { S(2, 6) },
        new[] { S(2, 6), S(1, 5) },
        new[] { S(2, 6), S(1, 5), S(3, 5) },
        new[] { S(1, 5), S(1, 6), S(3, 5), S(3, 6) },
        new[] { S(1, 5), S(1, 6), S(3, 5), S(3, 6), S(2, 6) },
        new[] { S(1, 5), S(1, 6), S(3, 5), S(3, 6), S(2, 6), S(2, 5) },
    };

    private static readonly FormationSlot[][] MiddleFormation =
    {
        new[] { S(2, 3) },
        new[] { S(1, 3), S(3, 4) },
        new[] { S(1, 3), S(3, 3), S(2, 4) },
        new[] { S(3, 3), S(3, 4), S(1, 3), S(1, 4) },
        new[] { S(3, 3), S(3, 4), S(1, 3), S(1, 4), S(2, 3) },
        new[] { S(3, 3), S(3, 4), S(1, 3), S(1, 4), S(2, 4), S(2, 3) },
    };

    private static readonly FormationSlot[][] BackFormation =
    {
        new[] { S(2, 1) },
        new[] { S(1, 1), S(3, 2) },
        new[] { S(1, 1), S(3, 1), S(2, 2) },
        new[] { S(1, 1), S(1, 2), S(3, 1), S(3, 2) },
        new[] { S(1, 1), S(1, 2), S(3, 1), S(3, 2), S(2, 1) },
        new[] { S(1, 1), S(1, 2), S(3, 1), S(3, 2), S(2, 2), S(2, 1) },
    };

    /// <summary>
    /// 포지션(전열 / 중열 / 후열)별 인원수에 맞춰 라인과 위치를 배정하고 진형 칸을 점유한다.
    /// 아군은 위치 1~6, 적군은 같은 배치표를 좌우 반전한 7~12를 쓴다.
    /// </summary>
    private void ApplyFormation(List<Player> units, bool isEnemy)
    {
        List<Player> front = new List<Player>();
        List<Player> middle = new List<Player>();
        List<Player> back = new List<Player>();

        // 덱의 뒤쪽 슬롯부터 분류해 배치 순번을 정한다
        for (int i = units.Count - 1; i >= 0; i--)
        {
            switch (units[i].GetPosType())
            {
                case EPosType.Front: front.Add(units[i]); break;
                case EPosType.Middle: middle.Add(units[i]); break;
                case EPosType.Back: back.Add(units[i]); break;
            }
        }

        PlaceGroup(front, FrontFormation, isEnemy);
        PlaceGroup(middle, MiddleFormation, isEnemy);
        PlaceGroup(back, BackFormation, isEnemy);
    }

    private void PlaceGroup(List<Player> group, FormationSlot[][] table, bool isEnemy)
    {
        if (group.Count == 0 || group.Count > table.Length)
            return;

        FormationSlot[] slots = table[group.Count - 1];
        for (int i = 0; i < group.Count; ++i)
        {
            int pos = isEnemy ? MirrorPosNumber - slots[i].pos : slots[i].pos;
            group[i]._lineNumber = slots[i].line;
            group[i].curPosNumber = pos;
            posObserver.SetOccupy(slots[i].line, pos, group[i]);
        }
    }
    #endregion

    public bool GetBattleEndState()
    {
        return isBattleEnd;
    }

    #region 액티브 스킬 대기열
    /// <summary>
    /// 게이지가 찬 캐릭터를 대기열에 넣는다. 맨 앞 캐릭터만 스킬을 쓰고, 끝나면 다음 차례로 넘어간다. (아군 / 적군 공용)
    /// </summary>
    public void AddSkillQueue(IdleOwner owner)
    {
        if (!skillQueue.Contains(owner))
            skillQueue.Add(owner);

        RefreshSkillQueueIdx();
    }

    /// <summary>아군 슬롯에 스킬 대기 순번(1, 2, ...)을 표시한다.</summary>
    public void RefreshSkillQueueIdx()
    {
        int idx = 1;
        for (int i = 0; i < skillQueue.Count; i++)
        {
            if (skillQueue[i]._teamType != ETeamType.Attack)
                continue;

            idleBattleUI._slots[skillQueue[i].SlotPos].SetActiveSkillNumber(idx);
            idx++;
        }
    }

    /// <summary>지금 스킬을 쓸 차례인 캐릭터 (대기열 맨 앞)</summary>
    public IdleOwner UseSkillOwner()
    {
        return skillQueue.Count > 0 ? skillQueue[0] : null;
    }

    /// <summary>대기열에서 뺀다. owner가 null이면 맨 앞을 뺀다.</summary>
    public void RemoveSkillQueue(IdleOwner owner = null)
    {
        if (skillQueue.Count == 0)
            return;

        if (owner == null)
            owner = skillQueue[0];
        else if (!skillQueue.Contains(owner))
            return;

        if (owner._teamType == ETeamType.Attack)
            idleBattleUI._slots[owner.SlotPos].SetActiveSkillNumber(0);

        skillQueue.Remove(owner);
        RefreshSkillQueueIdx();
    }

    /// <summary>같은 편에서 액티브 스킬을 쓰고 있는 캐릭터가 없으면 true (편마다 한 번에 한 명만 사용)</summary>
    public bool IsPossibleUseSkill(EOwnerType ownerType)
    {
        List<Player> team = ownerType == EOwnerType.Player ? attackerList : defenderList;
        return !team.Exists(unit => !unit.GetDie && unit.useActiveSkill);
    }
    #endregion

    #region 스킬 연출 (SLATE 컷신 / AVPro 영상)
    /// <summary>시전자와 타겟으로 필살기 컷신(SLATE Cutscene)을 구성해 재생한다.</summary>
    public void SetCutscene(IdleOwner _mainActor, List<IdleOwner> targets, GameObject cutsceneObject)
    {
        cutsceneSetting.SetCutscene(_mainActor, targets, cutsceneObject);
    }

    /// <summary>
    /// 컷신을 잠시 멈추고 캐릭터 전용 필살기 영상(StreamingAssets/Video/{AnimationName}.mp4)을 재생한다.
    /// 영상이 없거나 열지 못하면 컷신을 바로 이어서 재생한다. 영상이 끝나면 OnMediaPlayerEvent에서 재개한다.
    /// </summary>
    public void StopCutScene(RoCharacter character)
    {
        StopCutScene();
        GlobalClockManager.Instance.SetActiveEffectClock(true);

        string aniName = character.characterParamDataRow.AnimationName;
        if (aniName == "0")
        {
            ResumeCutScene();
            return;
        }

        string path = string.Format("Video/{0}.mp4", aniName);
        if (mediaPlayer.Player.OpenMedia(MediaPathType.RelativeToStreamingAssetsFolder, path, false))
            mediaPlayer.Player.Play();
        else
            ResumeCutScene();
    }

    public void StopCutScene()
    {
        Cutscene cutscene = cutsceneSetting.GetCutscene();
        if (cutscene != null)
            cutscene.Pause();
    }

    /// <summary>영상 화면 표시 여부 (IMGUI 영역 크기를 0 / 1로 전환)</summary>
    void ActiveMovie(bool state)
    {
        float value = state ? 1f : 0f;
        mediaPlayerRoot.SetActive(state);
        mediaPlayer.AreaWidth = value;
        mediaPlayer.AreaHeight = value;
    }

    /// <summary>영상을 닫고 멈춰 둔 컷신을 이어서 재생한다.</summary>
    public void ResumeCutScene()
    {
        Cutscene cutscene = cutsceneSetting.GetCutscene();
        if (cutscene != null)
            cutscene.Resume();

        ActiveMovie(false);
        GlobalClockManager.Instance.SetActiveEffectClock(false);
    }
    #endregion

    /// <summary>타겟들의 화면상 중심(바운딩 박스 중앙). 광역 피격 이펙트 위치로 쓴다.</summary>
    public Vector3 GetCenterPosition(List<IdleOwner> targetList)
    {
        if (targetList == null || targetList.Count == 0)
            return Vector3.zero;

        Vector3 firstPos = targetList[0].transform.position;
        float minX = firstPos.x;
        float minY = firstPos.y;
        float maxX = firstPos.x;
        float maxY = firstPos.y;

        for (int i = 1; i < targetList.Count; ++i)
        {
            Vector3 pos = targetList[i].transform.position;
            minX = Mathf.Min(minX, pos.x);
            maxX = Mathf.Max(maxX, pos.x);
            minY = Mathf.Min(minY, pos.y);
            maxY = Mathf.Max(maxY, pos.y);
        }

        return new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0);
    }

    /// <summary>전투 틱 진행 여부. 스킬 연출 / 일시정지 중에는 false</summary>
    public void GameStart(bool state)
    {
        isPlaying = state;
    }

    /// <summary>
    /// 전투는 렌더링 프레임이 아닌 FixedUpdate 고정 틱으로 진행한다.
    /// 시드 고정 난수와 함께 써서, 배속이나 프레임 저하와 관계없이 같은 입력이면 같은 결과가 나온다.
    /// </summary>
    private void FixedUpdate()
    {
        if (isPlaying && isBattle)
        {
            _gameTime += Time.fixedDeltaTime;
            BattleUpdate();
        }
    }

    void BattleUpdate()
    {
        // 틱 도중 누군가 액티브 스킬을 쓰면 isPlaying이 꺼지므로 유닛마다 확인한다
        for (int i = 0; i < attackerList.Count; i++)
        {
            if (isPlaying)
                attackerList[i].BattleUpdate();
        }

        for (int i = 0; i < defenderList.Count; i++)
        {
            if (isPlaying)
                defenderList[i].BattleUpdate();
        }
    }

    // AVPro 영상 이벤트: 첫 프레임이 준비되면 영상 화면을 켜고, 재생이 끝나면 컷신을 재개한다
    private void OnMediaPlayerEvent(MediaPlayer mp, MediaPlayerEvent.EventType et, ErrorCode errorCode)
    {
        switch (et)
        {
            case MediaPlayerEvent.EventType.FirstFrameReady:
                ActiveMovie(true);
                break;
            case MediaPlayerEvent.EventType.FinishedPlaying:
                ResumeCutScene();
                break;
        }
    }
}