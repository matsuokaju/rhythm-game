using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

public class NoteManager : MonoBehaviour
{
    [Header("ノート設定")]
    public GameObject tapNotePrefab;
    public GameObject holdNotePrefab; // ホールドノート専用プレハブ（基準点が前端）

    [Header("レーン設定")]
    public Transform[] lanePositions = new Transform[6];

    [Header("ゲーム設定")]
    public float noteSpeed = 10.0f;
    public float judgeLineZ = 0f;
    public float spawnDistance = 10f;

    [Header("タイミング調整")]
    [Tooltip("ノーツのタイミング調整 (秒)\nfastが多い時: マイナス値\nlateが多い時: プラス値")]
    public float noteTimingOffset = -0.025f; // audioOffsetと合わせて調整

    [Header("小節線設定")]
    public GameObject measureLinePrefab;
    [Range(0f, 1f)]
    public float measureLineAlpha = 0.3f;
    public bool showMeasureLines = true;
    [Tooltip("小節線の幅（全レーン幅）")]
    public float measureLineWidth = 6.05f;

    public bool debugMode = true;

    private List<ActiveNote> activeNotes = new List<ActiveNote>();
    private List<GameObject> activeMeasureLines = new List<GameObject>();
    private int nextNoteIndex = 0;
    private int nextMeasureIndex = 0; // 次にスポーンする小節線のインデックス
    private float travelTime;
    private int startFromMeasure = 0; // 途中開始時の開始小節

    // 依存関係
    private ChartManager chartManager;
    private RhythmGameController gameController;

    // プロパティ
    public List<ActiveNote> ActiveNotes => activeNotes;
    public int NextNoteIndex => nextNoteIndex;

    void Awake()
    {
        travelTime = spawnDistance / noteSpeed;
        chartManager = FindObjectOfType<ChartManager>();
        // ゲームコントローラー取得
        gameController = FindObjectOfType<RhythmGameController>();
    }

    // songTimeを取得するプロパティ
    private float songTime => gameController?.SongTime ?? 0f;

    void Start()
    {
        if (debugMode) ValidateLanes();
        Debug.Log($"Travel time: {travelTime:F3}s, Speed: {noteSpeed}, Distance: {spawnDistance}");
        Debug.Log($"Judge Line Z: {judgeLineZ}");
        Debug.Log($"Note Timing Offset: {noteTimingOffset:F3}s");
    }

    public void Initialize(int startFromMeasure = 0)
    {
        this.startFromMeasure = startFromMeasure; // 開始小節を保存
        nextNoteIndex = 0;
        nextMeasureIndex = startFromMeasure; // 小節線も開始小節から
        ClearAllNotes();
        ClearAllMeasureLines();
        
        // ★ 開始小節が指定されている場合、それより前のノーツをスキップ
        if (startFromMeasure > 0 && chartManager?.CurrentChart?.notes != null)
        {
            int skippedNotes = 0;
            foreach (var note in chartManager.CurrentChart.notes)
            {
                if (note.measure < startFromMeasure)
                {
                    skippedNotes++;
                }
                else
                {
                    break; // 最初の対象ノーツに到達
                }
            }
            nextNoteIndex = skippedNotes;
            
            if (debugMode && skippedNotes > 0)
            {
                Debug.Log($"開始小節{startFromMeasure}: {skippedNotes}個のノーツをスキップしました");
            }
        }
    }

    public void CheckAndSpawnMeasureLines(float songTime)
    {
        if (!showMeasureLines || measureLinePrefab == null || chartManager == null) return;

        float spawnThreshold = songTime + travelTime;
        
        // 現在の小節から先読み範囲内の小節線をスポーン
        while (true)
        {
            float measureTime = chartManager.GetMeasureStartTime(nextMeasureIndex);
            float adjustedMeasureTime = measureTime + noteTimingOffset; // スポーン判定にもオフセット適用
            
            if (adjustedMeasureTime <= spawnThreshold)
            {
                // 途中開始時：開始小節以前の小節線はスキップ
                if (startFromMeasure > 0 && nextMeasureIndex < startFromMeasure)
                {
                    if (debugMode)
                    {
                        Debug.Log($"小節線スキップ（開始小節前）: {nextMeasureIndex}小節目");
                    }
                    nextMeasureIndex++;
                    continue;
                }
                
                SpawnMeasureLine(nextMeasureIndex, adjustedMeasureTime);
                nextMeasureIndex++;
            }
            else
            {
                break;
            }
        }
    }

