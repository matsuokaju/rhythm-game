using UnityEngine;

public class HoldNoteController : MonoBehaviour
{
    [Header("ホールド設定")]
    public float startTime;
    public float endTime;
    public float duration;          // 拍数
    public float durationInSeconds; // 秒数
    public float holdLength;        // ワールド単位での長さ

    [Header("判定設定")]
    public float judgmentInterval;  // 判定間隔（秒）
    public float[] judgmentTimes;   // 各判定タイミング
    public int currentJudgmentIndex = 0; // 現在の判定インデックス
    public int totalSegments;       // 総判定回数

    [Header("状態")]
    public HoldJudgmentState currentState = HoldJudgmentState.NotStarted;
    public int perfectCount = 0;
    public int missCount = 0;
    public bool isCompleted = false;
    public bool hasStarted = false;
    public int lane = -1;

    [Header("押し直し管理")]
    public bool wasEverReleased = false;    // この区間で一度でも離したか
    public bool[] segmentWasReleased;       // 各区間で離したかの記録
    public bool isCurrentlyPressed = false; // 現在押されているか
    private bool previousPressed = false;   // 前フレームの押下状態
    public bool startWasMissed = false;     // 始点をミスったか

    [Header("表示制御")]
    public float originalLength;            // 元の長さ
    public Vector3 originalPosition;        // 元の位置
    public float judgeLineZ = 0f;          // 判定線のZ座標
    public float noteSpeed = 5f;           // ノーツ速度
    public bool useNormalScroll = true;     // 通常スクロールするか

    // 依存関係
    private RhythmGameController gameController;
    private ChartManager chartManager;
    private ScoreManager scoreManager;
    private JudgmentAnimator judgmentAnimator;
    private NoteManager noteManager;

    private float songTime => gameController?.SongTime ?? 0f;

    public enum HoldJudgmentState
    {
        NotStarted,   // まだ開始していない
        Perfect,      // 正常に長押し中
        Released,     // 離している（復帰可能）
        Completed     // 終了
    }

    void Awake()
    {
        gameController = FindObjectOfType<RhythmGameController>();
        chartManager = FindObjectOfType<ChartManager>();
        scoreManager = FindObjectOfType<ScoreManager>();
        judgmentAnimator = FindObjectOfType<JudgmentAnimator>();
        noteManager = FindObjectOfType<NoteManager>();
    }

    public void Initialize(float noteDuration, float noteDurationInSeconds, float noteHoldLength, float noteStartTime, int noteLane)
    {
        duration = noteDuration;
        durationInSeconds = noteDurationInSeconds;
        holdLength = noteHoldLength;
        startTime = noteStartTime;
        endTime = startTime + durationInSeconds;
        lane = noteLane;

        // 表示制御用の初期値を保存
        originalLength = holdLength;
        originalPosition = transform.position;
        judgeLineZ = noteManager?.judgeLineZ ?? 0f;
        noteSpeed = noteManager?.noteSpeed ?? 5f;
        useNormalScroll = true; // 初期は通常スクロール

        float currentBPM = chartManager?.CurrentBPM ?? 120f;
        CalculateJudgmentTimes(currentBPM);

        hasStarted = false;
        startWasMissed = false;
        currentState = HoldJudgmentState.NotStarted;
        perfectCount = 0;
        missCount = 0;
        currentJudgmentIndex = 0;
        wasEverReleased = false;
        isCurrentlyPressed = false;
        previousPressed = false;

        // 各区間の離し記録を初期化
        segmentWasReleased = new bool[totalSegments];

        Debug.Log($"HoldNote初期化: Lane={lane}, StartTime={startTime:F3}s, Duration={duration}拍, Segments={totalSegments}");
        Debug.Log($"  JudgeLineZ={judgeLineZ}, OriginalPos={originalPosition}, OriginalLength={originalLength}");
        for (int i = 0; i < judgmentTimes.Length; i++)
        {
            Debug.Log($"  判定{i + 1}: {judgmentTimes[i]:F3}s");
        }
    }

