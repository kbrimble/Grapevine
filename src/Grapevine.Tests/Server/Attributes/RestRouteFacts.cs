using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Grapevine.Exceptions.Server;
using Grapevine.Interfaces.Server;
using Grapevine.Server.Attributes;
using Grapevine.Shared;
using Shouldly;
using Xunit;

namespace Grapevine.Tests.Server.Attributes
{
    public class RestRouteFacts
    {
        public class ExtensionMethods
        {
            public class GetRouteAttributesMethod
            {
                [Fact]
                public void rest_route_returns_empty_list_when_attribute_not_present()
                {
                    var attributes = typeof(TestClass).GetMethod("IsEligibleButNoAttribute").GetRouteAttributes();
                    attributes.ShouldNotBeNull();
                    attributes.Count().ShouldBe(0);
                }

                [Fact]
                public void rest_route_no_args_gets_default_properties()
                {
                    var method = typeof(RestRouteTesterHelper).GetMethod("RouteHasNoArgs");
                    var attrs = method.GetRouteAttributes().ToList();

                    attrs.Count.ShouldBe(1);
                    attrs[0].HttpMethod.ShouldBe(HttpMethod.ALL);
                    attrs[0].PathInfo.Equals(string.Empty).ShouldBeTrue();
                }

                [Fact]
                public void rest_route_httpmethod_arg_only()
                {
                    var method = typeof(RestRouteTesterHelper).GetMethod("RouteHasHttpMethodOnly");
                    var attrs = method.GetRouteAttributes().ToList();

                    attrs.Count.ShouldBe(1);
                    attrs[0].HttpMethod.ShouldBe(HttpMethod.DELETE);
                    attrs[0].PathInfo.Equals(string.Empty).ShouldBeTrue();
                }

                [Fact]
                public void rest_route_pathinfo_arg_only()
                {
                    var method = typeof(RestRouteTesterHelper).GetMethod("RouteHasPathInfoOnly");
                    var attrs = method.GetRouteAttributes().ToList();

                    attrs.Count.ShouldBe(1);
                    attrs[0].HttpMethod.ShouldBe(HttpMethod.ALL);
                    attrs[0].PathInfo.Equals("/some/path").ShouldBeTrue();
                }

                [Fact]
                public void rest_route_both_args()
                {
                    var method = typeof(RestRouteTesterHelper).GetMethod("RouteHasBothArgs");
                    var attrs = method.GetRouteAttributes().ToList();

                    attrs.Count.ShouldBe(1);
                    attrs[0].HttpMethod.ShouldBe(HttpMethod.POST);
                    attrs[0].PathInfo.Equals("/some/other/path").ShouldBeTrue();
                }

                [Fact]
                public void rest_route_multiple_attributes()
                {
                    var method = typeof(RestRouteTesterHelper).GetMethod("RouteHasMultipleAttrs");
                    var attrs = method.GetRouteAttributes().ToList();

                    attrs.Count.ShouldBe(2);
                    attrs[0].HttpMethod.ShouldBe(HttpMethod.GET);
                    attrs[0].PathInfo.Equals("/index.html").ShouldBeTrue();
                    attrs[1].HttpMethod.ShouldBe(HttpMethod.HEAD);
                    attrs[1].PathInfo.Equals("/index").ShouldBeTrue();
                }
            }

            public class HasParameterlessConstructorMethod
            {
                [Fact]
                public void has_parameterless_constructor_returns_correct_value()
                {
                    typeof(ImplicitConstructor).HasParameterlessConstructor().ShouldBeTrue();
                    typeof(ExplicitConstructor).HasParameterlessConstructor().ShouldBeTrue();
                    typeof(MultipleConstructor).HasParameterlessConstructor().ShouldBeTrue();
                    typeof(NoParameterlessConstructor).HasParameterlessConstructor().ShouldBeFalse();
                }
            }

            public class CanInvokeMethod
            {
                [Fact]
                public void can_invoke_returns_true_for_static_methods()
                {
                    typeof(TestClass).GetMethod("TestStaticMethod").CanInvoke().ShouldBeTrue();
                }

                [Fact]
                public void can_invoke_returns_false_when_method_is_abstract()
                {
                    typeof(TestAbstract).GetMethod("TestAbstractMethod").CanInvoke().ShouldBeFalse();
                }

                [Fact]
                public void can_invoke_returns_false_when_reflectedtype_is_null()
                {
                    var method = new MethodInfoWrapper(typeof(TestClass).GetMethod("TestMethod"));

                    method.IsStatic.ShouldBeFalse();
                    method.IsAbstract.ShouldBeFalse();
                    method.CanInvoke().ShouldBeFalse();
                }

