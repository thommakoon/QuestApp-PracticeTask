using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// TCP client for the OpenEye PC GUI (Neon + mapping). Receives mapped gaze on a z=1m plane.
/// PC runs the server on port 5051; Quest connects as client.
/// </summary>
public class OpenEyeGazeReceiver : MonoBehaviour
{
    [Header("PC running openeye-quest-gui")]
    public string serverIp = "192.168.0.245";
    public int serverPort = 5051;

    [Header("Connection")]
    [SerializeField] bool autoConnectOnStart = true;
    [SerializeField] bool autoReconnect = true;
    [SerializeField] float reconnectIntervalSec = 2f;

    public enum State { Disconnected, Connecting, Connected, Failed }
    public State CurrentState { get; private set; } = State.Disconnected;

    public struct GazeSample
    {
        /// <summary>Neon / OpenEye timestamp in unix seconds (payload.t).</summary>
        public double timestamp;
        /// <summary>Unix epoch ns from Neon (same units as gaze.csv). 0 if absent.</summary>
        public long timestampNs;
        /// <summary>Quest wall-clock unix ms when this TCP packet was received.</summary>
        public long questReceivedUnixMs;
        public Vector2 planeMeters;
        public bool valid;
    }

    [Serializable] class MsgTypeOnly { public string type; }
    [Serializable] class PayloadGaze { public double t; public long t_ns; public float x; public float y; }
    [Serializable] class PayloadLaunch { public string package; }
    [Serializable] class PayloadSyncPulse { public int seq; public long quest_sent_unix_ms; }
    [Serializable] class MsgGaze { public string type; public PayloadGaze payload; }
    [Serializable] class MsgLaunch { public string type; public PayloadLaunch payload; }
    [Serializable] class MsgSyncPulse { public string type; public PayloadSyncPulse payload; }

    public event Action<string> OnLaunchApp;

    volatile bool _pendingLaunchApp;
    volatile string _pendingLaunchPackage = "";

    TcpClient _client;
    NetworkStream _stream;
    Thread _recvThread;
    CancellationTokenSource _cts;
    readonly object _sendLock = new object();

    static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    const int GazeBufCap = 8;
    readonly object _gazeLock = new object();
    readonly GazeSample[] _gazeBuf = new GazeSample[GazeBufCap];
    int _gazeHead;
    long _gazeSeq;

    public long GazeSequence => _gazeSeq;
    public bool HasGaze => _gazeSeq > 0;

    void Start()
    {
        if (GetComponent<OpenEyeHandoff>() == null)
            gameObject.AddComponent<OpenEyeHandoff>();
        if (GetComponent<OpenEyeSyncCalibration>() == null)
            gameObject.AddComponent<OpenEyeSyncCalibration>();

        if (autoConnectOnStart)
            _ = StartConnectLoop();
    }

    void Update()
    {
        if (_pendingLaunchApp)
        {
            _pendingLaunchApp = false;
            string package = _pendingLaunchPackage ?? "";
            OnLaunchApp?.Invoke(package);
        }
    }

    void OnDestroy() => Disconnect();

    void OnApplicationQuit() => Disconnect();

    async Task StartConnectLoop()
    {
        while (autoReconnect && Application.isPlaying)
        {
            if (CurrentState == State.Disconnected || CurrentState == State.Failed)
                Connect();

            try { await Task.Delay(TimeSpan.FromSeconds(reconnectIntervalSec)); }
            catch { }
        }
    }

    public void Connect()
    {
        if (CurrentState == State.Connecting || CurrentState == State.Connected)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = ConnectAsync(_cts.Token);
    }

    public void Disconnect()
    {
        try { _cts?.Cancel(); } catch { }
        try { _recvThread?.Join(100); } catch { }
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        SetState(State.Disconnected);
    }

