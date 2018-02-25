using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using Xunit;

namespace STKCore.Test
{
    public class UnitTest1
    {
        public static Delegate CastHelper<T>()
        {
            var typeConverter = TypeDescriptor.GetConverter(typeof(T));
            return (Func<object,T>)(arg => { return (T) typeConverter.ConvertFrom(arg); });
        }

        public static Delegate CastHelper(Type dst)
        {
            Expression<Action> act = () => CastHelper<double>();
            MethodInfo methodInfo = (act.Body as MethodCallExpression).Method;
            MethodInfo dstMethodInfo = methodInfo.GetGenericMethodDefinition().MakeGenericMethod(dst);
            return (Delegate)(dstMethodInfo.Invoke(null,null));
        }

        public static Delegate ResolveTypecast(Type dst)
        {

            var typeConverter = TypeDescriptor.GetConverter(dst);
            Expression<Action> act = () => typeConverter.ConvertFrom(null);
            var param = Expression.Parameter(typeof(object), "src");
            MethodInfo mi = ((MethodCallExpression)act.Body).Method;
            var expr = Expression.Convert(Expression.Call(Expression.Constant(typeConverter), mi, param), dst);
            return Expression.Lambda(expr,true,param).Compile();
        }

        [Fact]
        public void Test1()
        {
            //NetManager中使用Expression进行类型转换，与用delegate进行类型转换的效率对比
            Random src = new Random();
            List<string> data = new List<string>();
            for (int i = 0; i < 100000; i++)
            {
                data.Add(src.Next(1000000,9999999).ToString());
            }
            Stopwatch delegateMethod = new Stopwatch();
            Stopwatch expressionMethod = new Stopwatch();
            List<int> result1 = new List<int>(100000);
            var func = (Func<object, int>) ResolveTypecast(typeof(int));
            expressionMethod.Start();
            foreach (var element in data)
            {
                result1.Add(func(element));
            }
            expressionMethod.Stop();
            List<int> result2 = new List<int>(100000);
            var func2 = (Func<object, int>)CastHelper(typeof(int));
            delegateMethod.Start();
            foreach (var element in data)
            {
                result2.Add(func2(element));
            }
            delegateMethod.Stop();
            Debug.WriteLine(expressionMethod.ElapsedTicks);
            Debug.WriteLine(delegateMethod.ElapsedTicks);
        }
    }
}
