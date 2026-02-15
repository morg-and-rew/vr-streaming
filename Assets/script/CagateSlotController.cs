using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public sealed class CagateSlotController : MonoBehaviour
{
    private const string SlotURL = "ws://ghost-wheel.ru:50141/j-rpc/play";

    [SerializeField] private Button startButton;

    private ClientWebSocket _webSocket;
    private CancellationTokenSource _cts;
    private int _nextId;

    public bool IsConnected => _webSocket != null && _webSocket.State == WebSocketState.Open;

    private void OnEnable()
    {
        _cts = new CancellationTokenSource();
        _ = ConnectAsync();
    }

    private void OnDisable()
    {
        try { _cts?.Cancel(); } catch { }
        try { _webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).GetAwaiter().GetResult(); } catch { }
        try { _webSocket?.Dispose(); } catch { }
        _webSocket = null;
        _cts = null;
    }

    private void Start()
    {
        if (startButton == null)
        {
            Button btn = FindObjectOfType<Button>();
            if (btn != null) startButton = btn;
        }
        if (startButton != null)
            startButton.onClick.AddListener(OnStartClick);
        RefreshButton();
    }

    private void OnDestroy()
    {
        if (startButton != null)
            startButton.onClick.RemoveListener(OnStartClick);
    }

    private void RefreshButton()
    {
        if (startButton != null)
            startButton.interactable = IsConnected;
    }

    public void OnStartClick()
    {
        if (startButton == null || !IsConnected) return;
        _ = SendSetButtonClickAsync();
    }

    public async Task SendSetButtonClickAsync()
    {
        if (!IsConnected) return;
        SendSetButton(0, 1);
        await Task.Delay(TimeSpan.FromSeconds(0.25), _cts?.Token ?? default).ConfigureAwait(false);
        SendSetButton(0, 0);
    }

    private void SendSetButton(int key, int state)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open) return;
        int id = Interlocked.Increment(ref _nextId);
        string message = $"{{\"method\":\"setbutton\",\"params\":{{\"key\":{key},\"state\":{state}}},\"id\":{id}}}";
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        try
        {
            _ = _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? default);
        }
        catch (Exception e)
        {
            Debug.LogWarning(e.Message);
        }
    }

    public void SendSetTouch(int x, int y, int state)
    {
        if (_webSocket == null || _webSocket.State != WebSocketState.Open) return;
        int id = Interlocked.Increment(ref _nextId);
        string message = $"{{\"method\":\"settouch\",\"params\":{{\"x\":{x},\"y\":{y},\"state\":{state}}},\"id\":{id}}}";
        byte[] bytes = Encoding.UTF8.GetBytes(message);
        try
        {
            _ = _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? default);
        }
        catch (Exception e)
        {
            Debug.LogWarning(e.Message);
        }
    }

    private async Task ConnectAsync()
    {
        _webSocket = new ClientWebSocket();
        try
        {
            await _webSocket.ConnectAsync(new Uri(SlotURL), _cts.Token).ConfigureAwait(false);
            _ = ReceiveLoopAsync(_cts.Token);
        }
        catch (Exception)
        {
            try { _webSocket?.Dispose(); } catch { }
            _webSocket = null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        StringBuilder sb = new StringBuilder(4096);

        while (!ct.IsCancellationRequested && _webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            try
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception)
            {
                try { _webSocket?.Dispose(); } catch { }
                _webSocket = null;
                return;
            }
        }
    }

    private void Update()
    {
        RefreshButton();
    }
}
