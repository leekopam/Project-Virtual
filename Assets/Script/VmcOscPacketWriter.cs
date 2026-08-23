using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class VmcOscPacketWriter
{
    private readonly MemoryStream stream = new MemoryStream(1024);
    private byte[] textBuffer = new byte[64];

    public void Reset()
    {
        stream.Position = 0;
        stream.SetLength(0);
        WriteOscString("#bundle");
        WriteInt32(0);
        WriteInt32(1);
    }

    public void AddBone(string boneName, Vector3 localPosition, Quaternion localRotation)
    {
        long lengthOffset = BeginMessage("/VMC/Ext/Bone/Pos", ",sfffffff");
        WriteOscString(boneName);
        WriteFloat(localPosition.x);
        WriteFloat(localPosition.y);
        WriteFloat(localPosition.z);
        WriteFloat(localRotation.x);
        WriteFloat(localRotation.y);
        WriteFloat(localRotation.z);
        WriteFloat(localRotation.w);
        EndMessage(lengthOffset);
    }

    public void AddBlend(string expressionName, float value)
    {
        long lengthOffset = BeginMessage("/VMC/Ext/Blend/Val", ",sf");
        WriteOscString(expressionName);
        WriteFloat(Mathf.Clamp01(value));
        EndMessage(lengthOffset);
    }

    public void AddBlendApply()
    {
        long lengthOffset = BeginMessage("/VMC/Ext/Blend/Apply", ",");
        EndMessage(lengthOffset);
    }

    public ArraySegment<byte> GetPacket()
    {
        return new ArraySegment<byte>(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private long BeginMessage(string address, string typeTag)
    {
        long lengthOffset = stream.Position;
        WriteInt32(0);
        WriteOscString(address);
        WriteOscString(typeTag);
        return lengthOffset;
    }

    private void EndMessage(long lengthOffset)
    {
        long endOffset = stream.Position;
        long messageOffset = lengthOffset + 4;
        stream.Position = lengthOffset;
        WriteInt32(checked((int)(endOffset - messageOffset)));
        stream.Position = endOffset;
    }

    private void WriteOscString(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        if (textBuffer.Length < byteCount)
            Array.Resize(ref textBuffer, Mathf.NextPowerOfTwo(byteCount));

        int written = Encoding.UTF8.GetBytes(value, 0, value.Length, textBuffer, 0);
        stream.Write(textBuffer, 0, written);
        stream.WriteByte(0);
        while ((stream.Position & 3) != 0)
            stream.WriteByte(0);
    }

    private void WriteFloat(float value)
    {
        WriteInt32(BitConverter.SingleToInt32Bits(value));
    }

    private void WriteInt32(int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
