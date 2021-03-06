﻿// -----------------------------------------------------------------------
// <copyright company="Fireasy"
//      email="faib920@126.com"
//      qq="55570729">
//   (c) Copyright Fireasy. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections;
#if !SILVERLIGHT
using System.Data;
using System.Drawing;
using System.Linq;
#endif
using System.Globalization;
using Fireasy.Common.Extensions;
using System.Runtime.InteropServices;
#if !N35
using System.Dynamic;
using Fireasy.Common.Dynamic;
#endif
using Fireasy.Common.ComponentModel;
using Fireasy.Common.Reflection;
using System.Collections.Generic;

namespace Fireasy.Common.Serialization
{
    internal sealed class JsonSerialize : IDisposable
    {
        private readonly JsonSerializeOption option;
        private readonly JsonSerializer serializer;
        private JsonWriter jsonWriter;
        private readonly SerializeContext context;
        private bool isDisposed;

        internal JsonSerialize(JsonSerializer serializer, JsonWriter writer, JsonSerializeOption option)
        {
            this.serializer = serializer;
            jsonWriter = writer;
            this.option = option;
            context = new SerializeContext();
        }

        /// <summary>
        /// 将对象序列化为文本。
        /// </summary>
        /// <param name="value">要序列化的值。</param>
        internal void Serialize(object value)
        {
            if ((value == null) || DBNull.Value.Equals(value))
            {
                jsonWriter.WriteNull();
                return;
            }

            if (WithSerializable(value))
            {
                return;
            }

            var type = value.GetType();
            if (WithConverter(type, value))
            {
                return;
            }

#if !SILVERLIGHT
            if (type == typeof(DataSet))
            {
                SerializeDataSet(value as DataSet);
                return;
            }

            if (type == typeof(DataTable))
            {
                SerializeDataTable(value as DataTable);
                return;
            }

            if (type == typeof(DataRow))
            {
                SerializeDataRow(value as DataRow, null);
                return;
            }

            if (type == typeof(Color))
            {
                SerializeColor((Color)value);
                return;
            }
#endif
            if (typeof(_Type).IsAssignableFrom(type))
            {
                SerializeType((Type)(object)value);
                return;
            }

#if !N35
            if (typeof(IDynamicMetaObjectProvider).IsAssignableFrom(type))
            {
                SerializeDynamicObject((IDictionary<string, object>)value);
                return;
            }
#endif

            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                SerializeDictionary(value as IDictionary);
                return;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type) && type != typeof(string))
            {
                SerializeEnumerable(value as IEnumerable);
                return;
            }

            if (type == typeof(byte[]))
            {
                SerializeBytes((byte[])value);
                return;
            }

            SerializeValue(value);
        }

        private bool WithSerializable(object value)
        {
            var ser = value.As<ITextSerializable>();
            if (ser != null)
            {
                var result = ser.Serialize(serializer);
                jsonWriter.WriteValue(result);
                return true;
            }

            return false;
        }

        private bool WithConverter(Type type, object value)
        {
            var converter = option.Converters.GetConverter(type);
            if ((converter is JsonConverter || converter is ValueConverter) && converter.CanWrite)
            {
                var jsonConvert = converter as JsonConverter;
                if (jsonConvert != null && jsonConvert.Streaming)
                {
                    jsonConvert.WriteJson(serializer, jsonWriter, value);
                }
                else
                {
                    jsonWriter.WriteValue(converter.WriteObject(serializer, value));
                }

                return true;
            }

            return false;
        }