    public void CheckAndSpawnNotes(float songTime)
    {
        if (chartManager?.CurrentChart?.notes == null) return;

        float spawnThreshold = songTime + travelTime;
        while (nextNoteIndex < chartManager.CurrentChart.notes.Count)
        {
            Note note = chartManager.CurrentChart.notes[nextNoteIndex];
            
            // ChartManagerの正確な時刻計算を使用（GetTotalBeatsForNoteは使わずに直接計算）
            float noteBeats = chartManager.GetTotalBeatsForPosition(note.measure, note.beat);
            float noteTime = CalculateTimeFromBeatsDirectly(noteBeats);
            float adjustedNoteTime = noteTime + noteTimingOffset; // タイミング調整を適用

            if (adjustedNoteTime <= spawnThreshold)
            {
                // 既にアクティブノートに存在するかチェック（重複防止）
                bool alreadyExists = activeNotes.Any(activeNote =>
                    activeNote.hitTime == adjustedNoteTime &&
                    activeNote.lane == note.lane &&
                    activeNote.type == note.type);

                if (!alreadyExists)
                {
                    bool success = SpawnNote(note, adjustedNoteTime);
                    if (!success && debugMode)
                    {
                        Debug.LogWarning($"ノート生成に失敗しました: {note.GetMusicalDescription()}");
                    }
                }
                else if (debugMode)
                {
                    Debug.Log($"スキップ（既存）: {note.GetMusicalDescription()} at {noteTime:F3}s");
                }

                nextNoteIndex++;
            }
            else
            {
                break;
            }
        }
    }

    bool SpawnNote(Note note, float noteTime)
    {
        if (note.lane < 0 || note.lane >= lanePositions.Length ||
            lanePositions[note.lane] == null || tapNotePrefab == null)
        {
            if (debugMode) Debug.LogError($"スポーン失敗: Lane {note.lane} が無効、またはnotePrefabが未設定です");
            return false;
        }

        try
        {
            Vector3 lanePos = lanePositions[note.lane].position;

            // ノートタイプに応じてプレハブを選択
            GameObject prefabToUse = (note.type == "hold" && holdNotePrefab != null) ? holdNotePrefab : tapNotePrefab;
            if (prefabToUse == null)
            {
                if (debugMode) Debug.LogError($"ノートプレハブが設定されていません: {note.type}");
                return false;
            }
            // ノートのスポーン位置を計算
            Vector3 spawnPos = CalculateInitialNotePosition(note, noteTime, lanePos);

            GameObject noteObj = Instantiate(prefabToUse, spawnPos, prefabToUse.transform.rotation);

            if (noteObj == null)
            {
                if (debugMode) Debug.LogError("ノートオブジェクトのインスタンス化に失敗しました");
                return false;
            }

            // タップノートの基準点を前端に調整
            if (note.type != "hold")
            {
                SetupTapNote(noteObj);
            }

            NoteMovement movement = noteObj.GetComponent<NoteMovement>();
            if (movement == null)
            {
                movement = noteObj.AddComponent<NoteMovement>();
            }
            movement.speed = noteSpeed;
            movement.targetZ = judgeLineZ;

            // ホールドノートの長さ設定
            if (note.type == "hold" && note.duration > 0)
            {
                SetupHoldNote(noteObj, note, noteTime); // 調整済みの時刻を渡す
            }

            ActiveNote activeNote = new ActiveNote(noteObj, noteTime, note.lane, note.type, note.GetMusicalDescription(), note.duration);
            activeNotes.Add(activeNote);

            if (debugMode)
            {
                string noteInfo = note.type == "hold" ?
                    $"{note.GetMusicalDescription()} Duration={note.duration}拍" :
                    note.GetMusicalDescription();
                Debug.Log($"Spawned: {noteInfo} Lane={note.lane} HitTime={noteTime:F3} SpawnZ={spawnPos.z} JudgeZ={judgeLineZ}");
            }

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SpawnNote中にエラーが発生しました: {e.Message}");
            return false;
        }
    }

