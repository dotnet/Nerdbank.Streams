// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace IsolatedTestHost
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 4)
            {
                return (int)ExitCodes.UnexpectedCommandLineArgs;
            }

            string assemblyFile = args[0];
            string testClassName = args[1];
            string testMethodName = args[2];
            bool launchDebugger = bool.Parse(args[3]);
            if (launchDebugger)
            {
                Debugger.Launch();
            }

#if NETFRAMEWORK
            string configFile = assemblyFile + ".config";
            if (File.Exists(configFile))
            {
                var appDomainSetup = new AppDomainSetup();
                appDomainSetup.ConfigurationFile = configFile;
                var appDomain = AppDomain.CreateDomain("test host", null, appDomainSetup);
                var remote = (Remotable)appDomain.CreateInstanceAndUnwrap(typeof(Program).Assembly.GetName().FullName, typeof(Remotable).FullName);
                return (int)remote.MyMain(assemblyFile, testClassName, testMethodName);
            }
#endif

            return (int)MyMain(assemblyFile, testClassName, testMethodName);
        }

        private static ExitCodes MyMain(string assemblyFile, string testClassName, string testMethodName)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(assemblyFile);
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitCodes.AssemblyNotFound;
            }

            Type? testClass = assembly.GetType(testClassName);
            if (testClass == null)
            {
                return ExitCodes.TestClassNotFound;
            }

            MethodInfo? testMethod = testClass.GetRuntimeMethod(testMethodName, Type.EmptyTypes);
            if (testMethod == null)
            {
                return ExitCodes.TestMethodNotFound;
            }

            // TUnit's [Test] attribute.
            bool test = testMethod.GetCustomAttributesData().Any(a => a.AttributeType.Name == "TestAttribute");
            if (test)
            {
                return ExecuteTest(testClass, testMethod);
            }

            bool stafact = testMethod.GetCustomAttributesData().Any(a => a.AttributeType.Name == "StaFactAttribute");
            if (stafact)
            {
                ExitCodes result = ExitCodes.TestFailed;
                var testThread = new Thread(() =>
                {
                    result = ExecuteTest(testClass, testMethod);
                });
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    testThread.SetApartmentState(ApartmentState.STA);
                }

                testThread.Start();
                testThread.Join();
                return result;
            }

            return ExitCodes.TestNotSupported;
        }

        private static ExitCodes ExecuteTest(Type testClass, MethodInfo testMethod)
        {
            try
            {
                ConstructorInfo? ctorDefault = testClass.GetConstructor(Type.EmptyTypes);
                object? testClassInstance = ctorDefault?.Invoke(Type.EmptyTypes);
                if (testClassInstance == null)
                {
                    return ExitCodes.TestNotSupported;
                }

                // Mirror TUnit's lifecycle: run [Before(Test)] InitializeAsync, the test, then DisposeAsync or Dispose.
                InvokeAndWait(testClassInstance, "InitializeAsync");

                object? result = testMethod.Invoke(testClassInstance, Type.EmptyTypes);
                WaitForResult(result);

                if (!InvokeAndWait(testClassInstance, "DisposeAsync") && testClassInstance is IDisposable disposableTestClass)
                {
                    disposableTestClass.Dispose();
                }

                return ExitCodes.TestPassed;
            }
            catch (Exception ex)
            {
                if (ex is TargetInvocationException { InnerException: { } inner })
                {
                    ex = inner;
                }

                // TUnit's Skip.Test/Skip.When throw SkipTestException.
                if (ex.GetType().Name == "SkipTestException")
                {
                    return ExitCodes.TestSkipped;
                }

                Console.Error.WriteLine("Test failed.");
                Console.Error.WriteLine(ex);
                return ExitCodes.TestFailed;
            }
        }

        private static bool InvokeAndWait(object instance, string methodName)
        {
            MethodInfo? method = instance.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method is null)
            {
                return false;
            }

            WaitForResult(method.Invoke(instance, Type.EmptyTypes));
            return true;
        }

        private static void WaitForResult(object? result)
        {
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
            else if (result?.GetType().GetMethod("AsTask", Type.EmptyTypes) is MethodInfo asTask)
            {
                // ValueTask or ValueTask<T>
                ((Task)asTask.Invoke(result, Type.EmptyTypes)!).GetAwaiter().GetResult();
            }
        }

        private class Remotable : MarshalByRefObject
        {
            internal ExitCodes MyMain(string assemblyFile, string testClassName, string testMethodName)
            {
                return Program.MyMain(assemblyFile, testClassName, testMethodName);
            }
        }
    }
}
