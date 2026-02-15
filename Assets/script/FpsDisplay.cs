using UnityEngine;
using UnityEngine.UI;

public sealed class FpsDisplay : MonoBehaviour
{
    [SerializeField] private Text text;
    [SerializeField] private float updateInterval = 0.2f;

    private float _accum;
    private int _frames;
    private float _nextUpdate;

    private void Start()
    {
        if (text == null)
            CreateDefaultText();
        _nextUpdate = Time.realtimeSinceStartup + updateInterval;
    }

    private void CreateDefaultText()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        GameObject go = new GameObject("FPS");
        go.transform.SetParent(canvas.transform, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(12, -12);
        rt.sizeDelta = new Vector2(140, 36);

        text = go.AddComponent<Text>();
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font != null) text.font = font;
        text.fontSize = 28;
        text.color = Color.white;
    }

    private void Update()
    {
        _accum += Time.unscaledDeltaTime;
        _frames++;

        if (Time.realtimeSinceStartup < _nextUpdate || text == null) return;

        _nextUpdate = Time.realtimeSinceStartup + updateInterval;
        float fps = _frames / _accum;
        _frames = 0;
        _accum = 0f;

        text.text = $"FPS: {Mathf.RoundToInt(fps)}";
    }
}
