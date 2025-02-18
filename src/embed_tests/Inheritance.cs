using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using NUnit.Framework;

using Python.Runtime;

namespace Python.EmbeddingTest
{
    public class Inheritance
    {
        [OneTimeSetUp]
        public void SetUp()
        {
            PythonEngine.Initialize();
            var locals = new PyDict();
            PythonEngine.Exec(InheritanceTestBaseClassWrapper.ClassSourceCode, locals: locals.Handle);
            ExtraBaseTypeProvider.ExtraBase = new PyType(locals[InheritanceTestBaseClassWrapper.ClassName]);
            var baseTypeProviders = PythonEngine.InteropConfiguration.PythonBaseTypeProviders;
            baseTypeProviders.Add(new ExtraBaseTypeProvider());
            baseTypeProviders.Add(new NoEffectBaseTypeProvider());
        }

        [OneTimeTearDown]
        public void Dispose()
        {
            PythonEngine.Shutdown();
        }

        [Test]
        public void ExtraBase_PassesInstanceCheck()
        {
            var inherited = new Inherited();
            bool properlyInherited = PyIsInstance(inherited, ExtraBaseTypeProvider.ExtraBase);
            Assert.IsTrue(properlyInherited);
        }

        static dynamic PyIsInstance => PythonEngine.Eval("isinstance");

        [Test]
        public void InheritingWithExtraBase_CreatesNewClass()
        {
            PyObject a = ExtraBaseTypeProvider.ExtraBase;
            var inherited = new Inherited();
            PyObject inheritedClass = inherited.ToPython().GetAttr("__class__");
            Assert.IsFalse(PythonReferenceComparer.Instance.Equals(a, inheritedClass));
        }

        [Test]
        public void InheritedFromInheritedClassIsSelf()
        {
            using var scope = Py.CreateScope();
            scope.Exec($"from {typeof(Inherited).Namespace} import {nameof(Inherited)}");
            scope.Exec($"class B({nameof(Inherited)}): pass");
            PyObject b = scope.Eval("B");
            PyObject bInstance = b.Invoke();
            PyObject bInstanceClass = bInstance.GetAttr("__class__");
            Assert.IsTrue(PythonReferenceComparer.Instance.Equals(b, bInstanceClass));
        }

        [Test]
        public void Grandchild_PassesExtraBaseInstanceCheck()
        {
            using var scope = Py.CreateScope();
            scope.Exec($"from {typeof(Inherited).Namespace} import {nameof(Inherited)}");
            scope.Exec($"class B({nameof(Inherited)}): pass");
            PyObject b = scope.Eval("B");
            PyObject bInst = b.Invoke();
            bool properlyInherited = PyIsInstance(bInst, ExtraBaseTypeProvider.ExtraBase);
            Assert.IsTrue(properlyInherited);
        }

        [Test]
        public void CallInheritedClrMethod_WithExtraPythonBase()
        {
            var instance = new Inherited().ToPython();
            string result = instance.InvokeMethod(nameof(PythonWrapperBase.WrapperBaseMethod)).As<string>();
            Assert.AreEqual(result, nameof(PythonWrapperBase.WrapperBaseMethod));
        }

        [Test]
        public void CallExtraBaseMethod()
        {
            var instance = new Inherited();
            using var scope = Py.CreateScope();
            scope.Set(nameof(instance), instance);
            int actual = instance.ToPython().InvokeMethod("callVirt").As<int>();
            Assert.AreEqual(expected: Inherited.OverridenVirtValue, actual);
        }

        [Test]
        public void SetAdHocAttributes_WhenExtraBasePresent()
        {
            var instance = new Inherited();
            using var scope = Py.CreateScope();
            scope.Set(nameof(instance), instance);
            scope.Exec($"super({nameof(instance)}.__class__, {nameof(instance)}).set_x_to_42()");
            int actual = scope.Eval<int>($"{nameof(instance)}.{nameof(Inherited.XProp)}");
            Assert.AreEqual(expected: Inherited.X, actual);
        }
    }

    class ExtraBaseTypeProvider : IPythonBaseTypeProvider
    {
        internal static PyType ExtraBase;
        public IEnumerable<PyType> GetBaseTypes(Type type, IList<PyType> existingBases)
        {
            if (type == typeof(InheritanceTestBaseClassWrapper))
            {
                return new[] { PyType.Get(type.BaseType), ExtraBase };
            }
            return existingBases;
        }
    }

    class NoEffectBaseTypeProvider : IPythonBaseTypeProvider
    {
        public IEnumerable<PyType> GetBaseTypes(Type type, IList<PyType> existingBases)
            => existingBases;
    }

    public class PythonWrapperBase
    {
        public string WrapperBaseMethod() => nameof(WrapperBaseMethod);
    }

    public class InheritanceTestBaseClassWrapper : PythonWrapperBase
    {
        public const string ClassName = "InheritanceTestBaseClass";
        public const string ClassSourceCode = "class " + ClassName +
@":
  def virt(self):
    return 42
  def set_x_to_42(self):
    self.XProp = 42
  def callVirt(self):
    return self.virt()
  def __getattr__(self, name):
    return '__getattr__:' + name
  def __setattr__(self, name, value):
    value[name] = name
" + ClassName + " = " + ClassName + "\n";
    }

    public class Inherited : InheritanceTestBaseClassWrapper
    {
        public const int OverridenVirtValue = -42;
        public const int X = 42;
        readonly Dictionary<string, object> extras = new Dictionary<string, object>();
        public int virt() => OverridenVirtValue;
        public int XProp
        {
            get
            {
                using (var scope = Py.CreateScope())
                {
                    scope.Set("this", this);
                    try
                    {
                        return scope.Eval<int>($"super(this.__class__, this).{nameof(XProp)}");
                    }
                    catch (PythonException ex) when (ex.Type.Handle == Exceptions.AttributeError)
                    {
                        if (this.extras.TryGetValue(nameof(this.XProp), out object value))
                            return (int)value;
                        throw;
                    }
                }
            }
            set => this.extras[nameof(this.XProp)] = value;
        }
    }
}