                [Fact]
                public void can_invoke_returns_false_when_reflected_type_is_not_a_class()
                {
                    typeof(TestInterface).GetMethod("TestInterfaceMethod").CanInvoke().ShouldBeFalse();
                    typeof(TestStruct).GetMethod("TestStructMethod").CanInvoke().ShouldBeFalse();
                }

                [Fact]
                public void can_invoke_returns_false_when_reflected_type_is_abstract()
                {
                    typeof(TestAbstract).GetMethod("TestVirtualMethod").CanInvoke().ShouldBeFalse();
                }

                [Fact]
                public void can_invoke_returns_true_on_invokable_method()
                {
                    typeof(TestClass).GetMethod("TestMethod").CanInvoke().ShouldBeTrue();
                }
            }

            public class IsRestRouteEligibleMethod
            {
                [Fact]
                public void is_rr_eligible_returns_false_when_methodinfo_is_null()
                {
                    MethodInfo method = null;
                    Action<MethodInfo> action = info => info.IsRestRouteEligible();
                    Should.Throw<ArgumentNullException>(() => action(method));
                }

                [Fact]
                public void is_rr_eligible_returns_false_when_methodinfo_is_not_invokable()
                {
                    typeof(TestAbstract).GetMethod("TestAbstractMethod").IsRestRouteEligible().ShouldBeFalse();
                }

                [Fact]
                public void is_rr_eligible_returns_false_when_method_reflectedtype_has_no_parameterless_constructor()
                {
                    typeof(NoParameterlessConstructor).GetMethod("TestMethod").IsRestRouteEligible().ShouldBeFalse();
                }

                [Fact]
                public void is_rr_eligible_returns_false_when_method_is_special_name()
                {
                    typeof(TestClass).GetMethod("get_TestProperty").IsRestRouteEligible().ShouldBeFalse();
                }

                [Fact]
                public void is_rr_eligible_returns_false_when_return_type_is_not_ihttpcontext()
                {
                    typeof(TestClass).GetMethod("HasNoReturnValue").IsRestRouteEligible().ShouldBeFalse();
                    typeof(TestClass).GetMethod("ReturnValueIsWrongType").IsRestRouteEligible().ShouldBeFalse();
                }

                [Fact]
                public void is_rr_eligible_returns_false_when_method_accepts_more_or_less_than_one_argument()
                {
                    typeof(TestClass).GetMethod("TakesZeroArgs").IsRestRouteEligible().ShouldBeFalse();
                    typeof(TestClass).GetMethod("TakesTwoArgs").IsRestRouteEligible().ShouldBeFalse();
                }

                [Fact]
                public void is_rr_eligible_returns_false_when_first_argument_is_not_ihttpcontext()
                {
                    typeof(TestClass).GetMethod("TakesWrongArgs").IsRestRouteEligible().ShouldBeFalse();
                }

                [Fact]
                public void is_rr_eligible_throws_aggregate_exception_when_method_is_not_eligible_and_throw_exceptions_is_true()
                {
                    Should.Throw<InvalidRouteMethodExceptions>(
                        () => typeof(TestClass).GetMethod("TakesWrongArgs").IsRestRouteEligible(true));
                }

                [Fact]
                public void is_rr_eligible_returns_true_when_method_is_eligible()
                {
                    typeof(TestClass).GetMethod("ValidRoute").IsRestRouteEligible().ShouldBeTrue();
                }
            }

            public class IsRestRouteMethod
            {
                [Fact]
                public void is_rest_route_returns_false_when_method_is_not_eligible()
                {
                    typeof(TestClass).GetMethod("HasAttributeButIsNotEligible").IsRestRoute().ShouldBeFalse();
                }

                [Fact]
                public void is_rest_route_returns_false_when_restroute_attribute_is_not_present()
                {
                    typeof(TestClass).GetMethod("IsEligibleButNoAttribute").IsRestRoute().ShouldBeFalse();
                }

                [Fact]
                public void is_rest_route_throws_exception_when_false_and_throw_exceptions_is_true()
                {
                    Should.Throw<InvalidRouteMethodExceptions>(() => typeof(TestClass).GetMethod("HasAttributeButIsNotEligible").IsRestRoute(true));
                    Should.Throw<InvalidRouteMethodExceptions>(() => typeof(TestClass).GetMethod("IsEligibleButNoAttribute").IsRestRoute(true));
                }

                [Fact]
                public void is_rest_route_returns_true_when_attribute_is_present_and_method_is_eligible()
                {

                }
            }
        }
    }

