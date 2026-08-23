using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public enum UdpReceiveState
{
    Stopped,
    Waiting,
    Receiving,
    SignalLost,
    PortError
}

public class UDPReceiver : MonoBehaviour
{
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly object packetLock = new object();

    public static UDPReceiver Instance { get; private set; }
    public static string LatestData { get; private set; } = "";

    [Header("연결 설정")]
    [Tooltip("수신할 포트 번호 (iFacialMocap 기본값: 49983)")]
    public int port = 49983;

    [Tooltip("특정 IP 주소만 허용 (비어있으면 모든 IP 허용)")]
    public string targetIPAddress = "";

    [Tooltip("최초 정상 송신 IP를 잠그고 다른 송신자의 패킷을 무시합니다.")]
    public bool lockFirstSender = true;

    [Tooltip("이 시간 동안 패킷이 없으면 신호 끊김으로 처리합니다.")]
    [Min(0.1f)]
    public float signalTimeout = 1.0f;

    [Header("최근 데이터")]
    [Tooltip("가장 최근에 적용한 정상 패킷")]
    [TextArea(5, 20)]
    public string receivedLog = "수신 기록 없음";

    public event Action<string> OnDataReceived;

    public UdpReceiveState ReceiveState => receiveState;
    public string CurrentStatus => GetStatusText(receiveState);
    public string StatusDetail => statusDetail;
    public bool IsRunning => isRunning;
    public bool IsReceivingPackets => receiveState == UdpReceiveState.Receiving;
    public string LastReceivedAt => lastReceivedAt;
    public string SenderIPAddress => lastSenderIPAddress;
    public string LockedSenderIPAddress => lockedSenderIPAddress;
    public string SenderWarning => senderWarning;
    public float PacketsPerSecond => packetsPerSecond;
    public long TotalReceivedPackets => Interlocked.Read(ref totalReceivedPacketCounter);
    public long TotalAppliedPackets => Interlocked.Read(ref totalAppliedPacketCounter);
    public long TotalIgnoredPackets => Interlocked.Read(ref totalIgnoredPacketCounter);
    public long TotalInvalidPackets => Interlocked.Read(ref totalInvalidPacketCounter);
    public long TotalSupersededPackets => Interlocked.Read(ref totalSupersededPacketCounter);
    public string EstimatedLatency => "측정 불가 (송신 타임스탬프 없음)";

    [Obsolete("UDP는 연결 상태가 없으므로 IsReceivingPackets를 사용하세요.")]
    public bool IsConnected => IsReceivingPackets;

    private UdpReceiveState receiveState = UdpReceiveState.Stopped;
    private string statusDetail = "수신이 중지되어 있습니다.";
    private string lastReceivedAt = "수신 기록 없음";
    private string lastSenderIPAddress = "수신 기록 없음";
    private string lockedSenderIPAddress = "잠금되지 않음";
    private string senderWarning = "없음";
    private float packetsPerSecond;

    private Thread receiveThread;
    private UdpClient client;
    private volatile bool isRunning;
    private string configuredTargetIPAddress = "";
    private float lastReceivedRealtime = float.NegativeInfinity;
    private float lastPpsSampleRealtime;
    private long lastPpsPacketCount;

    private string pendingPacket;
    private string pendingSenderIPAddress;
    private string pendingLockedSenderIPAddress;
    private DateTime pendingReceivedUtc;
    private bool hasPendingPacket;
    private string lockedSenderOnReceiveThread = "";
    private string pendingSenderWarning;
    private string pendingReceiveError;

