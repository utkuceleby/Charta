namespace Charta.Svg;

/// <summary>A 2-D affine transform [a b c d e f] mapping (x, y) → (a·x + c·y + e, b·x + d·y + f).</summary>
internal readonly record struct SvgMatrix(double A, double B, double C, double D, double E, double F)
{
    public static readonly SvgMatrix Identity = new(1, 0, 0, 1, 0, 0);

    public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

    /// <summary>this ∘ o — applies <paramref name="o"/> first, then this.</summary>
    public SvgMatrix Multiply(SvgMatrix o) => new(
        A * o.A + C * o.B,
        B * o.A + D * o.B,
        A * o.C + C * o.D,
        B * o.C + D * o.D,
        A * o.E + C * o.F + E,
        B * o.E + D * o.F + F);

    public static SvgMatrix Translate(double x, double y) => new(1, 0, 0, 1, x, y);

    public static SvgMatrix Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    public static SvgMatrix Rotate(double degrees)
    {
        var r = degrees * Math.PI / 180.0;
        var cos = Math.Cos(r);
        var sin = Math.Sin(r);
        return new SvgMatrix(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Average scale factor, used to scale stroke widths under a transform.</summary>
    public double MeanScale => Math.Sqrt(Math.Abs(A * D - B * C));
}
