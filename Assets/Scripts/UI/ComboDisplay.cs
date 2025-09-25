using UnityEngine;

public class ComboDisplay : MonoBehaviour
{
    [Header("コンボ数字表示設定")]
    public Font comboNumberFont; // コンボ数字用フォント
    public int comboNumberFontSize = 48; // 数字は大きく
    public Color comboNumberColor = Color.grey;

    [Header("COMBOテキスト表示設定")]
    public Font comboTextFont; // COMBOテキスト用フォント
    public int comboTextFontSize = 24; // テキストは小さく
    public Color comboTextColor = Color.grey;
    public float comboTextYOffset = 40f; // 数字からCOMBOテキストまでの距離

    [Header("コンボ表示設定")]
    public int minComboToShow = 3; // この数値以上でコンボ表示開始

    [Header("表示位置設定")]
    public Vector2 comboOffset = new Vector2(0, -100); // 画面中央からのオフセット

    private ScoreManager scoreManager;
    private RhythmGameController gameController;
    private GUIStyle comboNumberStyle;
    private GUIStyle comboTextStyle;

    void Awake()
    {
        scoreManager = FindObjectOfType<ScoreManager>();
        gameController = FindObjectOfType<RhythmGameController>();
    }

    void Start()
    {
        InitializeStyles();
    }

    void InitializeStyles()
    {
        // 数字表示用スタイル
        comboNumberStyle = new GUIStyle();
        comboNumberStyle.fontSize = comboNumberFontSize;
        comboNumberStyle.fontStyle = FontStyle.Bold;
        comboNumberStyle.alignment = TextAnchor.MiddleCenter;
        comboNumberStyle.normal.textColor = comboNumberColor;
        if (comboNumberFont != null) comboNumberStyle.font = comboNumberFont;

        // COMBOテキスト表示用スタイル
        comboTextStyle = new GUIStyle();
        comboTextStyle.fontSize = comboTextFontSize;
        comboTextStyle.fontStyle = FontStyle.Bold;
        comboTextStyle.alignment = TextAnchor.MiddleCenter;
        comboTextStyle.normal.textColor = comboTextColor;
        if (comboTextFont != null) comboTextStyle.font = comboTextFont;
    }

    void OnGUI()
    {
        // ゲーム中のみ表示
        if (!gameController.IsPlaying) return;

        int currentCombo = scoreManager.Combo;

        // 最小コンボ数以上の場合のみ表示
        if (currentCombo >= minComboToShow)
        {
            // 画面中央を基準とした位置計算
            float centerX = Screen.width / 2 + comboOffset.x;
            float centerY = Screen.height / 2 + comboOffset.y;

            // 数字表示（大きく）
            GUI.Label(new Rect(centerX - 100, centerY, 200, 60), currentCombo.ToString(), comboNumberStyle);

            // COMBOテキスト表示（小さく、数字の下）
            GUI.Label(new Rect(centerX - 100, centerY + comboTextYOffset, 200, 40), "COMBO", comboTextStyle);
        }
    }
}