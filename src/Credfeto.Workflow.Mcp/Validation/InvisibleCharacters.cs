using System.Text;

namespace Credfeto.Workflow.Mcp.Validation;

public static class InvisibleCharacters
{
    // Code points that render as nothing or as blank space but which Rune.IsWhiteSpace does not report,
    // so a title made only of them would look empty on GitHub
    private static readonly (int Low, int High)[] Ranges =
    [
        (0x00A0, 0x00A0),
        (0x00AD, 0x00AD),
        (0x034F, 0x034F),
        (0x061C, 0x061C),
        (0x115F, 0x1160),
        (0x17B4, 0x17B5),
        (0x180B, 0x180F),
        (0x2007, 0x2007),
        (0x200B, 0x200F),
        (0x202A, 0x202F),
        (0x2060, 0x206F),
        (0x2800, 0x2800),
        (0x3164, 0x3164),
        (0xFE00, 0xFE0F),
        (0xFEFF, 0xFEFF),
        (0xFFA0, 0xFFA0),
        (0xFFF0, 0xFFF8),
        (0x1BCA0, 0x1BCA3),
        (0x1D173, 0x1D17A),
        (0xE0000, 0xE0FFF),
    ];

    public static bool Contains(Rune rune)
    {
        int value = rune.Value;

        foreach ((int low, int high) in Ranges)
        {
            if (value >= low && value <= high)
            {
                return true;
            }
        }

        return false;
    }
}
