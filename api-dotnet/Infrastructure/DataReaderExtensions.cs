using MySqlConnector;

namespace Opstrax.Api.Infrastructure;

public static class DataReaderExtensions
{
    public static bool IsDBNull(this MySqlDataReader reader, string name) => reader.IsDBNull(reader.GetOrdinal(name));
    public static string GetString(this MySqlDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));
    public static long GetInt64(this MySqlDataReader reader, string name) => reader.GetInt64(reader.GetOrdinal(name));
    public static int GetInt32(this MySqlDataReader reader, string name) => reader.GetInt32(reader.GetOrdinal(name));
    public static decimal GetDecimal(this MySqlDataReader reader, string name) => reader.GetDecimal(reader.GetOrdinal(name));
    public static DateTime GetDateTime(this MySqlDataReader reader, string name) => reader.GetDateTime(reader.GetOrdinal(name));
}