    private long totalReceivedPacketCounter;
    private long totalAppliedPacketCounter;
    private long totalIgnoredPacketCounter;
    private long totalInvalidPacketCounter;
    private long totalSupersededPacketCounter;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        lastPpsSampleRealtime = Time.realtimeSinceStartup;
    }

    private void Start()
    {
        StartConnection();
    }

    private void Update()
    {
        UpdatePacketsPerSecond();
        ConsumeSenderWarning();

        if (TryTakeLatestPacket(
                out string packet,
                out string senderIP,
                out string lockedSenderIP,
                out DateTime receivedUtc))
        {
            ApplyLatestPacket(packet, senderIP, lockedSenderIP, receivedUtc);
        }

        if (TryTakeReceiveError(out string receiveError))
        {
            HandleReceiveError(receiveError);
            return;
        }

        if (receiveState == UdpReceiveState.Receiving &&
            Time.realtimeSinceStartup - lastReceivedRealtime > Mathf.Max(0.1f, signalTimeout))
        {
            SetState(
                UdpReceiveState.SignalLost,
                $"{signalTimeout:0.##}초 동안 정상 패킷이 없습니다.");
            Debug.LogWarning($"[UDP] {signalTimeout:0.##}초 동안 패킷이 없어 신호가 끊겼습니다.");
        }
    }

    private void OnApplicationQuit()
    {
        StopConnection();
    }

    private void OnDestroy()
    {
        StopConnection();
        if (Instance == this) Instance = null;
    }

    #region 연결 제어
    public void StartConnection()
    {
        if (isRunning) return;

        ReleaseTransport();
        ResetSessionState();

        if (port < IPEndPoint.MinPort || port > IPEndPoint.MaxPort)
        {
            SetPortError($"포트 번호는 {IPEndPoint.MinPort}~{IPEndPoint.MaxPort} 범위여야 합니다.");
            return;
        }

        if (!TryResolveTargetIPAddress(out configuredTargetIPAddress))
        {
            SetPortError($"허용 IP 주소 형식이 올바르지 않습니다: {targetIPAddress}");
            return;
        }

        try
        {
            client = new UdpClient(port);
            isRunning = true;
            receiveThread = new Thread(ReceiveData)
            {
                IsBackground = true,
                Name = "iFacialMocap UDP Receiver"
            };
            receiveThread.Start();

            SetState(UdpReceiveState.Waiting, $"{port}번 포트에서 정상 패킷을 기다리는 중입니다.");
            Debug.Log($"[UDP] {port}번 포트에서 수신 대기를 시작합니다.");
        }
        catch (SocketException exception)
        {
            ReleaseTransport();
            SetPortError(FormatSocketError(exception));
        }
        catch (Exception exception)
        {
            ReleaseTransport();
            SetPortError($"수신 시작에 실패했습니다: {exception.Message}");
        }
    }

    public void StopConnection()
    {
        bool wasActive = isRunning || client != null || receiveThread != null;
        isRunning = false;
        ReleaseTransport();

        lock (packetLock)
        {
            hasPendingPacket = false;
            pendingPacket = null;
            pendingSenderIPAddress = null;
            pendingLockedSenderIPAddress = null;
            pendingReceiveError = null;
        }

        SetState(UdpReceiveState.Stopped, "사용자가 수신을 중지했습니다.");
        packetsPerSecond = 0f;

        if (wasActive)
            Debug.Log("[UDP] 연결을 종료합니다.");
    }

    public void RestartConnection()
    {
        StopConnection();
        StartConnection();
    }
    #endregion

    #region 백그라운드 수신
    private void ReceiveData()
    {
        UdpClient activeClient = client;

        while (isRunning)
        {
            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = activeClient.Receive(ref remoteEndPoint);
                Interlocked.Increment(ref totalReceivedPacketCounter);

                string senderIP = remoteEndPoint.Address.ToString();
                if (!IsAllowedSender(senderIP))
                {
                    Interlocked.Increment(ref totalIgnoredPacketCounter);
                    continue;
                }

                string text;
                try
                {
                    text = StrictUtf8.GetString(data);
                }
                catch (DecoderFallbackException)
                {
                    Interlocked.Increment(ref totalInvalidPacketCounter);
                    continue;
                }

                if (!IsValidMocapPacket(text))
                {
                    Interlocked.Increment(ref totalInvalidPacketCounter);
                    continue;
                }

                if (!TryStoreLatestPacket(text, senderIP, DateTime.UtcNow))
                {
                    Interlocked.Increment(ref totalIgnoredPacketCounter);
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception)
            {
                if (isRunning) QueueReceiveError(FormatSocketError(exception));
                break;
            }
            catch (Exception exception)
            {
                if (isRunning) QueueReceiveError($"수신 처리 중 오류가 발생했습니다: {exception.Message}");
                break;
            }
        }
    }

    private bool IsAllowedSender(string senderIP)
    {
        if (string.IsNullOrEmpty(configuredTargetIPAddress) ||
            string.Equals(senderIP, configuredTargetIPAddress, StringComparison.Ordinal))
        {
            return true;
        }

        QueueSenderWarning(
            $"허용 IP {configuredTargetIPAddress}가 아닌 {senderIP}의 패킷을 무시했습니다.");
        return false;
    }

    private bool TryStoreLatestPacket(string text, string senderIP, DateTime receivedUtc)
    {
        lock (packetLock)
        {
            if (lockFirstSender)
            {
                if (string.IsNullOrEmpty(lockedSenderOnReceiveThread))
                {
                    lockedSenderOnReceiveThread = senderIP;
                }
                else if (!string.Equals(
                             lockedSenderOnReceiveThread,
                             senderIP,
                             StringComparison.Ordinal))
                {
                    pendingSenderWarning =
                        $"송신자가 {lockedSenderOnReceiveThread}에서 {senderIP}(으)로 변경되어 패킷을 무시했습니다.";
                    return false;
                }
            }

            if (hasPendingPacket)
                Interlocked.Increment(ref totalSupersededPacketCounter);

            pendingPacket = text;
            pendingSenderIPAddress = senderIP;
            pendingLockedSenderIPAddress = lockFirstSender
                ? lockedSenderOnReceiveThread
                : "잠금 사용 안 함";
            pendingReceivedUtc = receivedUtc;
            hasPendingPacket = true;
            return true;
        }
    }
    #endregion

    #region 메인 스레드 적용
    private bool TryTakeLatestPacket(
        out string packet,
        out string senderIP,
        out string lockedSenderIP,
        out DateTime receivedUtc)
    {
        lock (packetLock)
        {
            if (!hasPendingPacket)
            {
                packet = null;
                senderIP = null;
                lockedSenderIP = null;
                receivedUtc = default;
                return false;
            }

            packet = pendingPacket;
            senderIP = pendingSenderIPAddress;
            lockedSenderIP = pendingLockedSenderIPAddress;
            receivedUtc = pendingReceivedUtc;
            hasPendingPacket = false;
            return true;
        }
    }

    private void ApplyLatestPacket(
        string packet,
        string senderIP,
        string lockedSenderIP,
        DateTime receivedUtc)
    {
        UdpReceiveState previousState = receiveState;
        double packetAgeSeconds = Math.Max(0d, (DateTime.UtcNow - receivedUtc).TotalSeconds);

        LatestData = packet;
        receivedLog = packet.Replace("|", "\n");
        lastReceivedRealtime = Time.realtimeSinceStartup - (float)packetAgeSeconds;
        lastReceivedAt = receivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        lastSenderIPAddress = senderIP;
        lockedSenderIPAddress = lockedSenderIP;
        senderWarning = "없음";
        Interlocked.Increment(ref totalAppliedPacketCounter);

        SetState(UdpReceiveState.Receiving, $"{senderIP}의 최신 패킷을 적용 중입니다.");
        if (previousState != UdpReceiveState.Receiving)
            Debug.Log($"[UDP] {senderIP}로부터 정상 패킷 수신을 시작했습니다.");

        OnDataReceived?.Invoke(packet);
    }

    private void UpdatePacketsPerSecond()
    {
        float now = Time.realtimeSinceStartup;
        float elapsed = now - lastPpsSampleRealtime;
        if (elapsed < 0.5f) return;

        long currentPacketCount = Interlocked.Read(ref totalReceivedPacketCounter);
        packetsPerSecond = (currentPacketCount - lastPpsPacketCount) / elapsed;
        lastPpsPacketCount = currentPacketCount;
        lastPpsSampleRealtime = now;
    }

    private void ConsumeSenderWarning()
    {
        string warning;
        lock (packetLock)
        {
            warning = pendingSenderWarning;
            pendingSenderWarning = null;
        }

        if (string.IsNullOrEmpty(warning) || string.Equals(senderWarning, warning, StringComparison.Ordinal))
            return;

        senderWarning = warning;
        Debug.LogWarning($"[UDP] {warning}");
    }

    private void HandleReceiveError(string error)
    {
        isRunning = false;
        ReleaseTransport();
        SetPortError(error);
    }
    #endregion

    #region 상태 및 검증
    private void SetState(UdpReceiveState state, string detail)
    {
        receiveState = state;
        statusDetail = detail;
    }

    private void SetPortError(string detail)
    {
        isRunning = false;
        packetsPerSecond = 0f;
        SetState(UdpReceiveState.PortError, detail);
        Debug.LogError($"[UDP] {detail}");
    }

    private void ResetSessionState()
    {
        lock (packetLock)
        {
            hasPendingPacket = false;
            lockedSenderOnReceiveThread = "";
            pendingSenderWarning = null;
            pendingReceiveError = null;
        }

        lastReceivedRealtime = float.NegativeInfinity;
        lastReceivedAt = "수신 기록 없음";
        lastSenderIPAddress = "수신 기록 없음";
        lockedSenderIPAddress = lockFirstSender ? "정상 패킷 대기 중" : "잠금 사용 안 함";
        senderWarning = "없음";
        receivedLog = "수신 기록 없음";
        packetsPerSecond = 0f;
        lastPpsPacketCount = TotalReceivedPackets;
        lastPpsSampleRealtime = Time.realtimeSinceStartup;
    }

    private bool TryResolveTargetIPAddress(out string resolvedIPAddress)
    {
        string input = targetIPAddress?.Trim();
        if (string.IsNullOrEmpty(input))
        {
            resolvedIPAddress = "";
            return true;
        }

        if (IPAddress.TryParse(input, out IPAddress parsedAddress))
        {
            resolvedIPAddress = parsedAddress.ToString();
            return true;
        }

        resolvedIPAddress = "";
        return false;
    }

    private static bool IsValidMocapPacket(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("|"))
            return false;

        string[] values = text.Split('|');
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            int headIndex = value.IndexOf("head#", StringComparison.Ordinal);
            if (headIndex >= 0 && headIndex + 5 < value.Length)
                return true;

            int separatorIndex = value.LastIndexOf('-');
            if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
                continue;

            if (float.TryParse(
                    value.Substring(separatorIndex + 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetStatusText(UdpReceiveState state)
    {
        switch (state)
        {
            case UdpReceiveState.Waiting: return "대기 중";
            case UdpReceiveState.Receiving: return "수신 중";
            case UdpReceiveState.SignalLost: return "신호 끊김";
            case UdpReceiveState.PortError: return "포트 오류";
            default: return "중지됨";
        }
    }

    private static string FormatSocketError(SocketException exception)
    {
        switch (exception.SocketErrorCode)
        {
            case SocketError.AddressAlreadyInUse:
                return "포트가 이미 다른 프로그램에서 사용 중입니다.";
            case SocketError.AccessDenied:
                return "포트를 열 권한이 없습니다.";
            case SocketError.AddressNotAvailable:
                return "현재 장치에서 사용할 수 없는 네트워크 주소입니다.";
            default:
                return $"소켓 오류가 발생했습니다: {exception.Message}";
        }
    }

    private void QueueSenderWarning(string warning)
    {
        lock (packetLock)
        {
            pendingSenderWarning = warning;
        }
    }

    private void QueueReceiveError(string error)
    {
        lock (packetLock)
        {
            pendingReceiveError = error;
        }

        isRunning = false;
    }

    private bool TryTakeReceiveError(out string error)
    {
        lock (packetLock)
        {
            error = pendingReceiveError;
            pendingReceiveError = null;
            return !string.IsNullOrEmpty(error);
        }
    }

    private void ReleaseTransport()
    {
        client?.Close();

        if (receiveThread != null &&
            receiveThread.IsAlive &&
            Thread.CurrentThread != receiveThread)
        {
            receiveThread.Join(1000);
        }

        receiveThread = null;
        client = null;
    }
    #endregion
}