    async Task ConnectAsync(CancellationToken token)
    {
        SetState(State.Connecting);
        try
        {
            _client = new TcpClient { NoDelay = true };
            using (token.Register(() => { try { _client?.Close(); } catch { } }))
            {
                await _client.ConnectAsync(serverIp, serverPort);
            }

            if (!_client.Connected)
                throw new Exception("connect failed");

            _stream = _client.GetStream();
            SetState(State.Connected);
            _recvThread = new Thread(() => ReceiveLoop(token)) { IsBackground = true };
            _recvThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OpenEye] connect error: {e.Message}");
            SetState(State.Failed);
        }
    }

    void ReceiveLoop(CancellationToken token)
    {
        var headerBuf = new byte[4];
        try
        {
            while (!token.IsCancellationRequested)
            {
                ReadExact(_stream, headerBuf, 0, 4);
                int len = (headerBuf[0] << 24) | (headerBuf[1] << 16) | (headerBuf[2] << 8) | headerBuf[3];
                if (len <= 0 || len > 10_000_000)
                    throw new Exception($"invalid length: {len}");

                var payload = new byte[len];
                ReadExact(_stream, payload, 0, len);
                string json = Encoding.UTF8.GetString(payload);

                var head = JsonUtility.FromJson<MsgTypeOnly>(json);
                if (head == null || string.IsNullOrEmpty(head.type))
                    continue;

                if (head.type == "launchApp")
                {
                    var msg = JsonUtility.FromJson<MsgLaunch>(json);
                    _pendingLaunchPackage = msg?.payload?.package ?? "";
                    _pendingLaunchApp = true;
                    continue;
                }

                if (head.type != "gazeVisual")
                    continue;

                var gazeMsg = JsonUtility.FromJson<MsgGaze>(json);
                if (gazeMsg?.payload == null)
                    continue;

                long questMs = (long)(DateTime.UtcNow - UnixEpochUtc).TotalMilliseconds;
                EnqueueGaze(new GazeSample
                {
                    timestamp = gazeMsg.payload.t,
                    timestampNs = gazeMsg.payload.t_ns,
                    questReceivedUnixMs = questMs,
                    planeMeters = new Vector2(gazeMsg.payload.x, gazeMsg.payload.y),
                    valid = true,
                });
            }
        }
        catch (Exception e)
        {
            if (!token.IsCancellationRequested)
                Debug.LogWarning($"[OpenEye] recv ended: {e.Message}");
        }

        Disconnect();
    }

    static void ReadExact(NetworkStream stream, byte[] buf, int off, int len)
    {
        int got = 0;
        while (got < len)
        {
            int r = stream.Read(buf, off + got, len - got);
            if (r <= 0)
                throw new Exception("socket closed");
            got += r;
        }
    }

    void EnqueueGaze(GazeSample sample)
    {
        lock (_gazeLock)
        {
            _gazeBuf[_gazeHead] = sample;
            _gazeHead = (_gazeHead + 1) % GazeBufCap;
            _gazeSeq++;
        }
    }

    public bool TrySendSyncPulse(int seq, out long questSentUnixMs)
    {
        questSentUnixMs = 0;
        if (_stream == null || CurrentState != State.Connected)
            return false;

        questSentUnixMs = (long)(DateTime.UtcNow - UnixEpochUtc).TotalMilliseconds;
        var msg = new MsgSyncPulse
        {
            type = "syncPulse",
            payload = new PayloadSyncPulse
            {
                seq = seq,
                quest_sent_unix_ms = questSentUnixMs,
            },
        };
        return TrySendJson(JsonUtility.ToJson(msg));
    }

    bool TrySendJson(string json)
    {
        if (_stream == null)
            return false;

        try
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            if (payload.Length <= 0 || payload.Length > 10_000_000)
                return false;

            byte[] header = new byte[4];
            int len = payload.Length;
            header[0] = (byte)((len >> 24) & 0xFF);
            header[1] = (byte)((len >> 16) & 0xFF);
            header[2] = (byte)((len >> 8) & 0xFF);
            header[3] = (byte)(len & 0xFF);

            lock (_sendLock)
            {
                _stream.Write(header, 0, 4);
                _stream.Write(payload, 0, payload.Length);
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OpenEye] send error: {e.Message}");
            return false;
        }
    }

    public bool TryGetLatestGaze(ref long lastSeenSeq, out GazeSample sample)
    {
        lock (_gazeLock)
        {
            if (_gazeSeq == lastSeenSeq)
            {
                sample = default;
                return false;
            }

            int latestIdx = (_gazeHead - 1 + GazeBufCap) % GazeBufCap;
            sample = _gazeBuf[latestIdx];
            lastSeenSeq = _gazeSeq;
            return sample.valid;
        }
    }

    void SetState(State state)
    {
        CurrentState = state;
        Debug.Log($"[OpenEye] state = {state}");
    }
}
