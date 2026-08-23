using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UDPReceiver : MonoBehaviour
{
    // 싱글톤 인스턴스
    public static UDPReceiver Instance { get; private set; }

    [Header("연결 설정")]
    [Tooltip("수신할 포트 번호 (iFacialMocap 기본값: 49983)")]
    public int port = 49983;

    [Tooltip("특정 IP 주소만 허용 (비어있으면 모든 IP 허용)")]
    public string targetIPAddress = "";

    [Tooltip("이 시간 동안 패킷이 없으면 신호 끊김으로 처리합니다.")]
    [Min(0.1f)]
    public float signalTimeout = 1.0f;

    [Header("수신 상태 모니터링")]
    [Tooltip("현재 연결 상태")]
    public bool IsConnected = false;

    [SerializeField, Tooltip("현재 UDP 수신 상태")]
    private string currentStatus = "대기 중";

    [SerializeField, Tooltip("마지막 정상 패킷 수신 시각")]
    private string lastReceivedAt = "수신 기록 없음";

    [SerializeField, Tooltip("제한 시간 안에 패킷을 수신했는지 여부")]
    private bool isReceivingPackets = false;

    [Tooltip("수신된 데이터 로그")]
    [TextArea(5, 100)]
    public string receivedLog = "대기중...";

    // 외부에서 구독 가능한 이벤트
    public event Action<string> OnDataReceived;

    // 프로퍼티
    public static string LatestData { get; private set; } = "";
    public bool IsReceivingPackets => isReceivingPackets;
    public string LastReceivedAt => lastReceivedAt;

    // 내부 변수
    private Thread receiveThread;
    private UdpClient client;
    private volatile bool isRunning = false;
    private float lastReceivedRealtime = float.NegativeInfinity;

    // 메인 스레드 처리를 위한 헬퍼
    private UnityMainThreadDispatcher mainThreadDispatcher;

    private void Awake()
    {
        // 싱글톤 초기화
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Dispatcher 초기화 (OrcController 스타일로 내부 컴포넌트 처리)
        mainThreadDispatcher = UnityMainThreadDispatcher.Instance();
    }

    private void Start()
    {
        StartConnection();
    }

    private void Update()
    {
        if (isReceivingPackets &&
            Time.realtimeSinceStartup - lastReceivedRealtime > Mathf.Max(0.1f, signalTimeout))
        {
            IsConnected = false;
            isReceivingPackets = false;
            currentStatus = "신호 끊김";
            Debug.LogWarning($"[UDP] {signalTimeout:0.##}초 동안 패킷이 없어 신호가 끊겼습니다.");
        }
    }

    // 앱 종료 시 연결 종료
    private void OnApplicationQuit() 
    {
        StopConnection();
    }

    private void OnDestroy()
    {
        StopConnection();
        if (Instance == this) Instance = null;
    }

    #region 연결 제어 기능
    // 연결 시작 메서드
    public void StartConnection()
    {
        if (isRunning) return;

        try
        {
            client = new UdpClient(port);
            isRunning = true;
            
            // 스레드 설정 및 시작
            receiveThread = new Thread(new ThreadStart(ReceiveData));
            receiveThread.IsBackground = true;
            receiveThread.Start();

            IsConnected = false;
            isReceivingPackets = false;
            currentStatus = "패킷 대기 중";
            receivedLog = "연결 성공! 데이터 수신 대기중...";
            
            Debug.Log($"[UDP] {port}번 포트에서 수신 대기를 시작합니다.");
        }
        catch (Exception e)
        {
            isRunning = false;
            client?.Close();
            client = null;
            Debug.LogError($"[UDP 연결 실패] {port}번 포트를 열 수 없습니다: {e.Message}");
            currentStatus = "연결 실패";
            receivedLog = $"연결 실패: {e.Message}";
        }
    }

    // 연결 종료 메서드
    public void StopConnection()
    {
        if (!isRunning && client == null && receiveThread == null) return;

        isRunning = false;
        client?.Close();

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(500);
        }

        receiveThread = null;
        client = null;
        IsConnected = false;
        isReceivingPackets = false;
        currentStatus = "연결 종료";
        Debug.Log("[UDP] 연결을 종료합니다.");
    }
    #endregion

    #region 데이터 수신 로직 (Thread)
    private void ReceiveData()
    {
        while (isRunning)
        {
            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = client.Receive(ref remoteEndPoint);
                string senderIP = remoteEndPoint.Address.ToString();

                // IP 주소 필터링 체크
                if (!string.IsNullOrEmpty(targetIPAddress) && !senderIP.Equals(targetIPAddress))
                {
                    Debug.LogWarning($"[UDP] 허용되지 않은 IP({senderIP})로부터 데이터를 수신하여 무시합니다.");
                    continue;
                }

                string text = Encoding.UTF8.GetString(data);

                // 메인 스레드로 작업 이관
                mainThreadDispatcher.Enqueue(() =>
                {
                    if (isRunning) HandleReceivedData(text, senderIP);
                });
            }
            catch (ThreadAbortException)
            {
                // Unity 도메인 리로드가 백그라운드 스레드를 종료하는 경우
            }
            catch (SocketException e)
            {
                // 소켓 종료 시 발생하는 예외는 로그만 남김
                if (isRunning) Debug.LogWarning($"[UDP] 소켓 오류: {e.Message}");
            }
            catch (Exception e)
            {
                if (isRunning) Debug.LogError($"[UDP] 수신 오류: {e.Message}");
            }
        }
    }

    // 메인 스레드에서 실행되는 데이터 처리
    private void HandleReceivedData(string text, string senderIP)
    {
        LatestData = text;
        receivedLog = text.Replace("|", "\n"); // 인스펙터 모니터링용
        lastReceivedRealtime = Time.realtimeSinceStartup;
        lastReceivedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        currentStatus = "수신 중";
        isReceivingPackets = true;

        // 첫 연결 시 로그 출력
        if (!IsConnected)
        {
            IsConnected = true;
            Debug.Log($"[UDP] {senderIP}로부터 데이터 수신 시작!");
        }

        // 이벤트 발송
        OnDataReceived?.Invoke(text);
    }
    #endregion
}

#region Helper Classes
// 메인 스레드 디스패처
public class UnityMainThreadDispatcher : MonoBehaviour
{
    
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher _instance = null;

    // 싱글톤 인스턴스
    public static UnityMainThreadDispatcher Instance()
    {
        if (!_instance)
        {
            GameObject obj = new GameObject("UnityMainThreadDispatcher");
            _instance = obj.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(obj);
        }
        return _instance;
    }

    private void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    // 작업 큐에 액션 추가
    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}
#endregion