#if !SILVERLIGHT
        private void SerializeDataSet(DataSet set)
        {
            var flag = new AssertFlag();
            jsonWriter.WriteStartObject();
            foreach (DataTable table in set.Tables)
            {
                if (!flag.AssertTrue())
                {
                    jsonWriter.WriteComma();
                }

                jsonWriter.WriteKey(SerializeName(table.TableName));
                SerializeDataTable(table);
            }

            jsonWriter.WriteEndObject();
        }

        private void SerializeDataTable(DataTable table)
        {
            var flag = new AssertFlag();
            jsonWriter.WriteStartArray();
            var columns = GetDataColumns(table).ToList();
            foreach (DataRow row in table.Rows)
            {
                if (!flag.AssertTrue())
                {
                    jsonWriter.WriteComma();
                }

                SerializeDataRow(row, columns);
            }

            jsonWriter.WriteEndArray();
        }

        private void SerializeDataRow(DataRow row, List<DataColumn> columns)
        {
            if (columns == null)
            {
                columns = GetDataColumns(row.Table).ToList();
            }

            var flag = new AssertFlag();
            jsonWriter.WriteStartObject();
            foreach (var column in columns)
            {
                if (!flag.AssertTrue())
                {
                    jsonWriter.WriteComma();
                }

                jsonWriter.WriteKey(SerializeName(column.ColumnName));
                JsonConvertContext.Current.Assign(column.ColumnName, row[column], () => SerializeValue(row[column]));
            }

            jsonWriter.WriteEndObject();
        }

        private void SerializeColor(Color color)
        {
            var value = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", color.A, color.R, color.G, color.B);
            SerializeString(value);
        }

#endif

        private void SerializeEnumerable(IEnumerable enumerable)
        {
            var flag = new AssertFlag();
            jsonWriter.WriteStartArray();

            foreach (var current in enumerable)
            {
                if (!flag.AssertTrue())
                {
                    jsonWriter.WriteComma();
                }

                Serialize(current);
            }

            jsonWriter.WriteEndArray();
        }

        private void SerializeDictionary(IDictionary dictionary)
        {
            var flag = new AssertFlag();
            jsonWriter.WriteStartObject();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (!flag.AssertTrue())
                {
                    jsonWriter.WriteComma();
                }

                SerializeKeyValue(entry.Key, entry.Value);
            }

            jsonWriter.WriteEndObject();
        }

#if !N35
        private void SerializeDynamicObject(IDictionary<string, object> dynamicObject)
        {
            var flag = new AssertFlag();

            jsonWriter.WriteStartObject();
            foreach (var name in dynamicObject.Keys)
            {
                if (!flag.AssertTrue())
                {
                    jsonWriter.WriteComma();
                }

                jsonWriter.WriteKey(SerializeName(name));

                object value;
                dynamicObject.TryGetValue(name, out value);
                JsonConvertContext.Current.Assign(name, value, () => Serialize(value));
            }

            jsonWriter.WriteEndObject();
        }
