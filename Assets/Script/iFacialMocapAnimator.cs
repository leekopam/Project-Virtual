using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[DefaultExecutionOrder(500)]
public class iFacialMocapAnimator : MonoBehaviour
{
    private const float NeutralReturnSpeed = 5f;

    #region 변수 선언
    [Header("설정 조정")]
    [Tooltip("체크하면 내 왼쪽 눈 감을 때 모델도 왼쪽 눈을 감습니다. (체크 해제 시 거울 모드)")]
    public bool swapLeftRightEye = true;

    [Header("회전 데이터 처리 설정")]
    [Tooltip("수신된 데이터가 라디안 단위라면 체크 (기본 체크)")]
    public bool useRadians = true;

    [Header("데이터 소스 매핑")]
    [Tooltip("고개 끄덕임(Pitch)을 담당하는 데이터 번호 (기본 5)")]
    public int pitchSourceIndex = 5;
    [Tooltip("고개 돌리기(Yaw)를 담당하는 데이터 번호 (기본 3)")]
    public int yawSourceIndex = 3;
    [Tooltip("고개 갸웃(Roll)을 담당하는 데이터 번호 (기본 4)")]
    public int rollSourceIndex = 4;

    [Header("캘리브레이션 (보정)")]
    [Tooltip("이 버튼을 체크하면 현재 머리 각도를 0점으로 설정합니다. (기울어짐 해결)")]
    public bool calibrateNow = false;

    [Tooltip("현재 적용된 보정값 (자동 설정됨)")]
    public Vector3 calibrationOffsetEuler;

    [Tooltip("저장할 캐릭터 보정 프로필 이름 (비어있으면 GameObject 이름 사용)")]
    public string calibrationProfileName = "";

    [Header("회전 보정 (최종 감도 및 오프셋)")]
    [Tooltip("각 축별 회전 방향 및 세기 (X: Pitch, Y: Yaw, Z: Roll). -1은 반전.")]
    public Vector3 rotationMultiplier = new Vector3(1f, -1f, 1f);

    [Tooltip("전체 감도")]
    public float headSensitivity = 5.0f;

    [Tooltip("추가 수동 오프셋 (필요시 사용)")]
    public Vector3 additionalOffset = Vector3.zero;

    [Tooltip("패킷 재수신과 일반 추적 시 적용할 보간 속도")]
    [Min(0.1f)]
    public float trackingSmoothingSpeed = 12f;

    [Header("연결 대상")]
    public SkinnedMeshRenderer faceMeshRenderer;
    public Transform headBone;
    public Transform leftEyeBone;
    public Transform rightEyeBone;

    // 내부 변수
    private readonly Dictionary<string, float> currentBlendShapes = new Dictionary<string, float>();
    private readonly Dictionary<string, int> blendShapeIndices = new Dictionary<string, int>();
    private Vector3 currentHeadEuler = Vector3.zero;

    private Quaternion initialHeadRotation;
    private bool isConnected = false;
    private string calibrationStatus = "미보정";
    private string lastCalibrationAt = "보정 기록 없음";
    private string calibrationSaveStatus = "저장 기록 없음";
    private string lastCalibrationSavedAt = "저장 기록 없음";
    private string calibrationLoadStatus = "불러오기 기록 없음";
    private string lastCalibrationLoadedAt = "불러오기 기록 없음";

    public string CalibrationStatus => calibrationStatus;
    public string LastCalibrationAt => lastCalibrationAt;
    public string CalibrationSaveStatus => calibrationSaveStatus;
    public string LastCalibrationSavedAt => lastCalibrationSavedAt;
    public string CalibrationLoadStatus => calibrationLoadStatus;
    public string LastCalibrationLoadedAt => lastCalibrationLoadedAt;
    #endregion

