﻿using CascadeParser;
using System;
using System.Reflection;

namespace CascadeSerializer
{
    class EmitReflector : BaseReflector
    {
        public override object Instantiate(Type type, ILogPrinter inLogger)
        {
            var ctor = EmitHelper.CreateParameterlessConstructorHandler(type, inLogger);
            if (ctor == null)
                return null;
            return ctor();
        }

        public override object GetValue(MemberInfo member, object instance)
        {
            if (member is PropertyInfo)
                return EmitHelper.CreatePropertyGetterHandler(member as PropertyInfo)(instance);
            if (member is FieldInfo)
                return EmitHelper.CreateFieldGetterHandler(member as FieldInfo)(instance);
            throw new NotImplementedException();
        }

        public override void SetValue(MemberInfo member, object instance, object value, ILogPrinter inLogger)
        {
            if (member is PropertyInfo)
                EmitHelper.CreatePropertySetterHandler(member as PropertyInfo)(instance, value);
            else if (member is FieldInfo)
                EmitHelper.CreateFieldSetterHandler(member as FieldInfo)(instance, value);
            else throw new NotImplementedException();
        }

        public override MethodHandler GetDelegate(MethodBase method)
        {
            return EmitHelper.CreateMethodHandler(method);
        }
    }
}
