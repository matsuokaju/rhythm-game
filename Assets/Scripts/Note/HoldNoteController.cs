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
    public float[] judgmentTimes;   // 各判定タイミング（始点判定は含まない）
    public int currentJudgmentIndex = 0; // 現在の判定インデックス
    public int totalSegments;       // 総判定回数（始点判定は含まない）
    
    [Header("始点判定")]
    public bool startJudgmentCompleted = false; // 始点判定が完了したか
    public JudgmentResult startJudgmentResult = JudgmentResult.None; // 始点の判定結果

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
    
    // 既に調整済みの時刻が渡されているので、そのまま使用
    private float GetAdjustedStartTime() => startTime;
    private float GetAdjustedEndTime() => endTime;

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

        // 各区間の離し記録を初期化（totalSegments > 0の場合のみ）
        if (totalSegments > 0)
        {
            segmentWasReleased = new bool[totalSegments];
        }
        else
        {
            segmentWasReleased = new bool[0]; // 空の配列
        }

        Debug.Log($"HoldNote初期化: Lane={lane}, StartTime={startTime:F3}s, Duration={duration}拍, Segments={totalSegments}");
        Debug.Log($"  JudgeLineZ={judgeLineZ}, OriginalPos={originalPosition}, OriginalLength={originalLength}");
        Debug.Log($"  PreAdjustedStartTime={startTime:F3}s (already includes timing offset)");
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

        // 区間判定回数を計算（始点判定は含まない）
        // 0.5拍未満の場合は区間判定なし（始点判定のみ）
        if (duration < 0.5f)
        {
            totalSegments = 0;
            judgmentTimes = new float[0];
            Debug.Log($"短いホールド判定設定: Duration={duration}拍, 始点判定のみ");
        }
        else
        {
            // 0.5拍以上の場合は従来通り（duration拍 * 2 - 1）
            totalSegments = Mathf.RoundToInt(duration * 2f) - 1;
            judgmentTimes = new float[totalSegments];

            for (int i = 0; i < totalSegments; i++)
            {
                // 0.5拍目から開始（タイミング調整を考慮）
                judgmentTimes[i] = GetAdjustedStartTime() + (i + 1) * judgmentInterval;
            }
            Debug.Log($"通常ホールド判定設定: BPM={bpm}, Interval={judgmentInterval:F3}s, Segments={totalSegments}");
        }
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

        // ★修正：消去判定のロジック変更（タイミング調整を考慮）
        if (hasStarted)
        {
            // 一度でも押した場合は、調整されたendTimeで消す
            if (currentTime >= GetAdjustedEndTime())
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
        // ★ 修正：ホールドノートの下端（開始点）が判定線に来るタイミングでの位置計算（タイミング調整を考慮）
        float timeToStart = GetAdjustedStartTime() - currentTime;
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

        // if (Time.frameCount % 60 == 0) // デバッグ用
        // {
        //     Debug.Log($"Hold通常スクロール: Lane={lane} TimeToStart={timeToStart:F2} StartPointZ={startPointZ:F2} CenterZ={centerZ:F2}");
        // }
    }

    // ホールドノートの表示位置を動的更新（下端固定）
    void UpdateHoldVisualPosition(float currentTime)
    {
        // 上端の理想的なZ座標を計算（通常のスクロール）（タイミング調整を考慮）
        float timeToEnd = GetAdjustedEndTime() - currentTime;
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
        // ★ 修正：直接Input監視を削除し、JudgmentSystemからの通知のみで動作
        // キー状態の更新と判定処理は OnKeyPress/OnKeyRelease で処理される
    }

    // ★ 正常な開始
    private void StartHold()
    {
        hasStarted = true;
        currentState = HoldJudgmentState.Perfect;
        useNormalScroll = false; // 下端を判定ラインに固定
        
        Debug.Log($"ホールド開始: Lane={lane} at {songTime:F3}s, StartResult={startJudgmentResult}, useNormalScroll={useNormalScroll}");
        
        // ビジュアル更新
        UpdateVisuals();
    }
    
    // ★ 遅れての開始（途中から）
    private void StartHoldLate()
    {
        hasStarted = true;
        startWasMissed = true;
        currentState = HoldJudgmentState.Perfect;
        useNormalScroll = false; // 復帰時は下端を判定ラインに固定

        Debug.Log($"ホールド遅延開始: Lane={lane} at {songTime:F3}s (始点Miss後), useNormalScroll={useNormalScroll}");

        // 既に過ぎた区間は全てMissとして記録（安全性チェック）
        if (segmentWasReleased != null)
        {
            for (int i = 0; i < currentJudgmentIndex && i < segmentWasReleased.Length; i++)
            {
                segmentWasReleased[i] = true;
            }
        }

        // ビジュアル更新
        UpdateVisuals();
        
        Debug.Log($"Hold遅れて開始: Lane={lane} at {songTime:F3}s (過去{currentJudgmentIndex}区間をMiss として記録)");
    }

    void ProcessJudgments(float currentTime)
    {
        // 始点判定の自動処理（タイムアウト）
        if (!startJudgmentCompleted && currentTime >= GetAdjustedStartTime() + 0.2f) // 200ms後
        {
            startJudgmentResult = JudgmentResult.Miss;
            startJudgmentCompleted = true;
            ProcessStartJudgmentTimeout(startJudgmentResult, currentTime);
            Debug.Log($"Hold始点判定タイムアウト: Miss at Lane={lane}");
        }

        // 区間判定の処理（0.5拍以上の場合のみ）
        // 始点判定完了後は、ホールド開始していなくてもMiss判定を出す
        if (startJudgmentCompleted && totalSegments > 0)
        {
            while (currentJudgmentIndex < totalSegments &&
                   currentTime >= judgmentTimes[currentJudgmentIndex])
            {
                ProcessSingleJudgment(currentJudgmentIndex, currentTime);
                currentJudgmentIndex++;
            }
        }
    }

    void ProcessSingleJudgment(int judgmentIndex, float currentTime)
    {
        // 安全性チェック
        if (judgmentTimes == null || judgmentIndex >= judgmentTimes.Length)
        {
            Debug.LogError($"ProcessSingleJudgment: Invalid access - judgmentIndex={judgmentIndex}, array length={judgmentTimes?.Length ?? 0}");
            return;
        }
        
        float judgmentTime = judgmentTimes[judgmentIndex];

        Debug.Log($"判定{judgmentIndex + 1}/{totalSegments}: 時刻={judgmentTime:F3}s 押下={isCurrentlyPressed} 開始済み={hasStarted}");

        bool shouldBePerfect = false;

        if (!hasStarted)
        {
            // ホールドが開始していない場合は必ずMiss
            shouldBePerfect = false;
            Debug.Log($"Hold未開始のためMiss: 判定{judgmentIndex + 1} Lane={lane}");
        }
        else if (judgmentIndex == 0)
        {
            // 1回目の判定：開始猶予あり
            if (HasBeenPressedInStartPeriod(currentTime) && isCurrentlyPressed)
            {
                shouldBePerfect = true;
            }
        }
        else
        {
            // 2回目以降：現在押されていて、この区間で一度も離していない
            if (isCurrentlyPressed && 
                segmentWasReleased != null && judgmentIndex < segmentWasReleased.Length && 
                !segmentWasReleased[judgmentIndex])
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
            
            string reason = !hasStarted ? "未開始" : 
                           !isCurrentlyPressed ? "未押下" : 
                           (segmentWasReleased != null && judgmentIndex < segmentWasReleased.Length && segmentWasReleased[judgmentIndex]) ? "区間離し" : "その他";
            
            Debug.Log($"Hold MISS: {judgmentIndex + 1}回目 Lane={lane} (理由: {reason})");
        }
    }

    bool HasBeenPressedInStartPeriod(float currentTime)
    {
        // 開始猶予：調整されたstartTime - judgmentInterval から judgmentTimes[0] まで
        return currentTime >= GetAdjustedStartTime() - judgmentInterval;
    }

    // 始点のタップ判定を行う（fast/late情報も含む）
    (JudgmentResult, bool) EvaluateStartJudgmentWithTiming(float currentTime)
    {
        float startTime = GetAdjustedStartTime();
        float timeDifference = currentTime - startTime; // 符号を保持
        float timeDifferenceMs = Mathf.Abs(timeDifference) * 1000f;
        bool isLate = timeDifference > 0; // 正の値はlate

        // JudgmentSystemと同じ判定基準を使用
        if (timeDifferenceMs <= 33.33f) return (JudgmentResult.Perfect, isLate);
        if (timeDifferenceMs <= 66.67f) return (JudgmentResult.Good, isLate);
        if (timeDifferenceMs <= 100.0f) return (JudgmentResult.Bad, isLate);
        if (timeDifferenceMs <= 200.0f) return (JudgmentResult.Miss, isLate);
        return (JudgmentResult.None, isLate);
    }

    // 始点のタップ判定を行う（後方互換性のため）
    JudgmentResult EvaluateStartJudgment(float currentTime)
    {
        return EvaluateStartJudgmentWithTiming(currentTime).Item1;
    }

    // 始点判定の処理（キー押し時）
    void ProcessStartJudgment(JudgmentResult judgment, float currentTime)
    {
        // タイミング情報を取得
        var (judgmentResult, isLate) = EvaluateStartJudgmentWithTiming(currentTime);
        
        // スコア処理
        JudgmentType judgmentType = ConvertToJudgmentType(judgment);
        scoreManager?.AddScore(judgmentType, isHold: true);

        // アニメーション表示（fast/late情報を含む）
        string judgmentText = GetJudgmentText(judgment);
        string timingText = "";
        
        // Perfect以外でfast/late表示を追加
        if (judgment != JudgmentResult.Perfect)
        {
            timingText = isLate ? "LATE" : "FAST";
        }
        
        judgmentAnimator?.PlayJudgmentAnimation(judgmentText, timingText, lane);

        Debug.Log($"Hold始点判定: {judgment} {(isLate ? "LATE" : "FAST")} at Lane={lane} Time={currentTime:F3}s");
    }

    // 始点判定の処理（タイムアウト時）
    void ProcessStartJudgmentTimeout(JudgmentResult judgment, float currentTime)
    {
        // スコア処理
        JudgmentType judgmentType = ConvertToJudgmentType(judgment);
        scoreManager?.AddScore(judgmentType, isHold: true);

        // アニメーション表示（fast/late情報を含めない）
        string judgmentText = GetJudgmentText(judgment);
        string timingText = ""; // タイムアウト時はfast/late表示なし
        
        judgmentAnimator?.PlayJudgmentAnimation(judgmentText, timingText, lane);

        Debug.Log($"Hold始点判定タイムアウト: {judgment} at Lane={lane} Time={currentTime:F3}s");
    }

    // JudgmentResultをJudgmentTypeに変換
    JudgmentType ConvertToJudgmentType(JudgmentResult result)
    {
        switch (result)
        {
            case JudgmentResult.Perfect: return JudgmentType.Perfect;
            case JudgmentResult.Good: return JudgmentType.Good;
            case JudgmentResult.Bad: return JudgmentType.Bad;
            case JudgmentResult.Miss: return JudgmentType.Miss;
            default: return JudgmentType.Miss;
        }
    }

    // 判定結果をテキストに変換
    string GetJudgmentText(JudgmentResult result)
    {
        switch (result)
        {
            case JudgmentResult.Perfect: return "PERFECT!";
            case JudgmentResult.Good: return "GOOD";
            case JudgmentResult.Bad: return "BAD";
            case JudgmentResult.Miss: return "MISS";
            default: return "MISS";
        }
    }

    public void OnKeyPress()
    {
        Debug.Log($"Hold OnKeyPress: Lane={lane} at {songTime:F3}s (hasStarted={hasStarted}, startJudgmentCompleted={startJudgmentCompleted})");
        
        previousPressed = isCurrentlyPressed;
        isCurrentlyPressed = true;

        // ★ 押し始め検出（開始していない状態で押した場合）
        if (!previousPressed && isCurrentlyPressed && !hasStarted && !startJudgmentCompleted)
        {
            float currentTime = songTime;
            
            // 始点のタップ判定を実行
            startJudgmentResult = EvaluateStartJudgment(currentTime);
            
            if (startJudgmentResult != JudgmentResult.None)
            {
                startJudgmentCompleted = true;
                
                // スコア処理
                ProcessStartJudgment(startJudgmentResult, currentTime);
                
                // ホールド開始（Perfect/Good/Bad/Missに関係なく開始）
                StartHold();
            }
        }
        
        // ★ 始点をスルーした後の復帰処理
        if (!previousPressed && isCurrentlyPressed && !hasStarted && startJudgmentCompleted)
        {
            // 始点判定は既に完了している（Miss）が、遅れてキーが押された場合
            Debug.Log($"Hold遅延開始: Lane={lane} at {songTime:F3}s (始点Miss後の復帰)");
            StartHoldLate();
        }

        // 復帰検出
        if (!previousPressed && isCurrentlyPressed && hasStarted && currentState == HoldJudgmentState.Released)
        {
            currentState = HoldJudgmentState.Perfect;
            Debug.Log($"Hold復帰: Lane={lane} at {songTime:F3}s");
        }
    }

    public void OnKeyRelease()
    {
        Debug.Log($"Hold OnKeyRelease: Lane={lane} at {songTime:F3}s (hasStarted={hasStarted})");
        
        previousPressed = isCurrentlyPressed;
        isCurrentlyPressed = false;

        // 離し検出：前フレーム押していて今フレーム離している
        if (previousPressed && !isCurrentlyPressed && hasStarted)
        {
            wasEverReleased = true;
            currentState = HoldJudgmentState.Released;

            // 現在の区間に離しフラグを設定（安全性チェック）
            if (segmentWasReleased != null && currentJudgmentIndex < totalSegments && 
                currentJudgmentIndex < segmentWasReleased.Length)
            {
                segmentWasReleased[currentJudgmentIndex] = true;
            }

            Debug.Log($"Hold離し検出: Lane={lane} at {songTime:F3}s (区間{currentJudgmentIndex + 1})");
        }
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
            // ★修正：配列の安全性チェックを追加
            if (judgmentTimes != null && judgmentTimes.Length > 0 && currentTime >= judgmentTimes[0])
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

            float firstJudgeTime = (judgmentTimes != null && judgmentTimes.Length > 0) ? judgmentTimes[0] : startTime + 0.5f;
            // Debug.Log($"Hold視覚更新: Lane={lane} Phase={phase} Time={currentTime:F2} FirstJudge={firstJudgeTime:F2} Alpha={alpha:F1}");
        }
    }
}