    #region 유니티 라이프사이클
    void Start()
    {
        // 연결 대상이 비어있으면 자동으로 찾기 수행
        if (faceMeshRenderer == null) AutoFindFaceMesh();
        if (headBone == null) AutoFindHeadBone();
        if (leftEyeBone == null || rightEyeBone == null) AutoFindEyeBones();
        CacheBlendShapeIndices();

        // 초기 회전값 저장 (보정 기준)
        if (headBone) initialHeadRotation = headBone.localRotation;

        LoadCalibration();

        // UDP 수신 연결 시작
        ConnectToUDP();
    }

    void OnDestroy()
    {
        // 객체 파괴 시 이벤트 연결 해제
        if (UDPReceiver.Instance != null)
            UDPReceiver.Instance.OnDataReceived -= ProcessReceivedData;
    }

    void LateUpdate()
    {
        // 캘리브레이션 트리거 체크
        if (calibrateNow)
        {
            calibrateNow = false;
            PerformCalibration();
        }

        if (!isConnected) return;

        if (UDPReceiver.Instance != null && UDPReceiver.Instance.IsReceivingPackets)
        {
            ApplyBlendShapes();
            ApplyHeadRotation();
        }
        else
        {
            ReturnToNeutral();
        }
    }
    #endregion

    #region 초기화 및 연결 기능
    // 얼굴 메쉬를 자동으로 찾아주는 메서드
    void AutoFindFaceMesh()
    {
        var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0) return;

