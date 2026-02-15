using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public sealed class WebRtcHttpAutoReceiver : MonoBehaviour
{
    [Header("Output")]
    [SerializeField] private RawImage rawImage;

    [Header("WebRTC (WHEP)")]
    [SerializeField] private string webrtcWhepUrl = "http://ghost-wheel.ru:50157/wrtc-main/whep";
    [SerializeField] private string bearerToken = "";

    [Header("Behavior")]
    [SerializeField] private bool preferH264 = true;
    [SerializeField, Min(1f)] private float httpTimeoutSec = 6f;

    private RTCPeerConnection _pc;
    private MediaStream _stream;

    private bool _started;
    private Coroutine _updateCoroutine;
    private Coroutine _runCoroutine;

    private void Start()
    {
        if (_started) return;
        _started = true;

        if (rawImage == null)
        {
            Debug.LogError($"{nameof(WebRtcHttpAutoReceiver)}: RawImage is not assigned.");
            return;
        }

        if (string.IsNullOrWhiteSpace(webrtcWhepUrl))
        {
            Debug.LogError($"{nameof(WebRtcHttpAutoReceiver)}: WHEP url is empty.");
            return;
        }

        _updateCoroutine = StartCoroutine(WebRTC.Update());
        _runCoroutine = StartCoroutine(Run());
    }

    private void OnDestroy()
    {
        if (_updateCoroutine != null) StopCoroutine(_updateCoroutine);
        if (_runCoroutine != null) StopCoroutine(_runCoroutine);

        Cleanup();
    }

    private IEnumerator Run()
    {
        CreatePeer();

        RTCSessionDescriptionAsyncOperation offerOp = _pc.CreateOffer();
        yield return offerOp;
        if (offerOp.IsError)
        {
            Debug.LogError($"CreateOffer error: {offerOp.Error.message}");
            yield break;
        }

        RTCSessionDescription offer = offerOp.Desc;

        RTCSetSessionDescriptionAsyncOperation setLocalOp = _pc.SetLocalDescription(ref offer);
        yield return setLocalOp;
        if (setLocalOp.IsError)
        {
            Debug.LogError($"SetLocalDescription error: {setLocalOp.Error.message}");
            yield break;
        }

        string answerSdp = null;
        string failReason = null;

        yield return PostOffer(
            webrtcWhepUrl.TrimEnd('/'),
            offer.sdp,
            sdp => answerSdp = sdp,
            reason => failReason = reason
        );

        if (string.IsNullOrWhiteSpace(answerSdp))
        {
            Debug.LogError($"WHEP POST failed: {failReason ?? "unknown"}");
            yield break;
        }

        yield return ApplyAnswer(answerSdp);
    }

    private void CreatePeer()
    {
        RTCConfiguration config = new RTCConfiguration { iceServers = Array.Empty<RTCIceServer>() };

        _pc = new RTCPeerConnection(ref config);
        _stream = new MediaStream();

        _pc.OnTrack = e => _stream.AddTrack(e.Track);

        _stream.OnAddTrack = e =>
        {
            if (e.Track is VideoStreamTrack vt)
                vt.OnVideoReceived += tex => rawImage.texture = tex;
        };

        RTCRtpTransceiverInit init = new RTCRtpTransceiverInit { direction = RTCRtpTransceiverDirection.RecvOnly };
        RTCRtpTransceiver transceiver = _pc.AddTransceiver(TrackKind.Video, init);

        if (preferH264)
            PreferH264(transceiver);
    }

    private static void PreferH264(RTCRtpTransceiver transceiver)
    {
        RTCRtpCapabilities caps = RTCRtpReceiver.GetCapabilities(TrackKind.Video);

        RTCRtpCodecCapability[] h264 = caps.codecs
            .Where(c => !string.IsNullOrEmpty(c.mimeType) &&
                        c.mimeType.IndexOf("video/h264", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();

        if (h264.Length == 0) return;

        RTCRtpCodecCapability[] rest = caps.codecs
            .Where(c => string.IsNullOrEmpty(c.mimeType) ||
                        c.mimeType.IndexOf("video/h264", StringComparison.OrdinalIgnoreCase) < 0)
            .ToArray();

        RTCRtpCodecCapability[] ordered = h264.Concat(rest).ToArray();

        MethodInfo m = transceiver.GetType().GetMethod(
            "SetCodecPreferences",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (m == null) return;

        try { m.Invoke(transceiver, new object[] { ordered }); }
        catch { }
    }

    private IEnumerator PostOffer(string url, string offerSdp, Action<string> onSuccess, Action<string> onFail)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.timeout = Mathf.CeilToInt(httpTimeoutSec);

            byte[] bodyRaw = Encoding.UTF8.GetBytes(offerSdp);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();

            req.SetRequestHeader("Content-Type", "application/sdp");
            req.SetRequestHeader("Accept", "application/sdp");

            if (!string.IsNullOrWhiteSpace(bearerToken))
                req.SetRequestHeader("Authorization", "Bearer " + bearerToken);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string detailedError = $"HTTP {req.responseCode}: {req.error}";
                if (req.downloadHandler != null && !string.IsNullOrEmpty(req.downloadHandler.text))
                    detailedError += $"\nServer response: {req.downloadHandler.text}";
                onFail?.Invoke(detailedError);
                yield break;
            }

            string text = req.downloadHandler?.text ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                onFail?.Invoke("empty response body");
                yield break;
            }

            if (!text.Contains("v=0"))
            {
                onFail?.Invoke($"response is not SDP (first 80 chars): {text.Substring(0, Math.Min(80, text.Length))}");
                yield break;
            }

            onSuccess?.Invoke(text);
        }
    }

    private IEnumerator ApplyAnswer(string answerSdp)
    {
        RTCSessionDescription answer = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = answerSdp };
        RTCSetSessionDescriptionAsyncOperation op = _pc.SetRemoteDescription(ref answer);
        yield return op;

        if (op.IsError)
            Debug.LogError($"SetRemoteDescription error: {op.Error.message}");
    }

    private void Cleanup()
    {
        try
        {
            if (_stream != null)
            {
                foreach (MediaStreamTrack t in _stream.GetTracks())
                    t.Dispose();
                _stream.Dispose();
                _stream = null;
            }
        }
        catch { }

        try
        {
            if (_pc != null)
            {
                _pc.Close();
                _pc.Dispose();
                _pc = null;
            }
        }
        catch { }
    }
}