    void CalculateJudgmentTimes(float bpm)
    {
        // 0.5拍間隔で判定
        float beatInterval = 0.5f;
        judgmentInterval = (60f / bpm) * beatInterval;

        // 判定回数を正確に計算（duration拍 * 2 - 1）
        // 例：3拍なら 3 * 2 - 1 = 5回（0.5, 1.0, 1.5, 2.0, 2.5拍目）
        totalSegments = Mathf.RoundToInt(duration * 2f) - 1;
        judgmentTimes = new float[totalSegments];

        for (int i = 0; i < totalSegments; i++)
        {
            // 0.5拍目から開始
            judgmentTimes[i] = startTime + (i + 1) * judgmentInterval;
        }

        Debug.Log($"判定設定: BPM={bpm}, Interval={judgmentInterval:F3}s, Segments={totalSegments}");
    }

    void Update()
    {
        if (isCompleted) return;

        float currentTime = songTime;

        // キー状態の更新と離し検出
        UpdateKeyState();

        // 判定処理
        ProcessJudgments(currentTime);

        // 表示更新
        if (useNormalScroll)
            UpdateNormalScroll(currentTime);
        else if (hasStarted && !isCompleted)
            UpdateHoldVisualPosition(currentTime);

        // ★修正：消去判定のロジック変更
        if (hasStarted)
        {
            // 一度でも押した場合は、endTimeで消す（従来通り）
            if (currentTime >= endTime)
            {
                CompleteHold();
                return;
            }
        }
        else
        {
            // ★一度も押されなかった場合は、ノートが十分後ろに流れてから消す
            // ノートの上端位置を計算
            float noteTopZ = transform.position.z + (transform.localScale.y * 0.5f);

            // 判定ラインより十分後ろ（例：-3.0単位）に行ったら消す
            if (noteTopZ < judgeLineZ - 3.0f)
            {
                CompleteHold();
                return;
            }
        }

        UpdateVisuals();
    }

    // ★ 通常スクロール更新
    void UpdateNormalScroll(float currentTime)
    {
        // ★ 修正：ホールドノートの下端（開始点）が判定線に来るタイミングでの位置計算
        float timeToStart = startTime - currentTime;
        float startPointZ = judgeLineZ + timeToStart * noteSpeed;

        // ★ ホールドノートの中心位置を計算（下端基準）
        float centerZ = startPointZ + (originalLength * 0.5f);

        Vector3 newPosition = transform.position;
        newPosition.z = centerZ;
        transform.position = newPosition;

        // スケールは元のまま
        transform.localScale = new Vector3(
            transform.localScale.x,
            originalLength,
            transform.localScale.z
        );

        if (Time.frameCount % 60 == 0) // デバッグ用
        {
            Debug.Log($"Hold通常スクロール: Lane={lane} TimeToStart={timeToStart:F2} StartPointZ={startPointZ:F2} CenterZ={centerZ:F2}");
        }
    }

    // ホールドノートの表示位置を動的更新（下端固定）
    void UpdateHoldVisualPosition(float currentTime)
    {
        // 上端の理想的なZ座標を計算（通常のスクロール）
        float timeToEnd = endTime - currentTime;
        float topEndZ = judgeLineZ + timeToEnd * noteSpeed;

        // 下端は判定線に固定
        float bottomZ = judgeLineZ;

        // 新しい長さを計算
        float currentLength = topEndZ - bottomZ;
        currentLength = Mathf.Max(0.1f, currentLength); // 最小長さ保証

        // 新しい中心位置を計算（下端固定のため、中心は上に移動）
        float centerZ = bottomZ + currentLength * 0.5f;

        // 位置とスケールを更新
        Vector3 newPosition = transform.position;
        newPosition.z = centerZ;
        transform.position = newPosition;

        // Y軸スケール（長さ）を更新
        Vector3 newScale = transform.localScale;
        newScale.y = currentLength;
        transform.localScale = newScale;

        if (Time.frameCount % 30 == 0) // デバッグ用（30フレームごと）
        {
            Debug.Log($"Hold固定表示: Lane={lane} CenterZ={centerZ:F2} Length={currentLength:F2} TopZ={topEndZ:F2}");
        }
    }

