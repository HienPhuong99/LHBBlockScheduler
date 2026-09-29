using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace LHBBlockScheduler.Core
{
    /// <summary>
    /// Serialize/Deserialize JSON bằng DataContractJsonSerializer (có sẵn trong .NET Framework 4.8).
    /// KHÔNG dùng System.Text.Json: package này kéo theo System.Memory + System.Runtime.CompilerServices.Unsafe,
    /// cần binding redirect mà add-in chạy trong acad.exe không có -> TypeInitializationException
    /// ("The type initializer for 'System.Text.Json.JsonSerializer' threw an exception").
    /// </summary>
    public static class JsonHelper
    {
        private static DataContractJsonSerializer CreateSerializer<T>()
        {
            var settings = new DataContractJsonSerializerSettings
            {
                // Dictionary ghi dạng {"key": value} thay vì [{"Key":..,"Value":..}]
                UseSimpleDictionaryFormat = true
            };
            return new DataContractJsonSerializer(typeof(T), settings);
        }

        public static string Serialize<T>(T obj)
        {
            var serializer = CreateSerializer<T>();
            using (var ms = new MemoryStream())
            {
                // Ghi JSON có thụt lề để người dùng mở file đọc được
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, false, true, "  "))
                {
                    serializer.WriteObject(writer, obj);
                    writer.Flush();
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var serializer = CreateSerializer<T>();
            byte[] bytes = Encoding.UTF8.GetBytes(json.TrimStart('﻿'));
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, XmlDictionaryReaderQuotas.Max))
            {
                return serializer.ReadObject(reader) as T;
            }
        }
    }
}
