using System.Text;

namespace Epubra.Epub;

/// <summary>
/// 将字符串清理为合法的 epub 文件名（不含路径、不含扩展名）。
/// </summary>
internal static class EpubSafeName
{
    /// <summary>把任意字符串转为安全的章节文件名片段（ASCII/中文都可，移除特殊字符）。</summary>
    public static string Sanitize(string input, int maxLength = 32)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c >= '\u4e00')
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append('_');
            }
            // 其他字符（路径分隔符、ASCII 控制字符等）丢弃
        }

        var result = sb.ToString().Trim('_', '-');
        if (string.IsNullOrEmpty(result))
        {
            result = "chapter";
        }

        return result.Length > maxLength ? result[..maxLength] : result;
    }
}