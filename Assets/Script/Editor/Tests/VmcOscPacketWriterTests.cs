using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

public class VmcOscPacketWriterTests
{
    [Test]
    public void Writer_CreatesBundleWithBoneBlendAndApplyMessages()
    {
        var writer = new VmcOscPacketWriter();
        writer.Reset();
        writer.AddBone("Head", new Vector3(1f, 2f, 3f), new Quaternion(0f, 0f, 0f, 1f));
        writer.AddBlend("Joy", 0.75f);
        writer.AddBlendApply();

        ArraySegment<byte> packet = writer.GetPacket();
        IReadOnlyList<OscMessage> messages = ParseBundle(packet);

        Assert.That(messages.Count, Is.EqualTo(3));
        Assert.That(messages[0].Address, Is.EqualTo("/VMC/Ext/Bone/Pos"));
        Assert.That(messages[0].TypeTag, Is.EqualTo(",sfffffff"));
        Assert.That(messages[0].ReadString(), Is.EqualTo("Head"));
        Assert.That(messages[0].ReadFloat(), Is.EqualTo(1f));
        Assert.That(messages[0].ReadFloat(), Is.EqualTo(2f));
        Assert.That(messages[0].ReadFloat(), Is.EqualTo(3f));
        Assert.That(messages[1].Address, Is.EqualTo("/VMC/Ext/Blend/Val"));
        Assert.That(messages[1].TypeTag, Is.EqualTo(",sf"));
        Assert.That(messages[1].ReadString(), Is.EqualTo("Joy"));
        Assert.That(messages[1].ReadFloat(), Is.EqualTo(0.75f));
        Assert.That(messages[2].Address, Is.EqualTo("/VMC/Ext/Blend/Apply"));
        Assert.That(messages[2].TypeTag, Is.EqualTo(","));
    }

    private static IReadOnlyList<OscMessage> ParseBundle(ArraySegment<byte> packet)
    {
        byte[] bytes = packet.Array;
        int offset = packet.Offset;
        Assert.That(ReadPaddedString(bytes, ref offset), Is.EqualTo("#bundle"));
        offset += 8;

        var messages = new List<OscMessage>();
        int end = packet.Offset + packet.Count;
        while (offset < end)
        {
            int length = ReadInt32(bytes, ref offset);
            int messageEnd = offset + length;
            string address = ReadPaddedString(bytes, ref offset);
            string typeTag = ReadPaddedString(bytes, ref offset);
            messages.Add(new OscMessage(bytes, offset, messageEnd, address, typeTag));
            offset = messageEnd;
        }

        return messages;
    }

    private static int ReadInt32(byte[] bytes, ref int offset)
    {
        int value = (bytes[offset] << 24) |
                    (bytes[offset + 1] << 16) |
                    (bytes[offset + 2] << 8) |
                    bytes[offset + 3];
        offset += 4;
        return value;
    }

    private static string ReadPaddedString(byte[] bytes, ref int offset)
    {
        int start = offset;
        while (bytes[offset] != 0) offset++;
        string value = Encoding.UTF8.GetString(bytes, start, offset - start);
        offset++;
        while ((offset & 3) != 0) offset++;
        return value;
    }

    private sealed class OscMessage
    {
        private readonly byte[] bytes;
        private readonly int end;
        private int offset;

        public OscMessage(byte[] bytes, int offset, int end, string address, string typeTag)
        {
            this.bytes = bytes;
            this.offset = offset;
            this.end = end;
            Address = address;
            TypeTag = typeTag;
        }

        public string Address { get; }
        public string TypeTag { get; }

        public string ReadString()
        {
            Assert.That(offset, Is.LessThan(end));
            return ReadPaddedString(bytes, ref offset);
        }

        public float ReadFloat()
        {
            Assert.That(offset + 4, Is.LessThanOrEqualTo(end));
            int bits = ReadInt32(bytes, ref offset);
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }
    }
}
