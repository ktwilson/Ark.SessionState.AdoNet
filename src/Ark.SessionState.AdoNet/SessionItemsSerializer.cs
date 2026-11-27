using System.IO;
using System.Web.SessionState;

namespace Ark.SessionState.AdoNet
{
    /// <summary>
    /// Converts a <see cref="SessionStateItemCollection"/> to and from the bytes stored
    /// in the SessionItems column, using ASP.NET's own session serialization format.
    /// Values must be serializable the same way they would be for the built-in
    /// StateServer and SQLServer modes.
    /// </summary>
    internal static class SessionItemsSerializer
    {
        public static byte[] Serialize(SessionStateItemCollection items)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    items?.Serialize(writer);
                }
                return stream.ToArray();
            }
        }

        public static SessionStateItemCollection Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                return new SessionStateItemCollection();

            using (var stream = new MemoryStream(data))
            using (var reader = new BinaryReader(stream))
            {
                return SessionStateItemCollection.Deserialize(reader);
            }
        }
    }
}
