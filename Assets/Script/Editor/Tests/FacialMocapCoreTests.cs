using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;

public class FacialMocapCoreTests
{
    private const string ProfileName = "CodexFacialMocapCoreTests";
    private const string StoragePrefix = "ProjectVirtual.iFacialMocap.Calibration." + ProfileName + ".";

    private static readonly string[] CalibrationKeys =
    {
        "offset.x", "offset.y", "offset.z",
        "multiplier.x", "multiplier.y", "multiplier.z",
        "headSensitivity",
        "additionalOffset.x", "additionalOffset.y", "additionalOffset.z"
    };

    [SetUp]
    public void SetUp()
    {
        DeleteTestProfile();
    }

    [TearDown]
    public void TearDown()
    {
        DeleteTestProfile();
    }

    [Test]
    public void PacketValidator_AcceptsMocapAndRejectsGarbage()
    {
        Assert.That(FacialMocapPacketValidator.IsValid("jawOpen-0.8|"), Is.True);
        Assert.That(FacialMocapPacketValidator.IsValid("head#0,0,0,0.1,0.2,0.3|"), Is.True);
        Assert.That(FacialMocapPacketValidator.IsValid("not-a-mocap-packet|"), Is.False);
        Assert.That(FacialMocapPacketValidator.IsValid("   "), Is.False);
    }

    [Test]
    public void LatestPacketBuffer_KeepsOnlyNewestFrame()
    {
        var buffer = new LatestMocapPacketBuffer();
        var first = new UdpMocapPacketFrame("jawOpen-0.1|", "127.0.0.1", "127.0.0.1", DateTime.UtcNow);
        var latest = new UdpMocapPacketFrame("jawOpen-0.9|", "127.0.0.1", "127.0.0.1", DateTime.UtcNow);

        Assert.That(buffer.Store(first), Is.False);
        Assert.That(buffer.Store(latest), Is.True);
        Assert.That(buffer.TryTake(out UdpMocapPacketFrame taken), Is.True);
        Assert.That(taken.Data, Is.EqualTo(latest.Data));
        Assert.That(buffer.TryTake(out _), Is.False);
    }

    [Test]
    public void RotationParser_UsesInvariantCultureAndRejectsMalformedValues()
    {
        Assert.That(FacialMocapRotationParser.TryParseEuler("6.0,-1.5,0.25", out Vector3 rotation), Is.True);
        Assert.That(rotation, Is.EqualTo(new Vector3(6f, -1.5f, 0.25f)));
        Assert.That(FacialMocapRotationParser.TryParseEuler("6,broken,0.25", out _), Is.False);
    }

    [Test]
    public void StreamingRequest_SendsOfficialCommandToRequestedEndpoint()
    {
        using (var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        using (var sender = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            listener.Client.ReceiveTimeout = 1000;
            int port = ((IPEndPoint)listener.Client.LocalEndPoint).Port;
            int senderPort = ((IPEndPoint)sender.Client.LocalEndPoint).Port;

            FacialMocapStreamingRequest.Send(sender, IPAddress.Loopback, port);

            var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
            byte[] payload = listener.Receive(ref remoteEndPoint);
            Assert.That(
                Encoding.UTF8.GetString(payload),
                Is.EqualTo(FacialMocapStreamingRequest.Message));
            Assert.That(remoteEndPoint.Port, Is.EqualTo(senderPort));
        }
    }

    [TestCase("あ", "A")]
    [TestCase("い", "I")]
    [TestCase("う", "U")]
    [TestCase("え", "E")]
    [TestCase("お", "O")]
    [TestCase("ウィンク", "Blink_L")]
    [TestCase("ウィンク右", "Blink_R")]
    [TestCase("笑い", "Joy")]
    [TestCase("怒り", "Angry")]
    [TestCase("困る", "Sorrow")]
    public void ExpressionMapper_MapsSupportedNames(string source, string expected)
    {
        Assert.That(VmcExpressionNameMapper.TryMap(source, out string actual), Is.True);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void ExpressionMapper_RejectsUnsupportedNames()
    {
        Assert.That(VmcExpressionNameMapper.TryMap("未対応", out _), Is.False);
    }

    [Test]
    public void CalibrationStore_RoundTripsAllValues()
    {
        var expected = new FacialMocapCalibrationSettings(
            new Vector3(1.25f, -2.5f, 3.75f),
            new Vector3(-1f, 0.75f, 1.5f),
            6.5f,
            new Vector3(4f, 5f, 6f));

        FacialMocapCalibrationStore.Save(ProfileName, expected);

        Assert.That(FacialMocapCalibrationStore.TryLoad(ProfileName, out FacialMocapCalibrationSettings actual), Is.True);
        Assert.That(actual.CalibrationOffsetEuler, Is.EqualTo(expected.CalibrationOffsetEuler));
        Assert.That(actual.RotationMultiplier, Is.EqualTo(expected.RotationMultiplier));
        Assert.That(actual.HeadSensitivity, Is.EqualTo(expected.HeadSensitivity));
        Assert.That(actual.AdditionalOffset, Is.EqualTo(expected.AdditionalOffset));
    }

    [Test]
    public void CalibrationStore_RejectsPartialProfile()
    {
        var settings = new FacialMocapCalibrationSettings(
            Vector3.one,
            Vector3.one,
            5f,
            Vector3.zero);
        FacialMocapCalibrationStore.Save(ProfileName, settings);
        PlayerPrefs.DeleteKey(StoragePrefix + "additionalOffset.z");

        Assert.That(FacialMocapCalibrationStore.TryLoad(ProfileName, out _), Is.False);
    }

    private static void DeleteTestProfile()
    {
        foreach (string key in CalibrationKeys)
            PlayerPrefs.DeleteKey(StoragePrefix + key);

        PlayerPrefs.Save();
    }
}
