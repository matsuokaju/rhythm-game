using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class JudgmentAnimation
{
    public string judgment;
    public string timing;
    public Vector2 position;
    public float startTime;
    public float duration;
    public float startY;
    public float targetY;

    public JudgmentAnimation(string judg, string tim, Vector2 pos, float dur = 1.0f)
    {
        judgment = judg;
        timing = tim;
        position = pos;
        startTime = Time.time;
        duration = dur;
        startY = pos.y;
        targetY = pos.y - 50f; // 50ピクセル上に移動
    }

    public Vector2 GetCurrentPosition()
    {
        float elapsed = Time.time - startTime;
        float t = elapsed / duration;

        if (t >= 1f) return new Vector2(position.x, targetY);

        // イージングアウト（だんだん遅くなる）
        float easedT = 1f - (1f - t) * (1f - t) * (1f - t);
        float currentY = Mathf.Lerp(startY, targetY, easedT);

        return new Vector2(position.x, currentY);
    }

    public float GetCurrentAlpha()
    {
        float elapsed = Time.time - startTime;
        float t = elapsed / duration;

        if (t >= 1f) return 0f;

        // 最後0.3秒でフェードアウト
        if (t > 0.7f)
        {
            float fadeT = (t - 0.7f) / 0.3f;
            return 1f - fadeT;
        }

        return 1f;
    }

    public bool IsFinished()
    {
        return Time.time - startTime >= duration;
    }
}

public class JudgmentAnimator : MonoBehaviour
{
    [Header("判定表示フォント設定")]
    public Font judgmentFont; // 判定表示専用フォント（PERFECT!, GOOD, BAD, MISS）
    public int judgmentFontSize = 20;
    public float judgmentYOffset = 0f; // 判定表示のY位置オフセット

    [Header("判定表示色設定")]
    public Color perfectColor = Color.yellow;
    public Color goodColor = Color.green;
    public Color badColor = Color.magenta;
    public Color missColor = Color.grey;

    [Header("タイミング表示フォント設定")]
    public Font timingFont; // タイミング表示専用フォント（FAST, LATE）
    public int timingFontSize = 16;
    public float timingYOffset = 25f; // タイミング表示のY位置オフセット（判定表示からの相対位置）

    [Header("タイミング表示色設定")]
    public Color fastColor = Color.blue;
    public Color lateColor = Color.red;

    private List<JudgmentAnimation> activeAnimations = new List<JudgmentAnimation>();
    private NoteManager noteManager;

    // GUI スタイル
    private GUIStyle judgmentStyle;
    private GUIStyle timingStyleFast;
    private GUIStyle timingStyleLate;

    void Awake()
    {
        noteManager = FindObjectOfType<NoteManager>();
    }

    void Start()
    {
        InitializeStyles();
    }

    void InitializeStyles()
    {
        // 判定表示用スタイル
        judgmentStyle = new GUIStyle();
        judgmentStyle.fontSize = judgmentFontSize;
        judgmentStyle.fontStyle = FontStyle.Bold;
        judgmentStyle.alignment = TextAnchor.MiddleCenter;
        if (judgmentFont != null) judgmentStyle.font = judgmentFont;

        // FAST表示用スタイル
        timingStyleFast = new GUIStyle();
        timingStyleFast.fontSize = timingFontSize;
        timingStyleFast.fontStyle = FontStyle.Bold;
        timingStyleFast.alignment = TextAnchor.MiddleCenter;
        timingStyleFast.normal.textColor = fastColor;
        if (timingFont != null) timingStyleFast.font = timingFont;

        // LATE表示用スタイル
        timingStyleLate = new GUIStyle();
        timingStyleLate.fontSize = timingFontSize;
        timingStyleLate.fontStyle = FontStyle.Bold;
        timingStyleLate.alignment = TextAnchor.MiddleCenter;
        timingStyleLate.normal.textColor = lateColor;
        if (timingFont != null) timingStyleLate.font = timingFont;
    }

    void Update()
    {
        // 終了したアニメーションを削除
        for (int i = activeAnimations.Count - 1; i >= 0; i--)
        {
            if (activeAnimations[i].IsFinished())
            {
                activeAnimations.RemoveAt(i);
            }
        }
    }

    public void PlayJudgmentAnimation(string judgment, string timing, int laneIndex)
    {
        if (noteManager == null || laneIndex < 0 || laneIndex >= noteManager.lanePositions.Length)
            return;

        if (noteManager.lanePositions[laneIndex] == null)
            return;

        // ワールド座標をスクリーン座標に変換
        Vector3 laneWorldPos = noteManager.lanePositions[laneIndex].position;
        laneWorldPos.z = noteManager.judgeLineZ; // 判定ラインのZ座標に設定

        Vector3 screenPos = Camera.main.WorldToScreenPoint(laneWorldPos);

        // スクリーン座標をGUI座標に変換（Yを反転）+ 判定Y位置オフセット適用
        Vector2 guiPos = new Vector2(screenPos.x - 50, Screen.height - screenPos.y - 50 + judgmentYOffset);

        JudgmentAnimation anim = new JudgmentAnimation(judgment, timing, guiPos, 1.2f);
        activeAnimations.Add(anim);
    }

    void OnGUI()
    {
        foreach (var anim in activeAnimations)
        {
            Vector2 currentPos = anim.GetCurrentPosition();
            float alpha = anim.GetCurrentAlpha();

            if (alpha <= 0f) continue;

            // 判定文字の色設定（カスタム色を使用）
            Color judgmentColor = GetJudgmentColor(anim.judgment);
            judgmentColor.a = alpha;
            judgmentStyle.normal.textColor = judgmentColor;

            // 判定文字表示
            GUI.Label(new Rect(currentPos.x, currentPos.y, 100, 30), anim.judgment, judgmentStyle);

            // タイミング文字表示（Fast/Late）- 空文字の場合は表示しない
            if (!string.IsNullOrEmpty(anim.timing))
            {
                GUIStyle timingStyle = anim.timing == "FAST" ? timingStyleFast : timingStyleLate;
                Color timingColor = anim.timing == "FAST" ? fastColor : lateColor;
                timingColor.a = alpha;
                timingStyle.normal.textColor = timingColor;

                // タイミング表示Y位置にオフセット適用
                GUI.Label(new Rect(currentPos.x, currentPos.y + timingYOffset, 100, 25), anim.timing, timingStyle);
            }
        }
    }

    Color GetJudgmentColor(string judgment)
    {
        switch (judgment)
        {
            case "PERFECT!": return perfectColor;
            case "GOOD": return goodColor;
            case "BAD": return badColor;
            case "MISS": return missColor;
            default: return Color.white;
        }
    }

    public void ClearAllAnimations()
    {
        activeAnimations.Clear();
    }
}