    void UpdateKeyState()
    {
        previousPressed = isCurrentlyPressed;
        isCurrentlyPressed = CheckIfPressed();

        // ★ 押し始め検出（開始していない状態で押した場合）
        if (!previousPressed && isCurrentlyPressed && !hasStarted)
        {
            float currentTime = songTime;

            // 始点の猶予時間内なら開始
            if (currentTime >= startTime - judgmentInterval && currentTime <= judgmentTimes[0])
            {
                StartHold();
            }
            // 猶予時間を過ぎていても途中から開始可能
            else if (currentTime > judgmentTimes[0])
            {
                StartHoldLate();
            }
        }

        // 離し検出：前フレーム押していて今フレーム離している
        if (previousPressed && !isCurrentlyPressed && hasStarted)
        {
            wasEverReleased = true;
            currentState = HoldJudgmentState.Released;

            // 現在の区間に離しフラグを設定
            if (currentJudgmentIndex < totalSegments)
            {
                segmentWasReleased[currentJudgmentIndex] = true;
            }

            Debug.Log($"Hold離し検出: Lane={lane} at {songTime:F3}s (区間{currentJudgmentIndex + 1})");
        }

        // 復帰検出
        if (!previousPressed && isCurrentlyPressed && hasStarted && currentState == HoldJudgmentState.Released)
        {
            currentState = HoldJudgmentState.Perfect;
            Debug.Log($"Hold復帰: Lane={lane} at {songTime:F3}s");
        }
    }

    // ★ 正常な開始
    void StartHold()
    {
        hasStarted = true;
        useNormalScroll = false; // 固定表示に切り替え
        currentState = HoldJudgmentState.Perfect;
        Debug.Log($"Hold開始成功: Lane={lane} at {songTime:F3}s");
    }

    // ★ 遅れての開始（途中から）
    void StartHoldLate()
    {
        hasStarted = true;
        startWasMissed = true;
        useNormalScroll = false; // 固定表示に切り替え
        currentState = HoldJudgmentState.Perfect;

        // 既に過ぎた区間は全てMissとして記録
        for (int i = 0; i < currentJudgmentIndex; i++)
        {
            segmentWasReleased[i] = true;
        }

        Debug.Log($"Hold遅れて開始: Lane={lane} at {songTime:F3}s (過去{currentJudgmentIndex}区間をMissとして記録)");
    }

    void ProcessJudgments(float currentTime)
    {
        while (currentJudgmentIndex < totalSegments &&
               currentTime >= judgmentTimes[currentJudgmentIndex])
        {
            ProcessSingleJudgment(currentJudgmentIndex, currentTime);
            currentJudgmentIndex++;
        }
    }

