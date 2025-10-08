using UnityEngine;

public class RhythmGameController : MonoBehaviour
{
    [Header("デバッグ設定")]
    public bool debugMode = true;

    [Header("システム参照")]
    [SerializeField] private ChartManager chartManager;
    [SerializeField] private NoteManager noteManager;
    [SerializeField] private JudgmentSystem judgmentSystem;
    [SerializeField] private ScoreManager scoreManager;
    [SerializeField] private GameUI gameUI;
    [SerializeField] private JudgmentAnimator judgmentAnimator;
    [SerializeField] private ComboDisplay comboDisplay;

    // ゲーム状態
    private float songTime = 0f;
    private bool isPlaying = false;

    // プロパティ
    public bool IsPlaying => isPlaying;
    public float SongTime => songTime;

    void Awake()
    {
        // 各システムを自動取得（Inspector設定がない場合）
        if (chartManager == null) chartManager = FindObjectOfType<ChartManager>();
        if (noteManager == null) noteManager = FindObjectOfType<NoteManager>();
        if (judgmentSystem == null) judgmentSystem = FindObjectOfType<JudgmentSystem>();
        if (scoreManager == null) scoreManager = FindObjectOfType<ScoreManager>();
        if (gameUI == null) gameUI = FindObjectOfType<GameUI>();
        if (judgmentAnimator == null) judgmentAnimator = FindObjectOfType<JudgmentAnimator>();
        if (comboDisplay == null) comboDisplay = FindObjectOfType<ComboDisplay>();

        // デバッグモード設定
        if (chartManager) chartManager.debugMode = debugMode;
        if (noteManager) noteManager.debugMode = debugMode;
        if (judgmentSystem) judgmentSystem.debugMode = debugMode;
    }

    void Start()
    {
        // 譜面読み込み
        bool loadSuccess = chartManager?.LoadChart() ?? false;

        if (loadSuccess)
        {
            Debug.Log($"リズムゲームシステム起動: {chartManager.CurrentChart.notes.Count}ノーツ");
            Debug.Log($"楽曲: {chartManager.CurrentChart.songInfo.title} - {chartManager.CurrentChart.songInfo.artist}");
        }
        else
        {
            Debug.LogError("譜面の読み込みに失敗しました");
        }
    }

    void Update()
    {
        // 入力処理
        if (Input.GetKeyDown(KeyCode.Space) && !isPlaying)
        {
            StartGame();
        }
        if (Input.GetKeyDown(KeyCode.R) && isPlaying)
        {
            StopGame();
        }

        // タイミング調整（プレイ中のみ）
        if (isPlaying && noteManager != null)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                noteManager.noteTimingOffset -= 0.01f; // 10ms早く
                Debug.Log($"タイミング調整: {noteManager.noteTimingOffset:F3}s (早く)");
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                noteManager.noteTimingOffset += 0.01f; // 10ms遅く
                Debug.Log($"タイミング調整: {noteManager.noteTimingOffset:F3}s (遅く)");
            }
        }

        // ゲーム進行処理
        if (isPlaying)
        {
            songTime += Time.deltaTime;

            // 各システムの更新
            chartManager?.UpdateCurrentTimingInfo(songTime);
            noteManager?.CheckAndSpawnNotes(songTime);
            noteManager?.CheckNotePositions(songTime);
            judgmentSystem?.CheckMissedNotes(songTime);
        }
    }

    public void StartGame()
    {
        if (chartManager?.CurrentChart?.notes == null || chartManager.CurrentChart.notes.Count == 0)
        {
            Debug.LogError("譜面データが読み込まれていません！");
            return;
        }

        if (chartManager.SortedTimingPoints.Count == 0)
        {
            Debug.LogError("TimingPointsが設定されていません！");
            return;
        }

        // ゲーム状態初期化
        isPlaying = true;
        songTime = 0f;

        // 各システムの初期化
        noteManager?.Initialize();
        scoreManager?.Initialize();
        judgmentSystem?.Initialize();

        // 初期ノート配置を追加
        noteManager?.InitializeVisibleNotes();

        // 音声再生開始
        chartManager?.StartAudio();

        // ログ出力
        float emptyTime = chartManager?.GetEmptyMeasureTime() ?? 0f;
        Debug.Log($"=== ゲーム開始 ===");
        Debug.Log($"楽曲: {chartManager.CurrentChart.songInfo.title}");
        Debug.Log($"空白時間: {emptyTime:F3}秒");
        Debug.Log($"初期BPM: {chartManager.CurrentBPM}, 初期拍子: {chartManager.CurrentTimeSignature[0]}/{chartManager.CurrentTimeSignature[1]}");
        Debug.Log($"Audio Offset: {chartManager.CurrentChart.songInfo.audioOffset}s");
        Debug.Log($"Volume: {chartManager.CurrentChart.songInfo.volume}");
        Debug.Log($"最初のノーツ到達時刻: {emptyTime:F3}s");
        Debug.Log($"音声開始予定時刻: {emptyTime + chartManager.CurrentChart.songInfo.audioOffset:F3}s");
        Debug.Log($"=== 操作方法 ===");
        Debug.Log($"←キー: タイミングを早く (-0.01s)");
        Debug.Log($"→キー: タイミングを遅く (+0.01s)");
        Debug.Log($"現在のタイミング調整: {noteManager?.noteTimingOffset:F3}s");
    }

    public void StopGame()
    {
        isPlaying = false;

        // 音声停止
        chartManager?.StopAudio();

        // ノート削除
        noteManager?.ClearAllNotes();

        // 結果表示
        scoreManager?.LogResults(chartManager.CurrentChart.songInfo.title);
    }
}