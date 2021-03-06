﻿// -----------------------------------------------------------------------
// <copyright company="Fireasy"
//      email="faib920@126.com"
//      qq="55570729">
//   (c) Copyright Fireasy. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Fireasy.Common;
using Fireasy.Common.Extensions;
using Fireasy.Data.Entity.Extensions;
using Fireasy.Data.Entity.Metadata;

namespace Fireasy.Data.Entity
{
    /// <summary>
    /// 实体持久化的环境。无法继承此类。
    /// </summary>
    [Serializable]
    public sealed class EntityPersistentEnvironment
    {
        private readonly Dictionary<string, object> parameters = new Dictionary<string, object>();

        /// <summary>
        /// 获取或设置格式化器。
        /// </summary>
        public Func<string, string> Formatter { get; set; }

        /// <summary>
        /// 添加一个环境变量，如果当前环境中已经存在该变量名称，则使用新值进行替换。
        /// </summary>
        /// <remarks>
        /// 如果实体类使用了 <see cref="EntityVariableAttribute"/> 标记，则该变量名称应包含在 TableName 中。
        /// </remarks>
        /// <param name="name">变量名称。</param>
        /// <param name="value">变量的值。</param>
        public void AddVariable(string name, object value)
        {
            Guard.ArgumentNull(name, "name");
            Guard.ArgumentNull(value, "value");

            object v;
            if (!parameters.TryGetValue(name, out v))
            {
                parameters.Add(name, value);
            }
            else if (!v.Equals(value))
            {
                parameters[name] = value;
            }
        }

        /// <summary>
        /// 移除指定的环境变量。
        /// </summary>
        /// <param name="name">变量名称。</param>
        public void RemoveVariable(string name)
        {
            Guard.ArgumentNull(name, "name");
            if (parameters.ContainsKey(name))
            {
                parameters.Remove(name);
            }
        }

        /// <summary>
        /// 使用所添加的变量解析实体映射的数据表名称。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <returns></returns>
        public string GetVariableTableName(Type entityType)
        {
            Guard.ArgumentNull(entityType, "entityType");
            return GetVariableTableName(EntityMetadataUnity.GetEntityMetadata(entityType));
        }

        /// <summary>
        /// 使用所添加的变量解析实体映射的数据表名称。
        /// </summary>
        /// <param name="entityType">实体类型。</param>
        /// <param name="entity">当前实体</param>
        /// <returns></returns>
        public string GetVariableTableName(Type entityType, IEntity entity)
        {
            Guard.ArgumentNull(entityType, "entityType");
            var metadata = EntityMetadataUnity.GetEntityMetadata(entityType);

            var regx = new Regex(@"(<\w+>)");
            var matches = regx.Matches(metadata.TableName);
            if (matches.Count == 0)
            {
                return metadata.TableName;
            }
            var tableName = metadata.TableName;
            var flag = new AssertFlag();
            foreach (Match match in matches)
            {
                var key = match.Value.TrimStart('<').TrimEnd('>');
                object v;
                if (parameters.TryGetValue(key, out v))
                {
                    v.As<IProperty>(p =>
                    {
                        var value = entity.InternalGetValue(p);
                        if (!value.IsNullOrEmpty())
                        {
                            flag.AssertTrue();
                            tableName = tableName.Replace(match.Value, value.ToString());
                        }
                    });
                }
            }
            if (flag.AssertTrue())
            {
                return GetVariableTableName(metadata);
            }
            return tableName;
        }

        /// <summary>
        /// 使用所添加的变量解析实体映射的数据表名称。
        /// </summary>
        /// <param name="metadata">实体元数据。</param>
        /// <returns></returns>
        public string GetVariableTableName(EntityMetadata metadata)
        {
            if (Formatter != null)
            {
                return Formatter(metadata.TableName);
            }

            var regx = new Regex(@"(<\w+>)");
            var matches = regx.Matches(metadata.TableName);
            if (matches.Count == 0)
            {
                return metadata.TableName;
            }
            var tableName = metadata.TableName;
            foreach (Match match in matches)
            {
                var key = match.Value.TrimStart('<').TrimEnd('>');
                object v;
                if (parameters.TryGetValue(key, out v))
                {
                    tableName = tableName.Replace(match.Value, v.ToString());
                }
            }
            return tableName;
        }
    }
}
