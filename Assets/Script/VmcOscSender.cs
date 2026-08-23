using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

[DefaultExecutionOrder(600)]
[RequireComponent(typeof(iFacialMocapAnimator))]
public sealed class VmcOscSender : MonoBehaviour
{
    private const float SendInterval = 1f / 30f;
    private const float RetryInterval = 1f;

    [Header("VMC OSC 송신 설정")]
    [Tooltip("활성화하면 현재 캐릭터의 최종 표정과 머리·눈 회전을 30 FPS로 송신합니다.")]
    public bool sendEnabled;

    [Tooltip("VMC 수신 프로그램이 실행 중인 PC의 IP")]
    public string targetIPAddress = "127.0.0.1";

    [Tooltip("VMC 수신 포트. 일반적인 Performer 수신 포트는 39540입니다.")]
    [Range(1, 65535)]
    public int targetPort = 39540;

    private readonly VmcOscPacketWriter packetWriter = new VmcOscPacketWriter();
    private readonly List<KeyValuePair<string, float>> appliedBlendShapes =
        new List<KeyValuePair<string, float>>(16);

    private iFacialMocapAnimator mocapAnimator;
    private UdpClient udpClient;
    private float nextSendAt;
    private float nextRetryAt;
    private string senderStatus = "중지됨";
    private DateTime? lastSentUtc;
    private int sentFrameCount;
    private int sendFailureCount;

    public string SenderStatus => senderStatus;
    public string LastSentAt => lastSentUtc.HasValue
        ? lastSentUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff")
        : "송신 기록 없음";
    public int SentFrameCount => sentFrameCount;
    public int SendFailureCount => sendFailureCount;
    public bool IsSending => sendEnabled && udpClient != null;

    private void Start()
    {
        mocapAnimator = GetComponent<iFacialMocapAnimator>();
        if (sendEnabled)
            StartSending();
    }

    private void LateUpdate()
    {
        if (!sendEnabled) return;

        if (udpClient == null)
        {
            if (Time.unscaledTime >= nextRetryAt)
                StartSending();
            return;
        }

        if (Time.unscaledTime < nextSendAt) return;

        nextSendAt = Time.unscaledTime + SendInterval;
        SendCurrentFrame();
    }

    private void OnDisable()
    {
        CloseSocket("중지됨");
    }

    private void OnDestroy()
    {
        CloseSocket("중지됨");
    }

    public void StartSending()
    {
        sendEnabled = true;
        CloseSocket(null);

        if (!IPAddress.TryParse(targetIPAddress, out IPAddress targetAddress))
        {
            senderStatus = "설정 오류: 대상 IP 확인 필요";
            nextRetryAt = Time.unscaledTime + RetryInterval;
            return;
        }

        if (targetPort < 1 || targetPort > 65535)
        {
            senderStatus = "설정 오류: 대상 포트 확인 필요";
            nextRetryAt = Time.unscaledTime + RetryInterval;
            return;
        }

        try
        {
            udpClient = new UdpClient();
            udpClient.Connect(targetAddress, targetPort);
            nextSendAt = Time.unscaledTime;
            senderStatus = "송신 중";
        }
        catch (Exception exception)
        {
            sendFailureCount++;
            senderStatus = $"송신 오류: {exception.Message}";
            nextRetryAt = Time.unscaledTime + RetryInterval;
            CloseSocket(senderStatus);
        }
    }

    public void StopSending()
    {
        sendEnabled = false;
        CloseSocket("중지됨");
    }

    public void RestartSending()
    {
        StopSending();
        StartSending();
    }

    public void ResetDiagnostics()
    {
        sentFrameCount = 0;
        sendFailureCount = 0;
        lastSentUtc = null;
    }

    private void SendCurrentFrame()
    {
        try
        {
            packetWriter.Reset();
            AddTrackedBone("Head", mocapAnimator.headBone);
            AddTrackedBone("LeftEye", mocapAnimator.leftEyeBone);
            AddTrackedBone("RightEye", mocapAnimator.rightEyeBone);

            mocapAnimator.CopyAppliedBlendShapes(appliedBlendShapes);
            foreach (KeyValuePair<string, float> blendShape in appliedBlendShapes)
            {
                if (VmcExpressionNameMapper.TryMap(blendShape.Key, out string expressionName))
                    packetWriter.AddBlend(expressionName, blendShape.Value);
            }

            packetWriter.AddBlendApply();
            ArraySegment<byte> packet = packetWriter.GetPacket();
            udpClient.Send(packet.Array, packet.Count);

            sentFrameCount++;
            lastSentUtc = DateTime.UtcNow;
            senderStatus = "송신 중";
        }
        catch (Exception exception)
        {
            sendFailureCount++;
            senderStatus = $"송신 오류: {exception.Message}";
            nextRetryAt = Time.unscaledTime + RetryInterval;
            CloseSocket(senderStatus);
        }
    }

    private void AddTrackedBone(string boneName, Transform bone)
    {
        if (bone != null)
            packetWriter.AddBone(boneName, bone.localPosition, bone.localRotation);
    }

    private void CloseSocket(string status)
    {
        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }

        if (!string.IsNullOrEmpty(status))
            senderStatus = status;
    }
}
