using System.Net;
using System.Net.Sockets;
using System.Text;

public static class FacialMocapStreamingRequest
{
    public const string Message = "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719";

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes(Message);

    public static void Send(UdpClient sender, IPAddress address, int port)
    {
        var destination = new IPEndPoint(address, port);
        sender.Send(Payload, Payload.Length, destination);
    }
}
