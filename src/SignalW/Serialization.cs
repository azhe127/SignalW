﻿using System;
using System.Reflection;
using System.Runtime.Serialization.Formatters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace DataSpreads.SignalW {

    /// <summary>
    /// Extensions for JSON.NET
    /// </summary>
    public static class JsonExtensions {

        /// <summary>
        ///
        /// </summary>
        public static T FromJson<T>(this string json) {
            var obj = JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings {
                TypeNameHandling = TypeNameHandling.None
            });
            return obj;
        }

        internal static IMessage FromJson(this string json) {
            var obj = JsonConvert.DeserializeObject<IMessage>(json, new MessageConverter());
            return obj;
        }

        /// <summary>
        ///
        /// </summary>
        public static object FromJson(this string json, Type type) {
            var obj = JsonConvert.DeserializeObject(json, type, new JsonSerializerSettings {
                TypeNameHandling = TypeNameHandling.None,
                // NB this is important for correctness and performance
                // Transaction could have many null properties
                NullValueHandling = NullValueHandling.Ignore
            });
            return obj;
        }

        /// <summary>
        ///
        /// </summary>
        public static string ToJson<T>(this T obj) {
            var message = JsonConvert.SerializeObject(obj, Formatting.None,
                new JsonSerializerSettings {
                    TypeNameHandling = TypeNameHandling.None, // Objects
                    TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple,
                    // NB this is important for correctness and performance
                    // Transaction could have many null properties
                    NullValueHandling = NullValueHandling.Ignore
                });
            return message;
        }

        /// <summary>
        /// Returns indented JSON
        /// </summary>
        public static string ToJsonFormatted<T>(this T obj) {
            var message = JsonConvert.SerializeObject(obj, Formatting.Indented,
                new JsonSerializerSettings {
                    TypeNameHandling = TypeNameHandling.None, // Objects
                    TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple,
                    // NB this is important for correctness and performance
                    // Transaction could have many null properties
                    NullValueHandling = NullValueHandling.Ignore
                });
            return message;
        }

    }

    /// <summary>
    /// Limits enum serialization only to defined values
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class SafeEnumConverter<T> : StringEnumConverter {

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
            var isDef = Enum.IsDefined(typeof(T), value);
            if (!isDef) {
                value = null;
            }
            base.WriteJson(writer, value, serializer);
        }
    }

    /// <summary>
    /// Serialize as string with ToString()
    /// </summary>
    public class ToStringConverter<T> : JsonConverter {

        public override bool CanConvert(Type objectType) {
            return true;
        }

        public override object ReadJson(JsonReader reader,
                                        Type objectType,
                                         object existingValue,
                                         JsonSerializer serializer) {
            var t = JToken.Load(reader);
            T target = t.Value<T>();
            return target;
        }

        public override void WriteJson(JsonWriter writer,
                                       object value,
                                       JsonSerializer serializer) {
            var t = JToken.FromObject(value.ToString());
            t.WriteTo(writer);
        }
    }

    /// <summary>
    /// Serialize Decimal to string without trailing zeros
    /// </summary>
    public class DecimalG29ToStringConverter : JsonConverter {

        public override bool CanConvert(Type objectType) {
            return objectType.Equals(typeof(decimal));
        }

        public override object ReadJson(JsonReader reader,
                                        Type objectType,
                                         object existingValue,
                                         JsonSerializer serializer) {
            var t = JToken.Load(reader);
            return t.Value<decimal>();
        }

        public override void WriteJson(JsonWriter writer,
                                       object value,
                                       JsonSerializer serializer) {
            decimal d = (decimal)value;
            var t = JToken.FromObject(d.ToString("G29"));
            t.WriteTo(writer);
        }
    }

    /// <summary>
    /// Convert DateTime to HHMMSS
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public class HHMMSSDateTimeConverter : JsonConverter {

        public override bool CanConvert(Type objectType) {
            return true;
        }

        public override object ReadJson(JsonReader reader,
                                        Type objectType,
                                         object existingValue,
                                         JsonSerializer serializer) {
            var t = JToken.Load(reader);
            var target = t.Value<string>();
            if (target == null) return null;
            var hh = int.Parse(target.Substring(0, 2));
            var mm = int.Parse(target.Substring(2, 2));
            var ss = int.Parse(target.Substring(4, 2));
            var now = DateTime.Now;
            var dt = new DateTime(now.Year, now.Month, now.Day, hh, mm, ss);
            return dt;
        }

        public override void WriteJson(JsonWriter writer,
                                       object value,
                                       JsonSerializer serializer) {
            var t = JToken.FromObject(((DateTime)value).ToString("HHmmss"));
            t.WriteTo(writer);
        }
    }

    internal class MessageConverter : JsonCreationConverter<IMessage> {
        // TODO reflection to cache types by names and use activator create instance
        // http://mattgabriel.co.uk/2016/02/10/object-creation-using-lambda-expression/
        //static MessageConverter()
        //{
        //    var types = AppDomain.CurrentDomain
        //            .GetAssemblies()
        //            .Except(typeof(DataContext).Assembly.ItemAsList())
        //            //.Where(a => !a.CodeBase.Contains("mscorlib.dll"))
        //            .SelectMany(s => {
        //                try {
        //                    return s.GetTypes();
        //                } catch {
        //                    return new Type[] { };
        //                }
        //            })
        //            .Where(p => {
        //                try {
        //                    return typeof(IData).IsAssignableFrom(p)
        //                           && !typeof(IDistributedDataObject).IsAssignableFrom(p)
        //                           && p.IsClass && !p.IsAbstract;
        //                } catch {
        //                    return false;
        //                }
        //            }).ToList();
        //}
        public MessageConverter() {
        }
        // we learn object type from correlation id and a type stored in responses dictionary
        // ReSharper disable once RedundantAssignment
        protected override IMessage Create(Type objectType, JObject jObject) {
            if (FieldExists("type", jObject)) {
                // without id we have an event
                var type = jObject.GetValue("type").Value<string>();
                switch (type) {
                    case "ping":
                        return new PingMessage();
                    case "pong":
                        return new PongMessage();
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            throw new ArgumentException("Bad message format: no type field");
        }

        private static bool FieldExists(string fieldName, JObject jObject) {
            return jObject[fieldName] != null;
        }
    }

    internal abstract class JsonCreationConverter<T> : JsonConverter {

        /// <summary>
        /// Create an instance of objectType, based properties in the JSON object
        /// </summary>
        /// <param name="objectType">type of object expected</param>
        /// <param name="jObject">
        /// contents of JSON object that will be deserialized
        /// </param>
        /// <returns></returns>
        protected abstract T Create(Type objectType, JObject jObject);

        public override bool CanConvert(Type objectType) {
            return typeof(T).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader,
                                        Type objectType,
                                         object existingValue,
                                         JsonSerializer serializer) {
            // Load JObject from stream
            JObject jObject = JObject.Load(reader);

            // Create target object based on JObject
            T target = Create(objectType, jObject);

            // Populate the object properties
            serializer.Populate(jObject.CreateReader(), target);

            return target;
        }

        public override void WriteJson(JsonWriter writer,
                                       object value,
                                       JsonSerializer serializer) {
            throw new InvalidOperationException();
        }
    }

}