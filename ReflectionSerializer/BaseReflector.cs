﻿using CascadeParser;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CascadeSerializer
{
    public abstract class BaseReflector : IReflectionProvider
    {
        public virtual T GetSingleAttributeOrDefault<T>(MemberInfo memberInfo) where T : Attribute, new()
        {
            object[] attributes = memberInfo.GetCustomAttributes(typeof(T), false);
            return attributes.Length == 0 ? new T() : attributes[0] as T;
        }

        static readonly BindingFlags CollectMembersP = 
            BindingFlags.Public | 
            BindingFlags.Instance | 
            BindingFlags.DeclaredOnly;

        static readonly BindingFlags CollectMembersNP = CollectMembersP | BindingFlags.NonPublic;

        MemberSerialization GetMemberSerialization(Type type)
        {
            object[] attributes = type.GetCustomAttributes(false);
            for (int i = 0; i < attributes.Length; ++i)
            {
                var dm = attributes[i] as CascadeObjectAttribute;
                if (dm != null)
                    return dm.MemberSerialization;
            }
            return MemberSerialization.OptOut;
        }

        private void CollectSerializableMembers(Type type, List<MemberInfo> outMembers)
        {
            MemberSerialization ms = GetMemberSerialization(type);

            BindingFlags flags = CollectMembersNP;
            if(ms == MemberSerialization.OptOut)
                flags = CollectMembersP;

            if (ms != MemberSerialization.Fields)
            {
                PropertyInfo[] properties = type.GetProperties(flags);
                for (int i = 0; i < properties.Length; i++)
                {
                    PropertyInfo p = properties[i];

                    if (IsIgnogeMember(p))
                        continue;

                    if (ms == MemberSerialization.OptIn && !IsSerializeMember(p))
                        continue;

                    MethodInfo get_info = p.GetGetMethod();
                    int get_params_count = get_info != null ? get_info.GetParameters().Length : 0;

                    MethodInfo set_info = p.GetSetMethod(true);

                    if (get_info != null && set_info != null && get_params_count == 0)
                        outMembers.Add(p);
                }
            }

            FieldInfo[] fields = type.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo f = fields[i];

                if (IsIgnogeMember(f))
                    continue;

                if (ms == MemberSerialization.OptIn && !IsSerializeMember(f))
                    continue;

                if (!f.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    outMembers.Add(f);
            }
        }

        bool IsSerializeMember(MemberInfo memberInfo)
        {
            object[] attributes = memberInfo.GetCustomAttributes(false);
            for (int i = 0; i < attributes.Length; ++i)
            {
                if (attributes[i] is CascadePropertyAttribute)
                    return true;
            }
            return false;
        }

        public virtual MemberInfo[] GetSerializableMembers(Type type)
        {
            List<MemberInfo> lst = new List<MemberInfo>();
            CollectSerializableMembers(type, lst);
            return lst.ToArray();
        }

        bool IsIgnogeMember(MemberInfo memberInfo)
        {
            object[] attributes = memberInfo.GetCustomAttributes(false);
            for (int i = 0; i < attributes.Length; ++i)
            {
                if (attributes[i] is CascadeIgnoreAttribute || attributes[i] is NonSerializedAttribute)
                    return true;
                CascadePropertyAttribute dm = attributes[i] as CascadePropertyAttribute;
                if (dm != null && dm.Ignore)
                    return true;
            }
            return false;
        }

        public abstract object Instantiate(Type type, ILogPrinter inLogger);
        public abstract object GetValue(MemberInfo member, object instance);
        public abstract void SetValue(MemberInfo member, object instance, object value, ILogPrinter inLogger);
        public abstract MethodHandler GetDelegate(MethodBase method);
    }
}
