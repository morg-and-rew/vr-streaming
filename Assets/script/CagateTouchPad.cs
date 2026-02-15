using UnityEngine;
using UnityEngine.EventSystems;

public sealed class CagateTouchPad : MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IDragHandler,
    ICancelHandler
{
    public enum CoordScale
    {
        Scale10000 = 10000,
        Scale0x7FFF = 32767
    }

    [SerializeField] private CagateSlotController controller;
    [SerializeField] private RectTransform padRect;
    [SerializeField] private CoordScale coordScale = CoordScale.Scale10000;
    [SerializeField] private bool invertY;

    private const int MaxCoord = 10000;
    private const float MinSendInterval = 0.02f;
    private const int MinCoordDelta = 8;

    private bool _pressed;
    private int _pointerId = int.MinValue;
    private int _lastX = int.MinValue;
    private int _lastY = int.MinValue;
    private float _lastSendTime = -999f;

    private void Awake()
    {
        if (padRect == null) padRect = transform as RectTransform;
        transform.SetAsLastSibling();
    }

    private bool Ready => controller != null && controller.IsConnected && padRect != null;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!Ready) return;
        if (_pressed) return;
        _pressed = true;
        _pointerId = eventData.pointerId;
        if (TryGetCoords(eventData, out int x, out int y))
            SendTouch(x, y, 1, true);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!Ready || !_pressed || eventData.pointerId != _pointerId) return;
        if (!TryGetCoords(eventData, out int x, out int y)) return;
        if (!ShouldSend(x, y)) return;
        SendTouch(x, y, 1, false);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!Ready || !_pressed || eventData.pointerId != _pointerId) return;
        _pressed = false;
        if (TryGetCoords(eventData, out int x, out int y))
            SendTouch(x, y, 0, true);
        else if (_lastX != int.MinValue && _lastY != int.MinValue)
            SendSetTouchInternal(_lastX, _lastY, 0);
        _pointerId = int.MinValue;
    }

    public void OnCancel(BaseEventData eventData)
    {
        if (!_pressed) return;
        _pressed = false;
        if (_lastX != int.MinValue && _lastY != int.MinValue)
            SendSetTouchInternal(_lastX, _lastY, 0);
        _pointerId = int.MinValue;
    }

    private void OnDisable()
    {
        if (_pressed && _lastX != int.MinValue && _lastY != int.MinValue && controller != null && controller.IsConnected)
            SendSetTouchInternal(_lastX, _lastY, 0);
        _pressed = false;
        _pointerId = int.MinValue;
    }

    private bool TryGetCoords(PointerEventData e, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(padRect, e.position, e.pressEventCamera, out Vector2 local))
            return false;
        Rect rect = padRect.rect;
        float nx = Mathf.Clamp01(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x));
        float ny = Mathf.Clamp01(Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
        ny = 1f - ny;
        x = Mathf.Clamp(Mathf.RoundToInt(nx * MaxCoord), 0, MaxCoord);
        y = Mathf.Clamp(Mathf.RoundToInt(ny * MaxCoord), 0, MaxCoord);
        return true;
    }

    private bool ShouldSend(int x, int y)
    {
        if (MinSendInterval > 0f && (Time.unscaledTime - _lastSendTime) < MinSendInterval)
            return false;
        if (_lastX == int.MinValue || _lastY == int.MinValue)
            return true;
        return Mathf.Abs(x - _lastX) >= MinCoordDelta || Mathf.Abs(y - _lastY) >= MinCoordDelta;
    }

    private void SendTouch(int x, int y, int state, bool force)
    {
        _lastX = x;
        _lastY = y;
        _lastSendTime = Time.unscaledTime;
        SendSetTouchInternal(x, y, state);
    }

    private void SendSetTouchInternal(int x, int y, int state)
    {
        int scale = (int)coordScale;
        int sendX = scale == MaxCoord ? x : Mathf.Clamp(Mathf.RoundToInt((float)x * scale / MaxCoord), 0, scale);
        int sendY = scale == MaxCoord ? y : Mathf.Clamp(Mathf.RoundToInt((float)y * scale / MaxCoord), 0, scale);
        if (invertY) sendY = scale - sendY;
        controller.SendSetTouch(sendX, sendY, state);
    }
}