#endif

        private void SerializeObject(object obj)
        {
            var lazyMgr = obj.As<ILazyManager>();
            var flag = new AssertFlag();
            var type = obj.GetType();
            jsonWriter.WriteStartObject();

            foreach (var acc in GetAccessorCache(type))
            {
                if (acc.Filter(acc.PropertyInfo, lazyMgr))
                {
                    continue;
                }

                var value = acc.Accessor.GetValue(obj);
                if (option.IgnoreNull && value == null)
                {
                    continue;
                }

                if (!flag.AssertTrue())
                {
                    jsonWriter.WriteComma();
                }

                jsonWriter.WriteKey(SerializeName(acc.PropertyName));
                JsonConvertContext.Current.Assign(acc.Accessor.PropertyInfo.Name, value, () => Serialize(value));
            }

            jsonWriter.WriteEndObject();
        }

        private void SerializeBytes(byte[] bytes)
        {
            jsonWriter.WriteValue(Convert.ToBase64String(bytes, 0, bytes.Length));
        }

        private void SerializeValue(object value)
        {
            var type = value.GetType();
            if (type.IsNullableType())
            {
                type = type.GetNonNullableType();
            }

            if (type.IsEnum)
            {
                jsonWriter.WriteValue(((Enum)value).ToString("D"));
                return;
            }

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    jsonWriter.WriteValue((bool)value ? "true" : "false");
                    break;
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Decimal:
                case TypeCode.Single:
                case TypeCode.Double:
                    jsonWriter.WriteValue(value.As<IFormattable>().ToString("G", CultureInfo.InvariantCulture));
                    break;
                case TypeCode.DateTime:
                    SerializeDateTime((DateTime)value);
                    break;
                case TypeCode.Char:
                case TypeCode.String:
                    SerializeString(value);
                    break;
                default:
                    context.TrySerialize(value, () => SerializeObject(value));
                    break;
            }
        }

        private void SerializeDateTime(DateTime value)
        {
#if !SILVERLIGHT
            var offset = TimeZone.CurrentTimeZone.GetUtcOffset(value);
#else
            var offset = TimeZoneInfo.Local.GetUtcOffset(value);
#endif
            var time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var ticks = (value.ToUniversalTime().Ticks - time.Ticks) / 10000;
            if (option.Format == JsonFormat.String)
            {
                jsonWriter.WriteValue("\"" + (value.Year <= 1 ? string.Empty : value.ToString("yyyy-MM-dd")) + "\"");
                //var sb = new StringBuilder();
                //sb.Append("\"\\/Date(" + ticks);
                //sb.Append(offset.Ticks >= 0 ? "+" : "-");
                //var h = Math.Abs(offset.Hours);
                //var m = Math.Abs(offset.Minutes);
                //if (h < 10)
                //{
                //    sb.Append(0);
                //}

                //sb.Append(h);
                //if (m < 10)
                //{
                //    sb.Append(0);
                //}

                //sb.Append(m);
                //sb.Append(")\\/\"");
                //jsonWriter.WriteValue(sb);
            }
            else
            {
                jsonWriter.WriteValue("new Date(" + ticks + ")");
            }
        }

        private void SerializeString<T>(T value)
        {
            var str = value.ToString();
#if SILVERLIGHT
            m_writer.WriteValue(String.Format("{0}{1}{0}", JsonTokens.StringDelimiter, str));
#else
            jsonWriter.WriteString(str);
#endif
        }

        private void SerializeKeyValue<TKey, TValue>(TKey key, TValue value)
        {
            jsonWriter.WriteKey(SerializeName(key.ToString()));
            JsonConvertContext.Current.Assign(key.ToString(), value, () => Serialize(value));
        }

        private string SerializeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            if (option.Format == JsonFormat.Object)
            {
                return option.CamelNaming ? char.ToLower(name[0]) + name.Substring(1) : name;
            }

            return JsonTokens.StringDelimiter + (option.CamelNaming ? char.ToLower(name[0]) + name.Substring(1) : name) + JsonTokens.StringDelimiter;
        }

        private void SerializeType(Type type)
        {
            if (option.IgnoreType)
            {
                jsonWriter.WriteNull();
            }
            else
            {
                jsonWriter.WriteString(type.FullName + ", " + type.Assembly.FullName);
            }
        }

        /// <summary>
        /// 释放对象所占用的非托管和托管资源。
        /// </summary>
        /// <param name="disposing">为 true 则释放托管资源和非托管资源；为 false 则仅释放非托管资源。</param>
        private void Dispose(bool disposing)
        {
            if (isDisposed)
            {
                return;
            }

            if (disposing)
            {
                context.Dispose();
                jsonWriter.Dispose();
                jsonWriter = null;
            }

            isDisposed = true;
        }

        /// <summary>
        /// 获取指定类型的属性访问缓存。
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private List<PropertyGetAccessorCache> GetAccessorCache(Type type)
        {
            return context.GetAccessors.TryGetValue(type, () =>
            {
                return type.GetProperties()
                    .Where(s => s.CanRead && !SerializerUtil.IsNoSerializable(option, s))
                    .Distinct(new SerializerUtil.PropertyEqualityComparer())
                    .Select(s => new PropertyGetAccessorCache
                    {
                        Accessor = ReflectionCache.GetAccessor(s),
                        Filter = (p, l) =>
                        {
                            return !SerializerUtil.CheckLazyValueCreate(l, p.Name);
                        },
                        PropertyInfo = s,
                        PropertyName = SerializerUtil.GetPropertyName(s)
                    })
                    .Where(s => !string.IsNullOrEmpty(s.PropertyName))
                    .ToList();
            });
        }

        private IEnumerable<DataColumn> GetDataColumns(DataTable table)
        {
            foreach (DataColumn column in table.Columns)
            {
                if (!SerializerUtil.IsNoSerializable(option, column.ColumnName))
                {
                    yield return column;
                }
            }
        }

        /// <summary>
        /// 释放对象所占用的所有资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