        // 쉐이프키가 가장 많은 렌더러를 얼굴로 간주
        var best = renderers.OrderByDescending(r => r.sharedMesh.blendShapeCount).First();
        faceMeshRenderer = best;
        Debug.Log($"✅ [자동 연결] 얼굴 메쉬: {best.name}");
    }

    // 머리 뼈를 자동으로 찾아주는 메서드
    void AutoFindHeadBone()
    {
        Animator anim = GetComponent<Animator>();
        if (anim != null && anim.isHuman)
        {
            headBone = anim.GetBoneTransform(HumanBodyBones.Head);
        }

        // Animator가 없으면 이름으로 검색
        if (headBone == null)
        {
            Transform[] allChildren = GetComponentsInChildren<Transform>(true);
            foreach (Transform t in allChildren)
            {
                if (t.name.ToLower().Contains("head") && !t.name.ToLower().Contains("mesh"))
                {
                    headBone = t;
                    break;
                }
            }
        }
        if (headBone != null) Debug.Log($"✅ [자동 연결] 머리 뼈: {headBone.name}");
    }

    // 좌우 눈 뼈를 자동으로 찾아주는 메서드
    void AutoFindEyeBones()
    {
        Animator anim = GetComponent<Animator>();
        if (leftEyeBone == null)
        {
            if (anim != null && anim.isHuman)
                leftEyeBone = anim.GetBoneTransform(HumanBodyBones.LeftEye);
            if (leftEyeBone == null)
                leftEyeBone = FindEyeBoneByName(true);
            if (leftEyeBone != null)
                Debug.Log($"✅ [자동 연결] 왼쪽 눈 뼈: {leftEyeBone.name}");
        }

        if (rightEyeBone == null)
        {
            if (anim != null && anim.isHuman)
                rightEyeBone = anim.GetBoneTransform(HumanBodyBones.RightEye);
            if (rightEyeBone == null)
                rightEyeBone = FindEyeBoneByName(false);
            if (rightEyeBone != null)
                Debug.Log($"✅ [자동 연결] 오른쪽 눈 뼈: {rightEyeBone.name}");
        }
    }

    private Transform FindEyeBoneByName(bool isLeft)
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        return allChildren.FirstOrDefault(t => IsEyeBoneName(t.name, isLeft));
    }

    private static bool IsEyeBoneName(string boneName, bool isLeft)
    {
        string normalizedName = new string(boneName.Where(char.IsLetter).ToArray()).ToLowerInvariant();

        if (isLeft)
        {
            return normalizedName.Contains("lefteye") ||
                   normalizedName.Contains("eyeleft") ||
                   normalizedName.EndsWith("leye") ||
                   normalizedName.EndsWith("eyel") ||
                   normalizedName.Contains("왼쪽눈") ||
                   normalizedName.Contains("左目");
        }

        return normalizedName.Contains("righteye") ||
               normalizedName.Contains("eyeright") ||
               normalizedName.EndsWith("reye") ||
               normalizedName.EndsWith("eyer") ||
               normalizedName.Contains("오른쪽눈") ||
               normalizedName.Contains("右目");
    }

    // UDP 리시버에 연결하는 기능
    void ConnectToUDP()
    {
        if (UDPReceiver.Instance != null)
        {
            UDPReceiver.Instance.OnDataReceived -= ProcessReceivedData;
            UDPReceiver.Instance.OnDataReceived += ProcessReceivedData;
            isConnected = true;
        }
        else
        {
            // 아직 리시버가 준비되지 않았다면 1초 뒤 재시도
            Invoke("ConnectToUDP", 1f);
        }
    }
    #endregion

    #region 데이터 처리 및 적용
    // UDP로부터 수신된 데이터를 처리하는 메서드
    private void ProcessReceivedData(string data)
    {
        var allValues = data.Split('|');

        foreach (var valuePair in allValues)
        {
            if (string.IsNullOrEmpty(valuePair)) continue;

            if (valuePair.Contains("head#"))
            {
                int index = valuePair.IndexOf("head#");
                if (index + 5 < valuePair.Length)
                {
                    string rawData = valuePair.Substring(index + 5);
                    currentHeadEuler = ParseHeadRotationEuler(rawData);
                }
            }
            else if (valuePair.Contains("-"))
            {
                var pair = valuePair.Split('-');
                if (pair.Length == 2)
                {
                    string key = RemapBlendshapeKey(pair[0], swapLeftRightEye);
                    if (float.TryParse(pair[1], out float value))
                    {
                        if (value <= 1.0f) value *= 100f;
                        currentBlendShapes[key] = Mathf.Clamp(value, 0f, 100f);
                    }
                }
            }
        }
    }

    // BlendShape 적용 메서드
    private void ApplyBlendShapes()
    {
        if (faceMeshRenderer != null)
        {
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, trackingSmoothingSpeed) * Time.deltaTime);
            foreach (var kvp in currentBlendShapes)
            {
                if (blendShapeIndices.TryGetValue(kvp.Key, out int index))
                {
                    float currentWeight = faceMeshRenderer.GetBlendShapeWeight(index);
                    faceMeshRenderer.SetBlendShapeWeight(index, Mathf.Lerp(currentWeight, kvp.Value, blend));
                }
            }
        }
    }

    // 머리 회전 적용 메서드
    private void ApplyHeadRotation()
    {
        if (headBone != null)
        {
            // 캘리브레이션 적용 (현재 값 - 기준 값)
            Vector3 finalEuler = currentHeadEuler - calibrationOffsetEuler;

            // 각도 정규화 (-180 ~ 180)
            finalEuler.x = NormalizeAngle(finalEuler.x);
            finalEuler.y = NormalizeAngle(finalEuler.y);
            finalEuler.z = NormalizeAngle(finalEuler.z);

            // 각 축별 Multiplier 적용 (방향 반전 등)
            finalEuler.x *= rotationMultiplier.x;
            finalEuler.y *= rotationMultiplier.y;
            finalEuler.z *= rotationMultiplier.z;

            // 전체 감도 적용
            finalEuler *= headSensitivity;

            // 추가 수동 오프셋
            finalEuler += additionalOffset;

            // 최종 회전 적용
            Quaternion targetRotation = checkInitialRotation() * Quaternion.Euler(finalEuler);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, trackingSmoothingSpeed) * Time.deltaTime);
            headBone.localRotation = Quaternion.Slerp(headBone.localRotation, targetRotation, blend);
        }
    }

    private void ReturnToNeutral()
    {
        float blend = 1f - Mathf.Exp(-NeutralReturnSpeed * Time.deltaTime);

        if (faceMeshRenderer != null)
        {
            foreach (var kvp in currentBlendShapes)
            {
                if (blendShapeIndices.TryGetValue(kvp.Key, out int index))
                {
                    float currentWeight = faceMeshRenderer.GetBlendShapeWeight(index);
                    faceMeshRenderer.SetBlendShapeWeight(index, Mathf.Lerp(currentWeight, 0f, blend));
                }
            }
        }

        if (headBone != null)
        {
            Quaternion neutralRotation = checkInitialRotation() * Quaternion.Euler(additionalOffset);
            headBone.localRotation = Quaternion.Slerp(headBone.localRotation, neutralRotation, blend);
        }
    }

    public void RequestCalibration()
    {
        calibrateNow = true;
        calibrationStatus = "보정 요청됨";
    }

    public void ResetCalibration()
    {
        calibrationOffsetEuler = Vector3.zero;
        rotationMultiplier = new Vector3(1f, -1f, 1f);
        headSensitivity = 5f;
        additionalOffset = Vector3.zero;
        calibrationStatus = "기본값 복원 완료";
        Debug.Log("[Calibration] 보정값을 기본값으로 복원했습니다.");
    }

    public void SaveCalibration()
    {
        string profileName = GetCalibrationProfileName();

        try
        {
            var settings = new FacialMocapCalibrationSettings(
                calibrationOffsetEuler,
                rotationMultiplier,
                headSensitivity,
                additionalOffset);
            FacialMocapCalibrationStore.Save(profileName, settings);

            calibrationSaveStatus = $"저장 완료: {profileName}";
            lastCalibrationSavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Debug.Log($"[Calibration] '{profileName}' 보정값을 저장했습니다.");
        }
        catch (Exception exception)
        {
            calibrationSaveStatus = $"저장 실패: {exception.Message}";
            Debug.LogError($"[Calibration] 보정값 저장 실패: {exception.Message}");
        }
    }

    public bool LoadCalibration()
    {
        string profileName = GetCalibrationProfileName();

        try
        {
            if (!FacialMocapCalibrationStore.TryLoad(profileName, out FacialMocapCalibrationSettings settings))
            {
                calibrationLoadStatus = $"저장값 없음: {profileName}";
                return false;
            }

            calibrationOffsetEuler = settings.CalibrationOffsetEuler;
            rotationMultiplier = settings.RotationMultiplier;
            headSensitivity = settings.HeadSensitivity;
            additionalOffset = settings.AdditionalOffset;

            calibrationStatus = "저장값 적용 완료";
            calibrationLoadStatus = $"불러오기 완료: {profileName}";
            lastCalibrationLoadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            Debug.Log($"[Calibration] '{profileName}' 보정값을 불러왔습니다.");
            return true;
        }
        catch (Exception exception)
        {
            calibrationLoadStatus = $"불러오기 실패: {exception.Message}";
            Debug.LogError($"[Calibration] 보정값 불러오기 실패: {exception.Message}");
            return false;
        }
    }
    #endregion

    #region 헬퍼 메서드
    private void PerformCalibration()
    {
        if (headBone == null)
        {
            calibrationStatus = "보정 실패: 머리 뼈 없음";
            Debug.LogWarning("[Calibration] 머리 뼈가 연결되지 않아 보정할 수 없습니다.");
            return;
        }

        if (UDPReceiver.Instance == null || !UDPReceiver.Instance.IsReceivingPackets)
        {
            calibrationStatus = "보정 실패: 정상 패킷 없음";
            Debug.LogWarning("[Calibration] 정상 패킷을 수신 중일 때만 보정할 수 있습니다.");
            return;
        }

        calibrationOffsetEuler = currentHeadEuler;
        calibrationStatus = "보정 완료";
        lastCalibrationAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Debug.Log($"[Calibration] 보정 완료. Offset: {calibrationOffsetEuler}");
    }

    private string GetCalibrationProfileName()
    {
        return string.IsNullOrWhiteSpace(calibrationProfileName)
            ? gameObject.name
            : calibrationProfileName.Trim();
    }

    private void CacheBlendShapeIndices()
    {
        blendShapeIndices.Clear();
        if (faceMeshRenderer == null || faceMeshRenderer.sharedMesh == null) return;

        Mesh mesh = faceMeshRenderer.sharedMesh;
        for (int index = 0; index < mesh.blendShapeCount; index++)
        {
            AddBlendShapeIndex(mesh.GetBlendShapeName(index), index);
        }

        for (int index = 0; index < mesh.blendShapeCount; index++)
        {
            string fullName = mesh.GetBlendShapeName(index);
            int separatorIndex = fullName.IndexOf('.');
            if (separatorIndex <= 0 ||
                !int.TryParse(fullName.Substring(0, separatorIndex), out _)) continue;

            string normalizedName = fullName.Substring(separatorIndex + 1);
            AddBlendShapeIndex(normalizedName, index);

            if (normalizedName.Length > 1 && normalizedName[0] == 'x')
            {
                AddBlendShapeIndex(normalizedName.Substring(1), index);
            }
        }
    }

    private void AddBlendShapeIndex(string name, int index)
    {
        if (!blendShapeIndices.ContainsKey(name))
        {
            blendShapeIndices.Add(name, index);
        }
    }

    // 초기 회전값 유효성 체크
    private Quaternion checkInitialRotation()
    {
        if (initialHeadRotation.x == 0 && initialHeadRotation.y == 0 && initialHeadRotation.z == 0 && initialHeadRotation.w == 0)
            return Quaternion.identity;
        return initialHeadRotation;
    }

    // 문자열 데이터를 파싱하여 오일러 각도로 변환
    private Vector3 ParseHeadRotationEuler(string rotationData)
    {
        var axis = rotationData.Split(',');
        float x = 0, y = 0, z = 0;
        var style = System.Globalization.NumberStyles.Any;
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        // 설정된 인덱스에서 데이터 추출
        int p = pitchSourceIndex;
        int yw = yawSourceIndex;
        int r = rollSourceIndex;

        // 배열 범위 체크 (안전장치)
        if (p >= 0 && p < axis.Length) float.TryParse(axis[p], style, culture, out x);
        if (yw >= 0 && yw < axis.Length) float.TryParse(axis[yw], style, culture, out y);
        if (r >= 0 && r < axis.Length) float.TryParse(axis[r], style, culture, out z);

        // 라디안 변환
        if (useRadians)
        {
            x *= Mathf.Rad2Deg;
            y *= Mathf.Rad2Deg;
            z *= Mathf.Rad2Deg;
        }

        // 1차 정규화
        x = NormalizeAngle(x);
        y = NormalizeAngle(y);
        z = NormalizeAngle(z);

        return new Vector3(x, y, z);
    }

    // 각도를 -180 ~ 180 사이로 정규화
    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    // 블렌드쉐이프 키 리매핑
    private string RemapBlendshapeKey(string key, bool swapEyes)
    {
        if (swapEyes)
        {
            if (key == "eyeBlink_L") key = "eyeBlink_R";
            else if (key == "eyeBlink_R") key = "eyeBlink_L";
            if (key == "eyeSquint_L") key = "eyeSquint_R";
            else if (key == "eyeSquint_R") key = "eyeSquint_L";
            if (key == "browDown_L") key = "browDown_R";
            else if (key == "browDown_R") key = "browDown_L";
        }
        switch (key)
        {
            case "jawOpen": return "あ";
            case "mouthLowerDown_L": return "い";
            case "mouthLowerDown_R": return "い";
            case "mouthFunnel": return "う";
            case "mouthPucker": return "お";
            case "mouthUpperUp_L": return "え";
            case "mouthUpperUp_R": return "え";
            case "eyeBlink_L": return "ウィンク";
            case "eyeBlink_R": return "ウィンク右";
            case "mouthSmile_L": return "笑い";
            case "mouthSmile_R": return "笑い";
            case "browDown_L": return "怒り";
            case "browDown_R": return "怒り";
            case "browInnerUp": return "困る";
            default: return key;
        }
    }
    #endregion
}