    void ProcessSingleJudgment(int judgmentIndex, float currentTime)
    {
        float judgmentTime = judgmentTimes[judgmentIndex];

        Debug.Log($"判定{judgmentIndex + 1}/{totalSegments}: 時刻={judgmentTime:F3}s 押下={isCurrentlyPressed} 開始済み={hasStarted}");

        bool shouldBePerfect = false;

        if (judgmentIndex == 0)
        {
            // 1回目の判定：開始猶予あり
            if (HasBeenPressedInStartPeriod(currentTime) && isCurrentlyPressed)
            {
                shouldBePerfect = true;
                if (!hasStarted)
                {
                    StartHold();
                }
            }
        }
        else
        {
            // 2回目以降：現在押されていて、この区間で一度も離していない
            if (hasStarted && isCurrentlyPressed && !segmentWasReleased[judgmentIndex])
            {
                shouldBePerfect = true;
            }
        }

        // 判定確定
        if (shouldBePerfect)
        {
            perfectCount++;
            scoreManager?.AddScore(JudgmentType.Perfect, isHold: true);
            judgmentAnimator?.PlayJudgmentAnimation("PERFECT!", "", lane);
            Debug.Log($"Hold PERFECT: {judgmentIndex + 1}回目 Lane={lane}");
        }
        else
        {
            missCount++;
            scoreManager?.AddScore(JudgmentType.Miss, isHold: true);
            judgmentAnimator?.PlayJudgmentAnimation("MISS", "", lane);
            Debug.Log($"Hold MISS: {judgmentIndex + 1}回目 Lane={lane} (理由: 開始={hasStarted}, 押下={isCurrentlyPressed}, 区間離し={segmentWasReleased[judgmentIndex]})");
        }
    }

    bool HasBeenPressedInStartPeriod(float currentTime)
    {
        // 開始猶予：startTime - judgmentInterval から judgmentTimes[0] まで
        return currentTime >= startTime - judgmentInterval;
    }

    bool CheckIfPressed()
    {
        switch (lane)
        {
            case 0: return Input.GetKey(KeyCode.S);
            case 1: return Input.GetKey(KeyCode.D);
            case 2: return Input.GetKey(KeyCode.F);
            case 3: return Input.GetKey(KeyCode.J);
            case 4: return Input.GetKey(KeyCode.K);
            case 5: return Input.GetKey(KeyCode.L);
            default: return Input.GetKey(KeyCode.Space);
        }
    }

    public void OnKeyPress()
    {
        // UpdateKeyState()で処理するため、ここでは何もしない
    }

    public void OnKeyRelease()
    {
        // UpdateKeyState()で処理するため、ここでは何もしない
    }

    void CompleteHold()
    {
        isCompleted = true;
        currentState = HoldJudgmentState.Completed;

        Debug.Log($"Hold完了: Perfect={perfectCount}, Miss={missCount}, Total={totalSegments}");

        var activeNote = noteManager?.ActiveNotes.Find(n => n.gameObject == gameObject);
        if (activeNote != null)
        {
            noteManager.RemoveNote(activeNote);
        }
    }

    void UpdateVisuals()
    {
        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null) return;

        Color baseColor = Color.white;
        float alpha = 1.0f; // デフォルトは不透明

        float currentTime = songTime;

        bool shouldShowTransparent = false;

        if (!hasStarted)
        {
            // 一度も押されていない場合
            // ★修正：最初の判定タイミングが来てから半透明にする
            if (judgmentTimes.Length > 0 && currentTime >= judgmentTimes[0])
            {
                shouldShowTransparent = true;
            }
            // startTime <= currentTime < judgmentTimes[0] の間は透明度1を維持
        }
        else
        {
            // 一度でも押されたことがある場合
            bool inJudgmentPeriod = currentTime >= startTime && currentTime <= endTime;
            if (inJudgmentPeriod && !isCurrentlyPressed)
            {
                shouldShowTransparent = true;
            }
        }

        if (shouldShowTransparent)
        {
            alpha = 0.5f; // 半透明
        }

        // 透明度を適用
        baseColor.a = alpha;
        renderer.material.color = baseColor;

        if (Time.frameCount % 60 == 0) // デバッグ用
        {
            string phase = "";
            if (!hasStarted)
            {
                if (currentTime < startTime)
                    phase = "開始前";
                else if (judgmentTimes.Length > 0 && currentTime < judgmentTimes[0])
                    phase = "猶予期間";
                else
                    phase = "未押し";
            }
            else
            {
                phase = "開始済み";
            }

            Debug.Log($"Hold視覚更新: Lane={lane} Phase={phase} Time={currentTime:F2} FirstJudge={judgmentTimes[0]:F2} Alpha={alpha:F1}");
        }
    }
}