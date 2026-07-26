using System;

namespace VideoMaterialRenamer.Tests
{
    // Minimal assertion helpers for the framework-free C#5 test harness.
    public static class TestAssert
    {
        public static void AreEqual(object expected, object actual, string context)
        {
            if (!object.Equals(expected, actual))
            {
                throw new Exception(string.Format("{0} -- expected <{1}> but got <{2}>",
                    context,
                    expected == null ? "(null)" : expected.ToString(),
                    actual == null ? "(null)" : actual.ToString()));
            }
        }

        public static void IsTrue(bool condition, string context)
        {
            if (!condition)
            {
                throw new Exception(context + " -- expected true but was false");
            }
        }

        public static void IsFalse(bool condition, string context)
        {
            if (condition)
            {
                throw new Exception(context + " -- expected false but was true");
            }
        }

        public static void IsNull(object value, string context)
        {
            if (value != null)
            {
                throw new Exception(context + " -- expected null but got <" + value + ">");
            }
        }

        public static void IsNotNull(object value, string context)
        {
            if (value == null)
            {
                throw new Exception(context + " -- expected non-null but got null");
            }
        }

        public static TException Throws<TException>(Action action, string context) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException expected)
            {
                return expected;
            }
            catch (Exception other)
            {
                throw new Exception(string.Format("{0} -- expected {1} but got {2}: {3}",
                    context, typeof(TException).Name, other.GetType().Name, other.Message));
            }

            throw new Exception(string.Format("{0} -- expected {1} but no exception was thrown",
                context, typeof(TException).Name));
        }
    }
}
