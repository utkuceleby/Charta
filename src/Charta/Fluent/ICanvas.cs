namespace Charta;

/// <summary>
/// A vector drawing surface with a top-left origin, measured in points. Path methods build the
/// current path (and chain); paint methods draw it and start a new one — the PDF path/paint model.
/// The origin (0, 0) is the top-left of the canvas area and y grows downward.
/// </summary>
public interface ICanvas
{
    /// <summary>The drawable width in points.</summary>
    double Width { get; }

    /// <summary>The drawable height in points.</summary>
    double Height { get; }

    /// <summary>Starts a new subpath at the given point.</summary>
    ICanvas MoveTo(double x, double y);

    /// <summary>Adds a straight line from the current point.</summary>
    ICanvas LineTo(double x, double y);

    /// <summary>Adds a cubic Bézier curve with the two control points and an end point.</summary>
    ICanvas CurveTo(double control1X, double control1Y, double control2X, double control2Y, double endX, double endY);

    /// <summary>Adds a rectangle subpath.</summary>
    ICanvas Rectangle(double x, double y, double width, double height);

    /// <summary>Adds a circle subpath.</summary>
    ICanvas Circle(double centerX, double centerY, double radius);

    /// <summary>Adds an ellipse subpath.</summary>
    ICanvas Ellipse(double centerX, double centerY, double radiusX, double radiusY);

    /// <summary>Closes the current subpath back to its start.</summary>
    ICanvas Close();

    /// <summary>Fills the current path with the color and clears it.</summary>
    void Fill(Color color);

    /// <summary>Strokes the current path with the color and line width, then clears it.</summary>
    void Stroke(Color color, double lineWidth = 1);

    /// <summary>Fills and strokes the current path, then clears it.</summary>
    void FillAndStroke(Color fillColor, Color strokeColor, double lineWidth = 1);
}
