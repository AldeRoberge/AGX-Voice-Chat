using System.Text;
using LiteNetLib.Utils;

namespace AGH.Voice.Scripts
{
    /// <summary>
    /// Helpers to match AGH server wire format (e.g. large string = length-prefixed UTF8).
    /// </summary>
    public static class NetIOHelper
    {
        public static void PutLargeString(NetDataWriter writer, string value)
        {
            var bytes = value != null ? Encoding.UTF8.GetBytes(value) : System.Array.Empty<byte>();
            writer.PutBytesWithLength(bytes);
        }

        public static string GetLargeString(NetDataReader reader)
        {
            var bytes = reader.GetBytesWithLength();
            return bytes != null && bytes.Length > 0 ? Encoding.UTF8.GetString(bytes) : string.Empty;
        }
    }
}