    Vector3 CalculateInitialNotePosition(Note note, float noteTime, Vector3 lanePos)
    {
        // 現在の時刻から判定タイミングまでの残り時間
        // noteTimeは既にタイミング調整が適用されているとする
        float timeToJudgment = noteTime - songTime;

        // 理想的な配置位置を計算
        float idealDistance;

        if (timeToJudgment > 0)
        {
            // まだ判定タイミング前：残り時間 × 速度で距離計算
            idealDistance = timeToJudgment * noteSpeed;

            // 最大スポーン距離でクランプ
            idealDistance = Mathf.Min(idealDistance, spawnDistance);
        }
        else
        {
            // 既に判定タイミング後：判定ライン位置またはそれより後ろ
            idealDistance = 0f;
        }

        // スポーン位置を計算
        Vector3 spawnPos = new Vector3(lanePos.x, lanePos.y + 0.03f, judgeLineZ + idealDistance);


        if (debugMode)
        {
            Debug.Log($"Note Initial Position:");
            Debug.Log($"  Note Time: {noteTime:F3}s, Current Time: {songTime:F3}s");
            Debug.Log($"  Time to Judgment: {timeToJudgment:F3}s");
            Debug.Log($"  Ideal Distance: {idealDistance:F2}, Spawn Position: {spawnPos}");
        }

        return spawnPos;
    }

    // ChartManagerの計算結果から直接時刻を取得（デバッグログの値を使用）
    float CalculateTimeFromBeatsDirectly(float totalBeats)
    {
        // ログから確認できる正確な時刻を使用
        // 1小節1拍目(beat=0) -> 2.000s
        // 1小節1拍半(beat=0.5) -> 2.250s  
        // 2小節1拍目(beat=0) -> 4.000s
        
        // 暫定的に、ChartManagerのGetNoteTimeFromBeatsを使用してデバッグ
        float result = chartManager.GetNoteTimeFromBeats(totalBeats);
        
        return result;
    }

    void SpawnMeasureLine(int measureNumber, float adjustedMeasureTime)
    {
        if (measureLinePrefab == null || lanePositions.Length == 0) return;

        // 既にオフセット適用済みの時刻を受け取る
        float originalMeasureTime = adjustedMeasureTime - noteTimingOffset;

        // レーンの中央位置を計算（全レーンにまたがる1本の小節線）
        Vector3 centerPos = CalculateLanesCenterPosition();
        Vector3 spawnPos = CalculateInitialMeasureLinePosition(adjustedMeasureTime, centerPos);

        GameObject measureLine = Instantiate(measureLinePrefab, spawnPos, measureLinePrefab.transform.rotation);

        // 全レーンにまたがるようにスケール調整
        SetupMeasureLineScale(measureLine);

        // 透明度設定
        SetMeasureLineAlpha(measureLine, measureLineAlpha);

        // 移動コンポーネント設定
        MeasureLineMovement movement = measureLine.GetComponent<MeasureLineMovement>();
        if (movement == null)
        {
            movement = measureLine.AddComponent<MeasureLineMovement>();
        }
        movement.Initialize(noteSpeed, measureNumber, this);

        activeMeasureLines.Add(measureLine);

        if (debugMode)
        {
            Debug.Log($"小節線スポーン: {measureNumber}小節目 Time={originalMeasureTime:F3}s→{adjustedMeasureTime:F3}s (offset={noteTimingOffset:F3}s) Center={centerPos}");
        }
    }

    Vector3 CalculateLanesCenterPosition()
    {
        if (lanePositions.Length == 0) return Vector3.zero;

        // 最初と最後のレーン位置から中央を計算
        Vector3 firstLane = lanePositions[0].position;
        Vector3 lastLane = lanePositions[lanePositions.Length - 1].position;
        
        Vector3 center = new Vector3(
            (firstLane.x + lastLane.x) * 0.5f,
            firstLane.y + 0.02f, // Y軸を少し上げて小節線とする
            firstLane.z
        );

        return center;
    }

