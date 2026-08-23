using System;

public readonly struct UdpMocapPacketFrame
{
    public UdpMocapPacketFrame(
        string data,
        string senderIPAddress,
        string lockedSenderIPAddress,
        DateTime receivedUtc)
    {
        Data = data;
        SenderIPAddress = senderIPAddress;
        LockedSenderIPAddress = lockedSenderIPAddress;
        ReceivedUtc = receivedUtc;
    }

    public string Data { get; }
    public string SenderIPAddress { get; }
    public string LockedSenderIPAddress { get; }
    public DateTime ReceivedUtc { get; }
}

public sealed class LatestMocapPacketBuffer
{
    private readonly object syncRoot = new object();
    private UdpMocapPacketFrame latestFrame;
    private bool hasFrame;

    public bool Store(UdpMocapPacketFrame frame)
    {
        lock (syncRoot)
        {
            bool superseded = hasFrame;
            latestFrame = frame;
            hasFrame = true;
            return superseded;
        }
    }

    public bool TryTake(out UdpMocapPacketFrame frame)
    {
        lock (syncRoot)
        {
            frame = latestFrame;
            if (!hasFrame) return false;

            hasFrame = false;
            return true;
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            latestFrame = default;
            hasFrame = false;
        }
    }
}
