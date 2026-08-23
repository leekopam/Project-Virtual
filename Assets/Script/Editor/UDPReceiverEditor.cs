using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UDPReceiver))]
public class UDPReceiverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        UDPReceiver receiver = (UDPReceiver)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("수신 제어", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "수신 시작·중지·재시작은 Play Mode에서 사용할 수 있습니다.",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(receiver.IsRunning))
            {
                if (GUILayout.Button("수신 시작"))
                    receiver.StartConnection();
            }

            using (new EditorGUI.DisabledScope(!receiver.IsRunning))
            {
                if (GUILayout.Button("수신 중지"))
                    receiver.StopConnection();
            }

            if (GUILayout.Button("수신 재시작"))
                receiver.RestartConnection();

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("실시간 진단", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"{receiver.CurrentStatus}\n{receiver.StatusDetail}",
            GetStatusMessageType(receiver.ReceiveState));

        DrawValue("최근 패킷 상태", receiver.IsReceivingPackets ? "정상" : "없음");
        DrawValue("송신 IP", receiver.SenderIPAddress);
        DrawValue("잠긴 송신 IP", receiver.LockedSenderIPAddress);
        DrawValue("최근 수신 시각", receiver.LastReceivedAt);
        DrawValue(
            "마지막 패킷 이후",
            float.IsPositiveInfinity(receiver.SecondsSinceLastPacket)
                ? "수신 기록 없음"
                : $"{receiver.SecondsSinceLastPacket:0.000}초");
        DrawValue("초당 패킷 수", $"{receiver.PacketsPerSecond:0.0} PPS");
        DrawValue("누적 수신", receiver.TotalReceivedPackets.ToString());
        DrawValue("누적 적용", receiver.TotalAppliedPackets.ToString());
        DrawValue("누적 무시", receiver.TotalIgnoredPackets.ToString());
        DrawValue("잘못된 패킷", receiver.TotalInvalidPackets.ToString());
        DrawValue("최신 프레임 대체", receiver.TotalSupersededPackets.ToString());
        if (receiver.SenderWarning != "없음")
            EditorGUILayout.HelpBox(receiver.SenderWarning, MessageType.Warning);

        if (Application.isPlaying)
            Repaint();
    }

    private static void DrawValue(string label, string value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);
        EditorGUILayout.SelectableLabel(value, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndHorizontal();
    }

    private static MessageType GetStatusMessageType(UdpReceiveState state)
    {
        switch (state)
        {
            case UdpReceiveState.Receiving:
                return MessageType.Info;
            case UdpReceiveState.SignalLost:
                return MessageType.Warning;
            case UdpReceiveState.PortError:
                return MessageType.Error;
            default:
                return MessageType.None;
        }
    }
}
