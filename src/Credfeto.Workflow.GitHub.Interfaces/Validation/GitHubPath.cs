using System.Text;

namespace Credfeto.Workflow.GitHub.Interfaces.Validation;

public static class GitHubPath
{
    private const string HEX_DIGITS = "0123456789ABCDEF";

    // Slash is kept so a branch such as feature/x maps onto GitHub's path form rather than a single %2F segment
    public static string BranchPath(string branch)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(branch);
        StringBuilder builder = new(bytes.Length * 3);

        foreach (byte value in bytes)
        {
            if (IsUnreserved(value))
            {
                builder.Append((char)value);
            }
            else
            {
                builder.Append('%').Append(HEX_DIGITS[value >> 4]).Append(HEX_DIGITS[value & 0x0F]);
            }
        }

        return builder.ToString();
    }

    private static bool IsUnreserved(byte value)
    {
        return value
            is >= (byte)'A'
                and <= (byte)'Z'
                or >= (byte)'a'
                and <= (byte)'z'
                or >= (byte)'0'
                and <= (byte)'9'
                or (byte)'.'
                or (byte)'_'
                or (byte)'~'
                or (byte)'-'
                or (byte)'/';
    }
}
