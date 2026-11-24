using System;
using System.Reflection;
using UnityEngine;

namespace CpcPrototype.Editor
{
    public static class CpcResponseFilterChecks
    {
        public static string Run()
        {
            CpcResponsePolicy policy = ScriptableObject.CreateInstance<CpcResponsePolicy>();
            GameObject temporary = null;
            int passed = 0;
            try
            {
                Check("같이 몬스터를 피해 가자!", false, policy, ref passed);
                Check("씨발", true, policy, ref passed);
                Check("씨 . 발", true, policy, ref passed);
                Check("씨\u200B발", true, policy, ref passed);
                Check("ㅅㅂ", true, policy, ref passed);
                Check("FUCK!", true, policy, ref passed);
                Check("ｆｕｃｋ", true, policy, ref passed);
                Check("f.u.c.k", true, policy, ref passed);
                Check("that is shit", true, policy, ref passed);
                Check("shitake mushroom", false, policy, ref passed);
                policy.Replacement = "씨발";
                string safe = CpcResponseFilter.Filter("씨발", policy, out bool blocked);
                if (!blocked || safe == policy.Replacement) throw new Exception("Unsafe replacement escaped filter.");
                passed++;
                policy.EnableFilter = false;
                if (CpcResponseFilter.Filter("씨발", policy, out blocked) != "씨발" || blocked) throw new Exception("Disabled filter check failed.");
                passed++;
                policy.EnableFilter = true;
                CpcLlmSettings settings = ScriptableObject.CreateInstance<CpcLlmSettings>();
                try
                {
                    settings.ResponsePolicy = policy;
                    temporary = new GameObject("CpcResponseFilterVerification");
                    LocalLlmClient client = temporary.AddComponent<LocalLlmClient>();
                    client.Configure(settings);
                    string received = null;
                    client.Replied += message => received = message;
                    typeof(LocalLlmClient).GetMethod("Publish", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(client, new object[] { "씨발", true });
                    if (client.LastReply != safe || received != safe) throw new Exception("Output publication bypassed filter.");
                    passed++;
                }
                finally { UnityEngine.Object.DestroyImmediate(settings); }
                return "PASS: " + passed + " response filter checks";
            }
            finally
            {
                if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                UnityEngine.Object.DestroyImmediate(policy);
            }
        }

        private static void Check(string text, bool expectedBlocked, CpcResponsePolicy policy, ref int passed)
        {
            string result = CpcResponseFilter.Filter(text, policy, out bool blocked);
            if (blocked != expectedBlocked || (blocked && result == text))
                throw new Exception("Response filter case failed: " + passed);
            passed++;
        }
    }
}
