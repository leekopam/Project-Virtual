using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(iFacialMocapAnimator))]
public class iFacialMocapAnimatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        iFacialMocapAnimator animator = (iFacialMocapAnimator)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("캘리브레이션 상태", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("현재 상태", animator.CalibrationStatus);
        EditorGUILayout.LabelField("최근 보정 시각", animator.LastCalibrationAt);
        EditorGUILayout.LabelField("저장 상태", animator.CalibrationSaveStatus);
        EditorGUILayout.LabelField("최근 저장 시각", animator.LastCalibrationSavedAt);
        EditorGUILayout.LabelField("불러오기 상태", animator.CalibrationLoadStatus);
        EditorGUILayout.LabelField("최근 불러오기 시각", animator.LastCalibrationLoadedAt);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "실시간 보정은 정상 패킷을 수신하는 Play Mode에서 실행할 수 있습니다.",
                MessageType.Info);
        }

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("현재 자세 보정"))
                animator.RequestCalibration();
        }

        if (GUILayout.Button("기본값 복원"))
        {
            Undo.RecordObject(animator, "캘리브레이션 기본값 복원");
            animator.ResetCalibration();
            EditorUtility.SetDirty(animator);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("캐릭터별 보정값 저장"))
            animator.SaveCalibration();

        if (GUILayout.Button("저장값 불러오기"))
        {
            Undo.RecordObject(animator, "캘리브레이션 저장값 불러오기");
            if (animator.LoadCalibration())
                EditorUtility.SetDirty(animator);
        }
        EditorGUILayout.EndHorizontal();

        if (Application.isPlaying)
            Repaint();
    }
}
