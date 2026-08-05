using System.Collections.Generic;
using System.Windows;

namespace PDFMarkup.Models;

public sealed class StrokeModel
{
    public List<Point> PdfPoints { get; } = new();

    public double Thickness { get; set; } = 3.0;

    public StrokeColor Color { get; set; } = StrokeColor.Red;
}

public enum StrokeColor
{
    Red,
    Blue,
    Green
}