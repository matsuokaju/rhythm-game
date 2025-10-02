using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections;

public class ChartManager : MonoBehaviour
{
    [Header("譜面設定")]
    public string chartFileName = "sample_chart";
    public bool debugMode = true;

    private ChartData currentChart;
    private List<TimingPoint> sortedTimingPoints = new List<TimingPoint>();
    private AudioSource audioSource;
    
    // 現在のBPMと拍子を追跡
    private float currentBPM = 120f;
    private int[] currentTimeSignature = { 4, 4 };

    // プロパティ
    public ChartData CurrentChart => currentChart;
    public List<TimingPoint> SortedTimingPoints => sortedTimingPoints;
    public float CurrentBPM => currentBPM;
    public int[] CurrentTimeSignature => currentTimeSignature;
    public AudioSource AudioSource => audioSource;

    void Awake()
    {
        // Audio Sourceを動的に作成
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        
        // AudioSourceを有効化
        audioSource.enabled = true;
        audioSource.playOnAwake = false;
    }

    public bool LoadChart()
    {
        string folderPath = Path.Combine(Application.streamingAssetsPath, "Charts");
        string filePath = Path.Combine(folderPath, chartFileName + ".json");
        
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Debug.LogError($"Charts フォルダが存在しませんでした。作成しました: {folderPath}");
        }

        if (!File.Exists(filePath))
        {
            Debug.LogError($"譜面ファイルが見つかりません: {filePath}");
            return false;
        }

