using System;
using System.Globalization;
using System.Text;

namespace CpcPrototype
{
    public static class CpcResponseFilter
    {
        private const string SAFE_REPLACEMENT = "무슨소릴 하는거야 정신차려.";

        public static string Filter(string response, CpcResponsePolicy policy, out bool blocked)
        {
            blocked = false;
            if (string.IsNullOrWhiteSpace(response)) return SAFE_REPLACEMENT;
            if (policy == null || !policy.EnableFilter) return response;
            if (!ContainsBlockedExpression(response, policy.BlockedExpressions)) return response;
            blocked = true;
            string replacement = policy.Replacement;
            // 설정자가 대체 대사에 금지어를 넣었더라도 출력하지 않는다.
            return string.IsNullOrWhiteSpace(replacement) || ContainsBlockedExpression(replacement, policy.BlockedExpressions)
                ? SAFE_REPLACEMENT : replacement;
        }

        private static bool ContainsBlockedExpression(string text, string[] expressions)
        {
            if (expressions == null) return false;
            string canonical = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            string compact = Compact(canonical);
            for (int i = 0; i < expressions.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(expressions[i])) continue;
                string token = Compact(expressions[i].Normalize(NormalizationForm.FormKC).ToLowerInvariant());
                if (token.Length == 0) continue;
                bool ascii = IsAsciiWord(token);
                int offset = 0;
                while ((offset = compact.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
                {
                    // 영문 부분 문자열로 정상 단어를 차단하지 않는다. 예: "class"의 "ass".
                    if (!ascii || ((offset == 0 || !IsAsciiLetter(compact[offset - 1])) &&
                        (offset + token.Length == compact.Length || !IsAsciiLetter(compact[offset + token.Length])))) return true;
                    offset++;
                }
                // compact에서는 공백 경계도 사라지므로 원문의 영문 단어 경계를 별도로 검사한다.
                if (ascii)
                {
                    offset = 0;
                    while ((offset = canonical.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
                    {
                        if ((offset == 0 || !IsAsciiLetter(canonical[offset - 1])) &&
                            (offset + token.Length == canonical.Length || !IsAsciiLetter(canonical[offset + token.Length]))) return true;
                        offset++;
                    }
                }
            }
            return false;
        }

        private static bool IsAsciiLetter(char value) => value >= 'a' && value <= 'z';
        private static bool IsAsciiWord(string value)
        {
            for (int i = 0; i < value.Length; i++) if (!IsAsciiLetter(value[i])) return false;
            return true;
        }
        private static string Compact(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                UnicodeCategory category = char.GetUnicodeCategory(value[i]);
                if (char.IsLetterOrDigit(value[i]) || category == UnicodeCategory.NonSpacingMark) builder.Append(value[i]);
            }
            return builder.ToString();
        }
    }
}
