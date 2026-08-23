using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(VmcOscSender))]
public sealed class VmcOscSenderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VmcOscSender sender = (VmcOscSender)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("실시간 송신 진단", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("현재 상태", sender.SenderStatus);
        EditorGUILayout.LabelField("송신 프레임", sender.SentFrameCount.ToString("N0"));
        EditorGUILayout.LabelField("송신 실패", sender.SendFailureCount.ToString("N0"));
        EditorGUILayout.LabelField("최근 송신 시각", sender.LastSentAt);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "송신 제어와 실시간 진단은 Play Mode에서 사용할 수 있습니다.",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("송신 시작")) sender.StartSending();
            if (GUILayout.Button("송신 중지")) sender.StopSending();
            if (GUILayout.Button("재시작")) sender.RestartSending();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("진단값 초기화")) sender.ResetDiagnostics();
        }

        if (Application.isPlaying)
            Repaint();
    }
}
