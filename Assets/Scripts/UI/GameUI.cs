using UnityEngine;

public class GameUI : MonoBehaviour
{
    [Header("UI設定")]
    public bool showDebugInfo = true;

    // 依存関係
    private ChartManager chartManager;
    private NoteManager noteManager;
    private ScoreManager scoreManager;
    private JudgmentSystem judgmentSystem;
    private RhythmGameController gameController;

    // GUI スタイル
    private GUIStyle style;
    private GUIStyle largeStyle;

    void Awake()
    {
        chartManager = FindObjectOfType<ChartManager>();
        noteManager = FindObjectOfType<NoteManager>();
        scoreManager = FindObjectOfType<ScoreManager>();
        judgmentSystem = FindObjectOfType<JudgmentSystem>();
        gameController = FindObjectOfType<RhythmGameController>();
    }

    void Start()
    {
        InitializeStyles();
    }

    void InitializeStyles()
    {
        style = new GUIStyle();
        style.fontSize = 16;
        style.normal.textColor = Color.white;

        largeStyle = new GUIStyle();
        largeStyle.fontSize = 24;
        largeStyle.normal.textColor = Color.yellow;
    }

    void OnGUI()
    {
        if (chartManager?.CurrentChart == null)
        {
            GUI.Label(new Rect(10, 40, 400, 30), "譜面ファイルの読み込みに失敗しました", style);
            return;
        }

        if (!gameController.IsPlaying)
        {
            DrawMenuUI();
        }
        else
        {
            DrawGameUI();
        }
    }

    void DrawMenuUI()
    {
        GUI.Label(new Rect(10, 10, 400, 30), "Rhythm Game - Proper Time Signature", style);
        GUI.Label(new Rect(10, 40, 400, 30), $"Song: {chartManager.CurrentChart.songInfo.title}", style);

        if (chartManager.SortedTimingPoints.Count > 0)
        {
            var firstTiming = chartManager.SortedTimingPoints[0];
            float beatsPerMeasure = firstTiming.GetBeatsPerMeasureIn4th();
            GUI.Label(new Rect(10, 70, 400, 30), $"BPM: {firstTiming.bpm}, 拍子: {firstTiming.GetTimeSignatureString()}", style);
            GUI.Label(new Rect(10, 100, 400, 30), $"4分音符換算: {beatsPerMeasure:F2}拍/小節", style);
        }

        GUI.Label(new Rect(10, 130, 400, 30), $"Level: {chartManager.CurrentChart.songInfo.level}", style);
        GUI.Label(new Rect(10, 160, 400, 30), $"Notes: {chartManager.CurrentChart.notes.Count}", style);
        GUI.Label(new Rect(10, 190, 400, 30), $"Chart File: {chartManager.chartFileName}.json", style);
        GUI.Label(new Rect(10, 220, 400, 30), $"Audio File: {chartManager.CurrentChart.songInfo.audioFile}", style);

        // Volume表示を小数点第3位まで精密表示
        GUI.Label(new Rect(10, 250, 400, 30), $"Volume: {chartManager.CurrentChart.songInfo.volume:F3}", style);

        float emptyTime = chartManager.GetEmptyMeasureTime();
        GUI.Label(new Rect(10, 280, 400, 30), $"Empty Measures: {chartManager.CurrentChart.songInfo.emptyMeasures} ({emptyTime:F2}s)", style);

        // Audio Offset表示を小数点第3位まで精密表示
        GUI.Label(new Rect(10, 310, 400, 30), $"Audio Offset: {chartManager.CurrentChart.songInfo.audioOffset:F3}s", style);

        // 音声ファイルの状態表示
        string audioStatus = chartManager.AudioSource != null && chartManager.AudioSource.clip != null ?
            $"Ready ({chartManager.AudioSource.clip.length:F2}s)" : "Loading...";
        GUI.Label(new Rect(10, 340, 400, 30), $"Audio Status: {audioStatus}", style);

        // 判定タイミング表示も精密に
        GUI.Label(new Rect(10, 370, 400, 30), $"Perfect: ±{judgmentSystem.perfectTiming:F2}ms, Good: ±{judgmentSystem.goodTiming:F2}ms", style);
        GUI.Label(new Rect(10, 400, 400, 30), $"Bad: ±{judgmentSystem.badTiming:F2}ms, Miss: ±{judgmentSystem.missTiming:F2}ms", style);
        GUI.Label(new Rect(10, 430, 400, 30), "Press SPACE to start", style);
    }

    void DrawGameUI()
    {
        GUI.Label(new Rect(10, 10, 300, 30), $"{chartManager.CurrentChart.songInfo.title}", style);
        GUI.Label(new Rect(10, 40, 200, 30), $"Score: {scoreManager.Score}", style);
        GUI.Label(new Rect(10, 70, 200, 30), $"Combo: {scoreManager.Combo}", style);

        // ★ タップノート判定数表示
        GUI.Label(new Rect(10, 100, 400, 30), $"Tap - P:{scoreManager.PerfectCount} G:{scoreManager.GoodCount} B:{scoreManager.BadCount} M:{scoreManager.MissCount}", style);

        // ★ 新規追加：ホールドノート判定数表示
        GUI.Label(new Rect(10, 130, 400, 30), $"Hold - P:{scoreManager.HoldPerfectCount} M:{scoreManager.HoldMissCount}", style);

        // Time表示を小数点第2位まで精密表示
        GUI.Label(new Rect(10, 160, 200, 30), $"Time: {gameController.SongTime:F2}s", style);

        // 現在のBPMと拍子を表示
        string timeSignatureStr = $"{chartManager.CurrentTimeSignature[0]}/{chartManager.CurrentTimeSignature[1]}";
        GUI.Label(new Rect(10, 190, 300, 30), $"BPM: {chartManager.CurrentBPM:F1}, 拍子: {timeSignatureStr}", style);

        // 位置表示（Y座標を調整）
        DrawPositionInfo();

        if (showDebugInfo)
        {
            DrawDebugInfo();
        }
    }

    void DrawPositionInfo()
    {
        float emptyMeasureTime = chartManager.GetEmptyMeasureTime();
        float adjustedTime = gameController.SongTime - emptyMeasureTime;

        if (adjustedTime >= 0)
        {
            // 簡略化された位置表示（拍子を考慮）
            float currentBeatsFromStart = adjustedTime * (chartManager.CurrentBPM / 60f);
            float currentBeatsPerMeasure = chartManager.CurrentTimeSignature[0] * (4f / chartManager.CurrentTimeSignature[1]);

            int displayMeasure = Mathf.FloorToInt(currentBeatsFromStart / currentBeatsPerMeasure) + 1;
            float displayBeat = (currentBeatsFromStart % currentBeatsPerMeasure) * (chartManager.CurrentTimeSignature[1] / 4f);

            // ★ Y座標を220に調整（ホールド判定表示分のスペースを確保）
            GUI.Label(new Rect(10, 220, 300, 30), $"Position: {displayMeasure}小節{displayBeat + 1:F2}拍", style);
        }
        else
        {
            // 空白小節中の表示
            GUI.Label(new Rect(10, 220, 300, 30), $"Empty Measures ({-adjustedTime:F2}s remaining)", style);
        }
    }

    void DrawDebugInfo()
    {
        // ★ Y座標を調整（250, 280に変更）
        GUI.Label(new Rect(10, 250, 400, 30), $"Active Notes: {noteManager.ActiveNotes.Count}", style);
        GUI.Label(new Rect(10, 280, 400, 30), $"Next Note Index: {noteManager.NextNoteIndex}/{chartManager.CurrentChart.notes.Count}", style);
    }
}