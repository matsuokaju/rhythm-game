using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    [Header("スコア設定")]
    public int perfectScore = 300;
    public int goodScore = 200;
    public int badScore = 50;

    private int score = 0;
    private int combo = 0;
    private int perfectCount = 0;
    private int goodCount = 0;
    private int badCount = 0;
    private int missCount = 0;

    // ★ ホールドノート専用カウンター
    private int holdPerfectCount = 0;
    private int holdMissCount = 0;

    // プロパティ
    public int Score => score;
    public int Combo => combo;
    public int PerfectCount => perfectCount;
    public int GoodCount => goodCount;
    public int BadCount => badCount;
    public int MissCount => missCount;
    public int HoldPerfectCount => holdPerfectCount;
    public int HoldMissCount => holdMissCount;

    public void Initialize()
    {
        score = 0;
        combo = 0;
        perfectCount = goodCount = badCount = missCount = 0;
        holdPerfectCount = holdMissCount = 0;
    }

    // 既存のタップノート判定処理
    public void ProcessJudgment(JudgmentResult judgment)
    {
        switch (judgment)
        {
            case JudgmentResult.Perfect:
                score += perfectScore;
                perfectCount++;
                combo++;
                break;
            case JudgmentResult.Good:
                score += goodScore;
                goodCount++;
                combo++;
                break;
            case JudgmentResult.Bad:
                score += badScore;
                badCount++;
                combo = 0;
                break;
            case JudgmentResult.Miss:
                missCount++;
                combo = 0;
                break;
        }
    }

    // ★ ホールドノート専用判定処理（1区間ごとに呼ばれる）
    public void ProcessHoldJudgment(JudgmentResult judgment)
    {
        switch (judgment)
        {
            case JudgmentResult.Perfect:
                score += perfectScore;  // タップのPerfectと同じ点数
                holdPerfectCount++;
                combo++;
                break;
            case JudgmentResult.Miss:
                holdMissCount++;
                combo = 0;
                break;
        }
    }

    // ★ 汎用的なスコア加算メソッド（HoldNoteControllerから呼ばれる）
    public void AddScore(JudgmentType judgmentType, bool isHold = false)
    {
        switch (judgmentType)
        {
            case JudgmentType.Perfect:
                score += perfectScore;
                if (isHold)
                {
                    holdPerfectCount++;
                }
                else
                {
                    perfectCount++;
                }
                combo++;
                break;

            case JudgmentType.Good:
                if (!isHold) // ホールドにはGood判定がない
                {
                    score += goodScore;
                    goodCount++;
                    combo++;
                }
                break;

            case JudgmentType.Bad:
                if (!isHold) // ホールドにはBad判定がない
                {
                    score += badScore;
                    badCount++;
                    combo = 0;
                }
                break;

            case JudgmentType.Miss:
                if (isHold)
                {
                    holdMissCount++;
                }
                else
                {
                    missCount++;
                }
                combo = 0;
                break;
        }
    }

    public void LogResults(string songTitle)
    {
        Debug.Log($"=== 結果 ===");
        Debug.Log($"楽曲: {songTitle}");
        Debug.Log($"Score: {score}");
        Debug.Log($"=== タップノート ===");
        Debug.Log($"Perfect: {perfectCount}, Good: {goodCount}, Bad: {badCount}, Miss: {missCount}");
        Debug.Log($"=== ホールドノート ===");
        Debug.Log($"Hold Perfect: {holdPerfectCount}, Hold Miss: {holdMissCount}");

        // 全体の正解率計算
        int totalTapNotes = perfectCount + goodCount + badCount + missCount;
        int totalHoldSegments = holdPerfectCount + holdMissCount;
        int totalJudgments = totalTapNotes + totalHoldSegments;

        if (totalJudgments > 0)
        {
            int successfulJudgments = perfectCount + goodCount + holdPerfectCount;
            float accuracy = (float)successfulJudgments / totalJudgments * 100f;
            Debug.Log($"正解率: {accuracy:F1}% ({successfulJudgments}/{totalJudgments})");
        }

        if (totalTapNotes > 0)
        {
            float tapAccuracy = (float)(perfectCount + goodCount) / totalTapNotes * 100f;
            Debug.Log($"タップ正解率: {tapAccuracy:F1}%");
        }

        if (totalHoldSegments > 0)
        {
            float holdAccuracy = (float)holdPerfectCount / totalHoldSegments * 100f;
            Debug.Log($"ホールド正解率: {holdAccuracy:F1}%");
        }
    }
}

// ★ 汎用判定タイプ（HoldNoteControllerで使用）
public enum JudgmentType
{
    Perfect,
    Good,
    Bad,
    Miss
}