    /* Classes and methods used in testing */

    public class RestRouteTesterHelper
    {
        [RestRoute]
        public IHttpContext RouteHasNoArgs(IHttpContext context)
        {
            return context;
        }

        [RestRoute(HttpMethod = HttpMethod.DELETE)]
        public IHttpContext RouteHasHttpMethodOnly(IHttpContext context)
        {
            return context;
        }

        [RestRoute(PathInfo = "/some/path")]
        public IHttpContext RouteHasPathInfoOnly(IHttpContext context)
        {
            return context;
        }

        [RestRoute(HttpMethod = HttpMethod.POST, PathInfo = "/some/other/path")]
        public IHttpContext RouteHasBothArgs(IHttpContext context)
        {
            return context;
        }

        [RestRoute(HttpMethod = HttpMethod.GET, PathInfo = "/index.html")]
        [RestRoute(HttpMethod = HttpMethod.HEAD, PathInfo = "/index")]
        public IHttpContext RouteHasMultipleAttrs(IHttpContext context)
        {
            return context;
        }
    }

    public class ImplicitConstructor
    {
        public int Value { get; set; }
    }

    public class ExplicitConstructor
    {
        public int Value { get; set; }

        public ExplicitConstructor()
        {
            Value = 30;
        }
    }

    public class MultipleConstructor
    {
        public int Value { get; set; }

        public MultipleConstructor()
        {
            Value = 12;
        }

        public MultipleConstructor(int i)
        {
            Value = i;
        }
    }

    public class NoParameterlessConstructor
    {
        public int Value { get; set; }

        public NoParameterlessConstructor(int i)
        {
            Value = i;
        }

        public void TestMethod() { /* intentionally left blank */ }
    }

    public interface TestInterface
    {
        void TestInterfaceMethod();
    }

    public struct TestStruct
    {
        public int Id { get; set; }

        public void TestStructMethod() { /* intentionally left blank */ }
    }

    public abstract class TestAbstract
    {
        public abstract void TestAbstractMethod();

        public virtual void TestVirtualMethod() { /* intentionally left blank */ }
    }

    public class TestClass
    {
        public string TestProperty { get; set; }

        public static void TestStaticMethod() { /* intentionally left blank */ }

        public void TestMethod() { /* intentionally left blank */ }

        public IHttpContext TakesZeroArgs()
        {
            return null;
        }

        [RestRoute]
        public IHttpContext ValidRoute(IHttpContext context)
        {
            return context;
        }

        public IHttpContext TakesTwoArgs(IHttpContext context, int y)
        {
            return context;
        }

        public IHttpContext TakesWrongArgs(int y)
        {
            return null;
        }

        public void HasNoReturnValue(IHttpContext context) { /* intentionally left blank */ }

        public int ReturnValueIsWrongType(IHttpContext context)
        {
            return 1;
        }

        [RestRoute]
        public void HasAttributeButIsNotEligible() { /* intentionally left blank */ }

        public IHttpContext IsEligibleButNoAttribute(IHttpContext context)
        {
            return context;
        }
    }

    public class MethodInfoWrapper : MethodInfo
    {
        private readonly MethodInfo _methodInfo;

        public override ICustomAttributeProvider ReturnTypeCustomAttributes => _methodInfo.ReturnTypeCustomAttributes;
        public override string Name => _methodInfo.Name;
        public override Type DeclaringType => _methodInfo.DeclaringType;
        public override Type ReflectedType => null;
        public override RuntimeMethodHandle MethodHandle => _methodInfo.MethodHandle;
        public override MethodAttributes Attributes => _methodInfo.Attributes;

        public MethodInfoWrapper(MethodInfo methodInfo)
        {
            _methodInfo = methodInfo;
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            return _methodInfo.GetCustomAttributes(inherit);
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            return _methodInfo.IsDefined(attributeType, inherit);
        }

        public override ParameterInfo[] GetParameters()
        {
            return _methodInfo.GetParameters();
        }

        public override MethodImplAttributes GetMethodImplementationFlags()
        {
            return _methodInfo.GetMethodImplementationFlags();
        }

        public override object Invoke(object obj, BindingFlags invokeAttr, Binder binder, object[] parameters, CultureInfo culture)
        {
            return _methodInfo.Invoke(obj, invokeAttr, binder, parameters, culture);
        }

        public override MethodInfo GetBaseDefinition()
        {
            return _methodInfo.GetBaseDefinition();
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            return _methodInfo.GetCustomAttributes(attributeType, inherit);
        }
    }
}