    void SetupMeasureLineScale(GameObject measureLine)
    {
        if (lanePositions.Length == 0) return;

        // Quadを水平（X-Z平面）に向ける
        measureLine.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // 小節線のスケールを設定
        measureLine.transform.localScale = new Vector3(
            measureLineWidth,    // X軸：全レーン幅（Inspector設定値）
            0.1f,               // Y軸：小節線の厚み（回転後はZ軸方向）
            measureLine.transform.localScale.z  // Z軸：そのまま
        );

        if (debugMode)
        {
            Debug.Log($"小節線設定: 幅={measureLineWidth}, 厚み=0.1, 回転=(90,0,0)");
        }
    }

    Vector3 CalculateInitialMeasureLinePosition(float measureTime, Vector3 centerPos)
    {
        // 現在の時刻から小節線タイミングまでの残り時間
        float timeToMeasure = measureTime - songTime;

        // 理想的な配置位置を計算
        float idealDistance;

        if (timeToMeasure > 0)
        {
            // まだ小節線タイミング前：残り時間 × 速度で距離計算
            idealDistance = timeToMeasure * noteSpeed;
            // 最大スポーン距離でクランプ
            idealDistance = Mathf.Min(idealDistance, spawnDistance);
        }
        else
        {
            // 既に小節線タイミング後：判定ライン位置
            idealDistance = 0f;
        }

        // スポーン位置を計算
        Vector3 spawnPos = new Vector3(centerPos.x, centerPos.y, judgeLineZ + idealDistance);

        return spawnPos;
    }

    void SetMeasureLineAlpha(GameObject measureLine, float alpha)
    {
        Renderer renderer = measureLine.GetComponent<Renderer>();
        if (renderer != null && renderer.material != null)
        {
            Color color = renderer.material.color;
            color.a = alpha;
            renderer.material.color = color;
        }
    }

    public void RemoveMeasureLine(GameObject measureLine)
    {
        activeMeasureLines.Remove(measureLine);
    }

    public void ClearAllMeasureLines()
    {
        foreach (var measureLine in activeMeasureLines)
        {
            if (measureLine != null) Destroy(measureLine);
        }
        activeMeasureLines.Clear();
    }

    public void InitializeVisibleNotes()
    {
        if (chartManager?.CurrentChart?.notes == null) return;

        float currentTime = songTime;
        float maxLookAhead = spawnDistance / noteSpeed; // 最大先読み時間

        if (debugMode)
        {
            Debug.Log($"=== 初期ノート配置開始 ===");
            Debug.Log($"Current Time: {currentTime:F3}s");
            Debug.Log($"Max Look Ahead: {maxLookAhead:F3}s");
        }

        int spawnedCount = 0;

        for (int i = 0; i < chartManager.CurrentChart.notes.Count; i++)
        {
            Note note = chartManager.CurrentChart.notes[i];
            
            // ★ 途中開始時：開始小節以前のノーツはスキップ
            if (startFromMeasure > 0 && note.measure < startFromMeasure)
            {
                if (debugMode)
                {
                    Debug.Log($"初期配置スキップ（開始小節前）: {note.GetMusicalDescription()} (小節{note.measure} < {startFromMeasure})");
                }
                continue;
            }
            
            // ChartManagerの正確な時刻計算を使用
            float noteBeats = chartManager.GetTotalBeatsForPosition(note.measure, note.beat);
            float noteTime = CalculateTimeFromBeatsDirectly(noteBeats);
            float adjustedNoteTime = noteTime + noteTimingOffset; // タイミング調整を適用
            float timeToNote = adjustedNoteTime - currentTime;

            // 現在見えているべきノート範囲をチェック
            if (timeToNote <= maxLookAhead && timeToNote >= -1f) // 1秒前まで許容
            {
                bool success = SpawnNoteAtCorrectPosition(note, adjustedNoteTime, i);
                if (success)
                {
                    spawnedCount++;

                    // このノートは通常のスポーン処理でスキップするため、nextNoteIndexを調整
                    if (i >= nextNoteIndex)
                    {
                        nextNoteIndex = i + 1;
                    }
                }
            }
            else if (timeToNote > maxLookAhead)
            {
                // これ以降のノートは範囲外なので処理終了
                break;
            }
        }

        if (debugMode)
        {
            Debug.Log($"初期配置完了: {spawnedCount}個のノートをスポーン");
            Debug.Log($"Next Note Index: {nextNoteIndex}");
        }
    }

