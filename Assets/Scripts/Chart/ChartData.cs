using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class ChartData
{
    public SongInfo songInfo;
    public List<TimingPoint> timingPoints;
    public List<Note> notes;

    public ChartData()
    {
        songInfo = new SongInfo();
        timingPoints = new List<TimingPoint>();
        notes = new List<Note>();
    }
}

[System.Serializable]
public class SongInfo
{
    public string title = "Unknown";
    public string artist = "Unknown";
    public string audioFile = "";
    public float audioOffset = 0f;
    public float volume = 1.0f;
    public float previewTime = 0f;
    public string difficulty = "Normal";
    public int level = 1;
    public int emptyMeasures = 1;
}

[System.Serializable]
public class TimingPoint
{
    public int measure = 1;
    public float beat = 0f;
    public float bpm = 120f;
    public int[] timeSignature = { 4, 4 };

    // 拍子を考慮した1小節あたりの4分音符数を計算
    public float GetBeatsPerMeasureIn4th()
    {
        // 分子 * (4 / 分母) = 4分音符換算での拍数
        return timeSignature[0] * (4f / timeSignature[1]);
    }

    public string GetPositionString()
    {
        return $"{measure}小節{beat + 1:F2}拍";
    }
    
    public string GetTimeSignatureString()
    {
        return $"{timeSignature[0]}/{timeSignature[1]}";
    }
}

[System.Serializable]
public class Note
{
    public int measure = 1;
    public float beat = 0f;
    public int lane;
    public string type = "tap";
    public float duration = 0f;

    public Note() { }
    public Note(int m, float b, int l, string noteType = "tap")
    {
        measure = m;
        beat = b;
        lane = l;
        type = noteType;
    }

    public float GetTotalBeats()
    {
        return (measure - 1) * 4f + beat;
    }
    public string GetPositionString()
    {
        return $"{measure}小節{beat + 1:F2}拍";
    }
    public string GetMusicalDescription()
    {
        string beatDesc = "";
        if (beat == 0f) beatDesc = "1拍目";
        else if (beat == 1f) beatDesc = "2拍目";
        else if (beat == 2f) beatDesc = "3拍目";
        else if (beat == 3f) beatDesc = "4拍目";
        else if (beat == 0.5f) beatDesc = "1拍半";
        else if (beat == 1.5f) beatDesc = "2拍半";
        else if (beat % 0.5f == 0) beatDesc = $"{beat + 1}拍目";
        else if (Mathf.Approximately(beat % 0.333f, 0f))
            beatDesc = $"{Mathf.FloorToInt(beat) + 1}拍+{((beat % 1f) / 0.333f):F0}/3";
        else beatDesc = $"{beat + 1:F3}拍";
        return $"{measure}小節{beatDesc}";
    }
}

public class ActiveNote
{
    public GameObject gameObject;
    public float hitTime;
    public int lane;
    public string type;
    public float duration;
    public string position;
    public NoteMovement movement;

    public ActiveNote(GameObject obj, float time, int laneNum, string noteType, string pos, float dur = 0f)
    {
        gameObject = obj;
        hitTime = time;
        lane = laneNum;
        type = noteType;
        position = pos;
        duration = dur;
        movement = obj?.GetComponent<NoteMovement>();
    }
}

public enum JudgmentResult
{
    None, Perfect, Good, Bad, Miss
}