        try
        {
            string jsonText = File.ReadAllText(filePath);
            currentChart = JsonUtility.FromJson<ChartData>(jsonText);
            Debug.Log($"譜面ファイル読込: {filePath}");
            
            if (currentChart == null || currentChart.notes == null)
            {
                Debug.LogError("譜面データの読み込みに失敗しました");
                return false;
            }

            // TimingPointsの必須チェック
            if (currentChart.timingPoints == null || currentChart.timingPoints.Count == 0)
            {
                Debug.LogError("TimingPointsが設定されていません。最低でもmeasure 1の情報が必要です。");
                return false;
            }

            // measure 1の情報があるかチェック
            var firstTimingPoint = currentChart.timingPoints.FirstOrDefault(tp => tp.measure == 1 && tp.beat == 0);
            if (firstTimingPoint == null)
            {
                Debug.LogError("TimingPointsにmeasure 1, beat 0の情報が必要です。");
                return false;
            }

            // 音声ファイルを読み込み
            LoadAudioFile();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"譜面ファイルの読み込みエラー: {e.Message}");
            return false;
        }

        sortedTimingPoints = currentChart.timingPoints.OrderBy(tp => GetTotalBeatsForPosition(tp.measure, tp.beat)).ToList();
        currentChart.notes.Sort((a, b) => a.GetTotalBeats().CompareTo(b.GetTotalBeats()));

        // 初期BPMと拍子を設定
        if (sortedTimingPoints.Count > 0)
        {
            currentBPM = sortedTimingPoints[0].bpm;
            currentTimeSignature = sortedTimingPoints[0].timeSignature;
        }

        if (debugMode)
        {
            DebugLogChartInfo();
        }

        return true;
    }

    void LoadAudioFile()
    {
        if (string.IsNullOrEmpty(currentChart.songInfo.audioFile))
        {
            Debug.LogWarning("audioFileが指定されていません");
            return;
        }

        string[] possiblePaths = {
            Path.Combine(Application.streamingAssetsPath, "Audio", currentChart.songInfo.audioFile),
            Path.Combine(Application.streamingAssetsPath, "Charts", currentChart.songInfo.audioFile),
            Path.Combine(Application.streamingAssetsPath, currentChart.songInfo.audioFile)
        };

        foreach (string audioPath in possiblePaths)
        {
            if (File.Exists(audioPath))
            {
                StartCoroutine(LoadAudioClip(audioPath));
                return;
            }
        }

        Debug.LogError($"音声ファイルが見つかりません: {currentChart.songInfo.audioFile}");
        Debug.LogError("以下の場所を確認してください:");
        foreach (string path in possiblePaths)
        {
            Debug.LogError($"- {path}");
        }
    }

    IEnumerator LoadAudioClip(string path)
    {
        string url = "file://" + path.Replace("\\", "/");
        
        // ファイル拡張子に基づいてAudioTypeを決定
        AudioType audioType = AudioType.MPEG;
        string extension = Path.GetExtension(path).ToLower();
        
        switch (extension)
        {
            case ".mp3":
                audioType = AudioType.MPEG;
                break;
            case ".wav":
                audioType = AudioType.WAV;
                break;
            case ".ogg":
                audioType = AudioType.OGGVORBIS;
                break;
        }
        
        Debug.Log($"音声ファイル読込開始: {path} (Type: {audioType})");
        
        using (UnityEngine.Networking.UnityWebRequest www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, audioType))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                if (clip != null)
                {
                    audioSource.clip = clip;
                    
                    // 音量をJSONの設定値に合わせる
                    audioSource.volume = Mathf.Clamp01(currentChart.songInfo.volume);
                    
                    Debug.Log($"音声ファイル読込成功: {path}");
                    Debug.Log($"音声の長さ: {clip.length:F2}秒");
                    Debug.Log($"音量設定: {audioSource.volume:F2}");
                    Debug.Log($"AudioSource enabled: {audioSource.enabled}");
                }
                else
                {
                    Debug.LogError($"AudioClipの作成に失敗: {path}");
                }
            }
            else
            {
                Debug.LogError($"音声ファイルの読込に失敗: {www.error}");
                Debug.LogError($"URL: {url}");
            }
        }
    }

    // 拍子を考慮した総拍数計算（音価も考慮）
    public float GetTotalBeatsForPosition(int measure, float beat)
    {
        if (sortedTimingPoints.Count == 0) return 0f;
        
        float totalBeats = 0f;
        int currentMeasure = 1;
        float currentBeatsPerMeasure = sortedTimingPoints[0].GetBeatsPerMeasureIn4th();
        
        foreach (var tp in sortedTimingPoints)
        {
            // このTimingPointまでの小節を計算
            while (currentMeasure < tp.measure)
            {
                if (currentMeasure >= measure)
                {
                    // 目標小節に到達
                    return totalBeats + beat;
                }
                totalBeats += currentBeatsPerMeasure;
                currentMeasure++;
            }
            
            // TimingPointの位置で拍子変更
            if (tp.measure == currentMeasure && currentMeasure < measure)
            {
                totalBeats += tp.beat;
                currentBeatsPerMeasure = tp.GetBeatsPerMeasureIn4th();
            }
            else if (tp.measure > measure)
            {
                break;
            }
        }
        
        // 残りの小節を計算
        while (currentMeasure < measure)
        {
            totalBeats += currentBeatsPerMeasure;
            currentMeasure++;
        }
        
        return totalBeats + beat;
    }

    // 空白小節の時間を正しく計算（音価考慮）
    public float GetEmptyMeasureTime()
    {
        if (sortedTimingPoints.Count == 0)
            return 0f;

        // 固定で1小節の時間を計算
        int fixedEmptyMeasures = 1; // 固定値

        float totalTime = 0f;
        float currentBPM = sortedTimingPoints[0].bpm;
        float currentBeatsPerMeasure = sortedTimingPoints[0].GetBeatsPerMeasureIn4th();

        for (int measure = 1; measure <= fixedEmptyMeasures; measure++)
        {
            // この小節でのBPM/拍子変更をチェック
            foreach (var tp in sortedTimingPoints)
            {
                if (tp.measure == measure && tp.beat == 0)
                {
                    currentBPM = tp.bpm;
                    currentBeatsPerMeasure = tp.GetBeatsPerMeasureIn4th();
                    break;
                }
            }

            float secondsPerBeat = 60f / currentBPM; // 4分音符1拍の時間
            totalTime += currentBeatsPerMeasure * secondsPerBeat;
        }

        return totalTime;
    }

    // ノーツのタイミング計算を修正（音価考慮）
    public float GetNoteTimeFromBeats(float totalBeats)
    {
        float currentTime = 0f;
        float currentBeat = 0f;
        float currentBPM = sortedTimingPoints[0].bpm;
        
        // 空白小節分の時間を最初に加算
        float emptyTime = GetEmptyMeasureTime();
        
        // 実際の譜面の計算
        float targetTotalBeats = GetTotalBeatsForNote(totalBeats);
        
        foreach (var tp in sortedTimingPoints)
        {
            float tpTotalBeats = GetTotalBeatsForPosition(tp.measure, tp.beat);
            
            if (tpTotalBeats > targetTotalBeats)
                break;
                
            if (tpTotalBeats > currentBeat)
            {
                float sectionBeats = tpTotalBeats - currentBeat;
                float secondsPerBeat = 60f / currentBPM; // 4分音符1拍の時間
                currentTime += sectionBeats * secondsPerBeat;
                currentBeat = tpTotalBeats;
            }
            
            currentBPM = tp.bpm;
        }
        
        if (targetTotalBeats > currentBeat)
        {
            float remainingBeats = targetTotalBeats - currentBeat;
            float secondsPerBeat = 60f / currentBPM; // 4分音符1拍の時間
            currentTime += remainingBeats * secondsPerBeat;
        }
        
        return currentTime + emptyTime;
    }

    // ノーツの総拍数計算（従来の4拍子固定から拍子考慮版に変更）
    float GetTotalBeatsForNote(float originalTotalBeats)
    {
        // originalTotalBeatsは(measure-1)*4 + beatの形式なので、これを実際の拍子に変換
        int measure = Mathf.FloorToInt(originalTotalBeats / 4f) + 1;
        float beat = originalTotalBeats % 4f;
        
        return GetTotalBeatsForPosition(measure, beat);
    }

    // 現在の時刻でのBPMと拍子を取得（修正版）
    public void UpdateCurrentTimingInfo(float songTime)
    {
        float emptyMeasureTime = GetEmptyMeasureTime();
        float adjustedTime = songTime - emptyMeasureTime;
        
        if (adjustedTime < 0)
        {
            // 空白小節中は最初のTimingPointを使用
            if (sortedTimingPoints.Count > 0)
            {
                currentBPM = sortedTimingPoints[0].bpm;
                currentTimeSignature = sortedTimingPoints[0].timeSignature;
            }
            return;
        }

        // 現在時刻に対応するTimingPointを見つける
        float currentTime = 0f;
        float currentBeat = 0f;
        float tempBPM = sortedTimingPoints[0].bpm;
        int[] tempTimeSignature = sortedTimingPoints[0].timeSignature;

        foreach (var tp in sortedTimingPoints)
        {
            float tpBeat = GetTotalBeatsForPosition(tp.measure, tp.beat);
            
            // 現在の位置まで時間を計算
            if (tpBeat > currentBeat)
            {
                float sectionBeats = tpBeat - currentBeat;
                float secondsPerBeat = 60f / tempBPM; // 4分音符1拍の時間
                currentTime += sectionBeats * secondsPerBeat;
                currentBeat = tpBeat;
            }
            
            if (currentTime > adjustedTime)
                break;
                
            tempBPM = tp.bpm;
            tempTimeSignature = tp.timeSignature;
        }
        
        currentBPM = tempBPM;
        currentTimeSignature = tempTimeSignature;
    }

    public void StartAudio()
    {
        if (audioSource != null)
        {
            if (!audioSource.enabled)
            {
                audioSource.enabled = true;
                Debug.Log("AudioSourceを有効化しました");
            }
            
            if (audioSource.clip != null)
            {
                audioSource.volume = Mathf.Clamp01(currentChart.songInfo.volume);
                
                float emptyMeasureTime = GetEmptyMeasureTime();
                float audioStartDelay = emptyMeasureTime + currentChart.songInfo.audioOffset;
                
                if (audioStartDelay > 0)
                {
                    // 正の遅延: 指定秒数後に音声を再生開始
                    StartCoroutine(DelayedAudioPlay(audioStartDelay));
                    Debug.Log($"音声再生を{audioStartDelay:F3}秒後に開始予定 (空白小節: {emptyMeasureTime:F3}s + AudioOffset: {currentChart.songInfo.audioOffset:F3}s)");
                }
                else
                {
                    // 負または0の場合: 即座に再生開始（必要に応じて音声ファイルの途中から）
                    if (audioStartDelay < 0)
                    {
                        float startTime = Mathf.Abs(audioStartDelay);
                        if (startTime < audioSource.clip.length)
                        {
                            audioSource.time = startTime;
                        }
                    }
                    audioSource.Play();
                    Debug.Log($"音声即座再生開始 (Volume: {audioSource.volume:F2})");
                }
            }
            else
            {
                Debug.LogWarning("AudioClipが設定されていません");
            }
        }
        else
        {
            Debug.LogError("AudioSourceが見つかりません");
        }
    }

    IEnumerator DelayedAudioPlay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (audioSource != null && audioSource.clip != null)
        {
            audioSource.Play();
            Debug.Log($"遅延音声再生開始: {audioSource.clip.name} ({delay:F3}秒遅延)");
        }
    }

    public void StopAudio()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }

    void DebugLogChartInfo()
    {
        Debug.Log("=== 解析結果 ===");
        float emptyMeasureTime = GetEmptyMeasureTime();
        Debug.Log($"Audio Offset: {currentChart.songInfo.audioOffset}s");
        Debug.Log($"Audio File: {currentChart.songInfo.audioFile}");
        Debug.Log($"Volume: {currentChart.songInfo.volume}");
        
        Debug.Log("TimingPoints:");
        foreach (var tp in sortedTimingPoints)
        {
            float beatsPerMeasure = tp.GetBeatsPerMeasureIn4th();
            Debug.Log($"  {tp.GetPositionString()} - BPM: {tp.bpm}, 拍子: {tp.GetTimeSignatureString()} (4分音符換算: {beatsPerMeasure:F2}拍/小節)");
        }
        
        for (int i = 0; i < Mathf.Min(10, currentChart.notes.Count); i++)
        {
            var note = currentChart.notes[i];
            float noteTimeInBeats = note.GetTotalBeats();
            float noteTimeInSeconds = GetNoteTimeFromBeats(noteTimeInBeats);
            Debug.Log($"{note.GetMusicalDescription()} → Beats:{noteTimeInBeats:F3} Time:{noteTimeInSeconds:F3}s (Lane {note.lane})");
        }
    }
}