    bool SpawnNoteAtCorrectPosition(Note note, float noteTime, int noteIndex)
    {
        if (note.lane < 0 || note.lane >= lanePositions.Length ||
            lanePositions[note.lane] == null || tapNotePrefab == null)
        {
            if (debugMode) Debug.LogError($"初期スポーン失敗: Lane {note.lane} が無効、またはnotePrefabが未設定です");
            return false;
        }

        try
        {
            Vector3 lanePos = lanePositions[note.lane].position;

            // ノートタイプに応じてプレハブを選択
            GameObject prefabToUse = (note.type == "hold" && holdNotePrefab != null) ? holdNotePrefab : tapNotePrefab;
            if (prefabToUse == null)
            {
                if (debugMode) Debug.LogError($"ノートプレハブが設定されていません: {note.type}");
                return false;
            }

            // 初期位置を計算
            Vector3 spawnPos = CalculateInitialNotePosition(note, noteTime, lanePos);

            GameObject noteObj = Instantiate(prefabToUse, spawnPos, prefabToUse.transform.rotation);

            if (noteObj == null)
            {
                if (debugMode) Debug.LogError("ノートオブジェクトのインスタンス化に失敗しました");
                return false;
            }

            // タップノートの基準点を前端に調整
            if (note.type != "hold")
            {
                SetupTapNote(noteObj);
            }

            NoteMovement movement = noteObj.GetComponent<NoteMovement>();
            if (movement == null)
            {
                movement = noteObj.AddComponent<NoteMovement>();
            }
            movement.speed = noteSpeed;
            movement.targetZ = judgeLineZ;

            // ホールドノートの長さ設定
            if (note.type == "hold" && note.duration > 0)
            {
                SetupHoldNote(noteObj, note, noteTime); // 調整済みの時刻を渡す
            }

            ActiveNote activeNote = new ActiveNote(noteObj, noteTime, note.lane, note.type, note.GetMusicalDescription(), note.duration);
            activeNotes.Add(activeNote);

            if (debugMode)
            {
                string noteInfo = note.type == "hold" ?
                    $"{note.GetMusicalDescription()} Duration={note.duration}拍" :
                    note.GetMusicalDescription();
                Debug.Log($"Initial Spawned: {noteInfo} Lane={note.lane} HitTime={noteTime:F3} SpawnZ={spawnPos.z} (Index:{noteIndex})");
            }

            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SpawnNoteAtCorrectPosition中にエラーが発生しました: {e.Message}");
            return false;
        }
    }

    void SetupTapNote(GameObject noteObj)
    {
        // タップノートの厚みを取得（Scale.yが厚み）
        float tapThickness = noteObj.transform.localScale.y;

        // 基準点を前端に調整：厚みの半分だけ後ろに移動
        Vector3 currentPos = noteObj.transform.position;
        currentPos.z += tapThickness * 0.5f;
        noteObj.transform.position = currentPos;

        if (debugMode)
        {
            Debug.Log($"Tap Note Setup:");
            Debug.Log($"  Thickness: {tapThickness:F2}");
            Debug.Log($"  Adjusted Position: {noteObj.transform.position}");
            Debug.Log($"  Front Position: {noteObj.transform.position.z + tapThickness * 0.5f:F2}");
        }
    }

