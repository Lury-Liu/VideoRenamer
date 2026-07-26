using System;
using System.Collections.Generic;

namespace VideoMaterialRenamer.Tests
{
    public sealed class TestCase
    {
        public readonly string Name;
        public readonly Action Body;

        public TestCase(string name, Action body)
        {
            Name = name;
            Body = body;
        }
    }

    // Framework-free C#5 test harness. Unlike the former monolithic RunSelfTest,
    // it runs ALL cases (no first-failure abort) and reports each by name.
    //
    // FROZEN CONTRACT: RunAll() returns the exact string "SelfTest OK" on success
    // and throws on any failure - the dev loader echoes the return value and the
    // packaging/publish scripts gate on that marker.
    public static class TestRunner
    {
        public static List<TestCase> CollectAll()
        {
            List<TestCase> cases = new List<TestCase>();
            cases.AddRange(CoreTests.Cases());
            cases.AddRange(MediaTests.Cases());
            cases.AddRange(ServicesTests.Cases());
            cases.AddRange(LicensingTests.Cases());
            cases.AddRange(AppTests.Cases());
            cases.AddRange(GoldenMasterTests.Cases());
            return cases;
        }

        public static string RunAll()
        {
            List<TestCase> cases = CollectAll();
            List<string> failures = new List<string>();

            foreach (TestCase testCase in cases)
            {
                try
                {
                    testCase.Body();
                    Console.WriteLine("  [PASS] " + testCase.Name);
                }
                catch (Exception ex)
                {
                    failures.Add(testCase.Name + ": " + ex.Message);
                    Console.WriteLine("  [FAIL] " + testCase.Name + " -- " + ex.Message);
                }
            }

            Console.WriteLine(string.Format("Self-test cases: {0} total, {1} passed, {2} failed.",
                cases.Count, cases.Count - failures.Count, failures.Count));

            if (failures.Count > 0)
            {
                throw new Exception(string.Format("SelfTest FAILED ({0} case(s)):\r\n{1}",
                    failures.Count, string.Join("\r\n", failures.ToArray())));
            }

            return "SelfTest OK";
        }
    }
}
