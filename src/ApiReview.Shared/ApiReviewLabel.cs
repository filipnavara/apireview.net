using System.Drawing;
using System.Globalization;
using System.Text;

namespace ApiReviewDotNet.Data;

public sealed class ApiReviewLabel
{
    public ApiReviewLabel(string name,
                          string color,
                          string description)
    {
        Name = name;
        Color = color;
        Description = description;
    }

    public string Name { get; }
    public string Color { get; }
    public string Description { get; }

    public string GetStyle()
    {
        var color = ParseColor(Color);
        var labelR = color.R;
        var labelG = color.G;
        var labelB = color.B;
        var labelH = color.GetHue();
        var labelS = color.GetSaturation() * 100;
        var labelL = color.GetBrightness() * 100;
        var sb = new StringBuilder();
        sb.Append($"--label-r: {labelR};");
        sb.Append($"--label-g: {labelG};");
        sb.Append($"--label-b: {labelB};");
        sb.Append($"--label-h: {labelH};");
        sb.Append($"--label-s: {labelS};");
        sb.Append($"--label-l: {labelL};");
        return sb.ToString();
    }

    private static Color ParseColor(string color)
    {
        if (!string.IsNullOrEmpty(color) && color.Length == 6 &&
            int.TryParse(color.AsSpan(0, 2), NumberStyles.HexNumber, null, out var r) &&
            int.TryParse(color.AsSpan(2, 2), NumberStyles.HexNumber, null, out var g) &&
            int.TryParse(color.AsSpan(4, 2), NumberStyles.HexNumber, null, out var b))
        {
            return System.Drawing.Color.FromArgb(r, g, b);
        }

        return System.Drawing.Color.Black;
    }
}
