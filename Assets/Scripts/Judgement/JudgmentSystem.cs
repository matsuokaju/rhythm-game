using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class JudgmentSystem : MonoBehaviour
{
    [Header("判定タイミング設定（ミリ秒）")]
    public float perfectTiming = 33.33f;
    public float goodTiming = 66.67f;
    public float badTiming = 100.0f;
    public float missTiming = 200.0f;

    public bool debugMode = true;

    // 依存関係
    private NoteManager noteManager;
    private ScoreManager scoreManager;
    private RhythmGameController gameController;
    private JudgmentAnimator judgmentAnimator;

    // 判定結果（デバッグログ用のみ）
    public string lastJudgment = "";
    public string lastTiming = "";
    public float judgmentDisplayTime = 0f;

    // ★ 前フレームのキー状態（ホールド監視用）
    private bool[] previousKeyStates = new bool[6];

    // イベント
    public System.Action<JudgmentResult, string> OnJudgment;

    void Awake()
    {
        noteManager = FindObjectOfType<NoteManager>();
        scoreManager = FindObjectOfType<ScoreManager>();
        gameController = FindObjectOfType<RhythmGameController>();
        judgmentAnimator = FindObjectOfType<JudgmentAnimator>();
    }

    void Update()
    {
        if (gameController.IsPlaying)
        {
            HandleInput();
        }

        if (judgmentDisplayTime > 0)
        {
            judgmentDisplayTime -= Time.deltaTime;
            if (judgmentDisplayTime <= 0)
            {
                lastJudgment = "";
                lastTiming = "";
            }
        }
    }

    void HandleInput()
    {
        KeyCode[] laneKeys = { KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.J, KeyCode.K, KeyCode.L };

        for (int i = 0; i < laneKeys.Length; i++)
        {
            bool currentKeyState = Input.GetKey(laneKeys[i]);

            // キー押下検出
            bool tapNoteProcessed = false;
            if (Input.GetKeyDown(laneKeys[i]))
            {
                tapNoteProcessed = CheckHit(i);
            }

            // ★ ホールドノートのキー監視（タップノートが処理されなかった場合のみ）
            if (!previousKeyStates[i] && currentKeyState && !tapNoteProcessed)
            {
                // キー押下
                if (debugMode) Debug.Log($"NotifyHoldKeyPress: Lane={i} (タップノート未処理のため)");
                NotifyHoldKeyPress(i);
            }
            else if (!previousKeyStates[i] && currentKeyState && tapNoteProcessed)
            {
                if (debugMode) Debug.Log($"ホールド通知スキップ: Lane={i} (タップノート処理済み)");
            }
            else if (previousKeyStates[i] && !currentKeyState)
            {
                // キー離し
                NotifyHoldKeyRelease(i);
            }

            // 前フレーム状態を保存
            previousKeyStates[i] = currentKeyState;
        }
    }

    bool CheckHit(int laneIndex)
    {
        // ★ 修正：ホールドノートは除外し、タップノートのみを対象にする
        var laneNotes = noteManager.ActiveNotes
            .Where(note => note.lane == laneIndex && 
                          note.gameObject != null && 
                          note.type != "hold") // ホールドノートを除外
            .OrderBy(note => note.hitTime) // 時系列順（先に来たノーツ優先）
            .ToList();

        if (debugMode)
            Debug.Log($"Key pressed: Lane={laneIndex} SongTime={gameController.SongTime:F3} ActiveNotes in lane={laneNotes.Count}");

        if (laneNotes.Count == 0)
        {
            if (debugMode) Debug.Log($"Lane {laneIndex}: ノーツなし");
            return false; // タップノートなし
        }

        // ★ 判定範囲内で最も早いノーツを探す
        ActiveNote targetNote = null;
        float targetTimeDifference = 0f;
        float targetTimeDifferenceMs = 0f;

        foreach (var note in laneNotes)
        {
            float timeDifference = note.hitTime - gameController.SongTime;
            float timeDifferenceMs = Mathf.Abs(timeDifference) * 1000f;

            // 判定範囲内かチェック
            if (timeDifferenceMs <= missTiming)
            {
                targetNote = note;
                targetTimeDifference = timeDifference;
                targetTimeDifferenceMs = timeDifferenceMs;
                break; // 時系列順なので最初に見つかったものが最も早い
            }
        }

        if (targetNote == null)
        {
            if (debugMode) Debug.Log($"Lane {laneIndex}: 判定範囲内のノーツなし");
            return false; // 判定範囲内にタップノートなし
        }

        if (debugMode)
        {
            float currentZ = targetNote.gameObject.transform.position.z;
            Debug.Log($"Hit Check - Lane={laneIndex} NoteHitTime={targetNote.hitTime:F3} SongTime={gameController.SongTime:F3} Diff={targetTimeDifference * 1000f:F1}ms Position={targetNote.position} NoteZ={currentZ:F2}");
        }

        JudgmentResult judgment = GetJudgment(targetTimeDifferenceMs);

        if (judgment != JudgmentResult.None)
        {
            ProcessHit(targetNote, judgment, targetTimeDifferenceMs, targetTimeDifference, laneIndex);
            if (debugMode) Debug.Log($"CheckHit完了: Lane={laneIndex} タップノート処理済み");
            return true; // タップノートを処理した
        }
        else if (debugMode)
        {
            Debug.Log($"Lane {laneIndex}: 判定範囲外 ({targetTimeDifferenceMs:F1}ms差) - Miss判定範囲: {missTiming}ms");
        }
        
        return false; // 判定範囲外
    }

    JudgmentResult GetJudgment(float timeDifferenceMs)
    {
        if (timeDifferenceMs <= perfectTiming) return JudgmentResult.Perfect;
        if (timeDifferenceMs <= goodTiming) return JudgmentResult.Good;
        if (timeDifferenceMs <= badTiming) return JudgmentResult.Bad;
        if (timeDifferenceMs <= missTiming) return JudgmentResult.Miss;
        return JudgmentResult.None;
    }

    void ProcessHit(ActiveNote note, JudgmentResult judgment, float timeDifferenceMs, float rawTimeDifference, int laneIndex)
    {
        // ★ ホールドノートはCheckHitで除外されているため、ここではタップノートのみが来る
        ProcessTapHit(note, judgment, timeDifferenceMs, rawTimeDifference, laneIndex);
    }

    // ★ タップノート処理
    void ProcessTapHit(ActiveNote note, JudgmentResult judgment, float timeDifferenceMs, float rawTimeDifference, int laneIndex)
    {
        // スコア処理
        scoreManager?.ProcessJudgment(judgment);

        // 判定文字列設定
        string judgmentText = GetJudgmentText(judgment);
        string timingText = GetTimingText(rawTimeDifference, judgment);

        // レーン上にアニメーション表示
        if (judgmentAnimator != null)
        {
            judgmentAnimator.PlayJudgmentAnimation(judgmentText, timingText, laneIndex);
        }

        judgmentDisplayTime = 1.0f;
        lastJudgment = judgmentText;
        lastTiming = timingText;

        // タップノートは削除
        noteManager.RemoveNote(note);

        // イベント発火
        OnJudgment?.Invoke(judgment, timingText);

        if (debugMode)
        {
            string timingStr = !string.IsNullOrEmpty(timingText) ? $" ({timingText})" : "";
            Debug.Log($"★ HIT! 判定: {judgment}{timingStr} at {note.position} (誤差: {timeDifferenceMs:F1}ms) Lane: {laneIndex} TimeDiff: {rawTimeDifference:F3}s");
        }
    }

    string GetJudgmentText(JudgmentResult judgment)
    {
        switch (judgment)
        {
            case JudgmentResult.Perfect: return "PERFECT!";
            case JudgmentResult.Good: return "GOOD";
            case JudgmentResult.Bad: return "BAD";
            case JudgmentResult.Miss: return "MISS";
            default: return "";
        }
    }

    string GetTimingText(float rawTimeDifference, JudgmentResult judgment)
    {
        if (judgment == JudgmentResult.Perfect)
        {
            return ""; // Perfectの場合は表示なし
        }
        else if (judgment != JudgmentResult.Miss)
        {
            // Good, Badの場合のタイミング表示
            if (rawTimeDifference > 0) return "FAST";
            else if (rawTimeDifference < 0) return "LATE";
        }
        else
        {
            // Missの場合
            if (rawTimeDifference > 0) return "FAST";
            else if (rawTimeDifference < 0) return "LATE";
        }
        return "";
    }

    // ★ ホールドキー監視
    void NotifyHoldKeyPress(int lane)
    {
        var holdNotes = noteManager.ActiveNotes.FindAll(n =>
            n.lane == lane &&
            n.type == "hold" &&
            n.gameObject != null);

        foreach (var holdNote in holdNotes)
        {
            var controller = holdNote.gameObject.GetComponent<HoldNoteController>();
            controller?.OnKeyPress();
        }
    }

    void NotifyHoldKeyRelease(int lane)
    {
        var holdNotes = noteManager.ActiveNotes.FindAll(n =>
            n.lane == lane &&
            n.type == "hold" &&
            n.gameObject != null);

        foreach (var holdNote in holdNotes)
        {
            var controller = holdNote.gameObject.GetComponent<HoldNoteController>();
            controller?.OnKeyRelease();
        }
    }

    public void CheckMissedNotes(float songTime)
    {
        for (int i = noteManager.ActiveNotes.Count - 1; i >= 0; i--)
        {
            ActiveNote note = noteManager.ActiveNotes[i];
            if (note.gameObject == null)
            {
                noteManager.ActiveNotes.RemoveAt(i);
                continue;
            }

            // ★ ホールドノートはオートミス対象外（HoldNoteController内で管理）
            if (note.type == "hold") continue;

            float timeDifference = songTime - note.hitTime;
            float timeDifferenceMs = timeDifference * 1000f;

            if (timeDifferenceMs > missTiming)
            {
                // オートミス処理（タイミング表示なし）
                scoreManager?.ProcessJudgment(JudgmentResult.Miss);
                lastJudgment = "MISS";
                lastTiming = ""; // オートミスはタイミング表示なし

                // オートミスもレーン上にアニメーション表示（タイミングなし）
                if (judgmentAnimator != null)
                {
                    judgmentAnimator.PlayJudgmentAnimation("MISS", "", note.lane);
                }

                if (debugMode) Debug.Log($"オートミス: {note.position} ({timeDifferenceMs:F1}ms経過) Lane: {note.lane}");

                noteManager.RemoveNote(note);
                i--; // インデックス調整
            }
        }
    }

    public void Initialize()
    {
        lastJudgment = "";
        lastTiming = "";
        judgmentDisplayTime = 0f;
        previousKeyStates = new bool[6];

        // アニメーションもクリア
        if (judgmentAnimator != null)
        {
            judgmentAnimator.ClearAllAnimations();
        }
    }
}