    void SetupHoldNote(GameObject noteObj, Note note, float adjustedNoteTime)
    {
        // BPMから長さを計算
        float bpm = chartManager.CurrentBPM;
        float durationInSeconds = note.duration * (60f / bpm);
        float holdLength = durationInSeconds * noteSpeed;

        // スケール調整
        noteObj.transform.localScale = new Vector3(
            noteObj.transform.localScale.x,  // 幅はそのまま (0.8)
            holdLength,                      // 長さ：Y軸スケールで移動方向に伸ばす
            noteObj.transform.localScale.z   // Z軸はそのまま
        );

        // 基準点を前端に調整：長さの半分だけ後ろに移動
        Vector3 currentPos = noteObj.transform.position;
        currentPos.z += holdLength * 0.5f; // 長さの半分だけ後ろに移動
        noteObj.transform.position = currentPos;

        // ★ 修正：調整済みのstartTimeを使用（SpawnNoteから渡される）
        // SetupHoldNoteは調整済みのnoteTimeが渡される前提で動作するように変更

        // ホールドノート専用のコンポーネントを追加・初期化
        HoldNoteController holdController = noteObj.GetComponent<HoldNoteController>();
        if (holdController == null)
        {
            holdController = noteObj.AddComponent<HoldNoteController>();
        }
        holdController.Initialize(note.duration, durationInSeconds, holdLength, adjustedNoteTime, note.lane);

        if (debugMode)
        {
            Debug.Log($"Hold Note Setup:");
            Debug.Log($"  Duration: {note.duration}拍 ({durationInSeconds:F2}秒)");
            Debug.Log($"  Length: {holdLength:F2} ワールド単位");
            Debug.Log($"  Start Time: {adjustedNoteTime:F3}s");
            Debug.Log($"  Lane: {note.lane}");
            Debug.Log($"  Scale: {noteObj.transform.localScale}");
            Debug.Log($"  Adjusted Position: {noteObj.transform.position}");
            Debug.Log($"  Front Position: {noteObj.transform.position.z + holdLength * 0.5f:F2}");
            Debug.Log($"  Back Position: {noteObj.transform.position.z - holdLength * 0.5f:F2}");
        }
    }

    public void CheckNotePositions(float songTime)
    {
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            ActiveNote note = activeNotes[i];
            if (note.gameObject == null)
            {
                activeNotes.RemoveAt(i);
                continue;
            }

            float currentZ = note.gameObject.transform.position.z;

            if (currentZ <= judgeLineZ && !note.gameObject.GetComponent<NotePassedMarker>())
            {
                note.gameObject.AddComponent<NotePassedMarker>();

                if (debugMode)
                {
                    Debug.Log($"ノーツが判定ライン通過: {note.position} - 理論時刻:{note.hitTime:F3}s 実際:{songTime:F3}s 差:{(songTime - note.hitTime) * 1000:F1}ms");
                }
            }
        }
    }

    public void RemoveNote(ActiveNote note)
    {
        activeNotes.Remove(note);
        if (note.gameObject != null)
        {
            Destroy(note.gameObject);
        }
    }

    public void ClearAllNotes()
    {
        foreach (var note in activeNotes)
        {
            if (note.gameObject != null) Destroy(note.gameObject);
        }
        activeNotes.Clear();
        ClearAllMeasureLines();
    }

    void ValidateLanes()
    {
        Debug.Log("=== レーン設定確認 ===");
        for (int i = 0; i < lanePositions.Length; i++)
        {
            if (lanePositions[i] != null)
                Debug.Log($"Lane {i}: Position {lanePositions[i].position}");
            else
                Debug.LogError($"Lane {i}: 未設定");
        }
        Debug.Log($"Judge Line Z: {judgeLineZ}");
    }
}

public class NoteMovement : MonoBehaviour
{
    public float speed;
    public float targetZ;

    void Update()
    {
        // 回転に関係なく、ワールド座標のZ軸負方向に移動
        transform.position += Vector3.back * speed * Time.deltaTime;
    }
}

public class NotePassedMarker : MonoBehaviour { }

public class MeasureLineMovement : MonoBehaviour
{
    public float speed;
    public int measureNumber;
    
    private NoteManager noteManager;

    public void Initialize(float noteSpeed, int measure, NoteManager manager)
    {
        speed = noteSpeed;
        measureNumber = measure;
        noteManager = manager;
    }

    void Update()
    {
        // 回転に関係なく、ワールド座標のZ軸負方向に移動
        transform.position += Vector3.back * speed * Time.deltaTime;

        // 判定ラインを通過したら削除
        if (transform.position.z < -5f)
        {
            noteManager?.RemoveMeasureLine(gameObject);
            Destroy(gameObject);
        }
    }
}