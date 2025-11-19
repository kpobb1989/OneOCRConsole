using System.Collections.Generic;
using System.Drawing;

namespace L2InGameVision.OneOcr.Models
{
    public class OcrLine
    {
        public string? Text { get; init; }
        public float X1 { get; init; }
        public float Y1 { get; init; }
        public float X2 { get; init; }
        public float Y2 { get; init; }
        public float X3 { get; init; }
        public float Y3 { get; init; }
        public float X4 { get; init; }
        public float Y4 { get; init; }
        public IReadOnlyList<OcrWord> Words { get; init; } = [];

        public RectangleF BoundingBox => new(X1, Y1, X2 - X1, Y4 - Y1);

        public override string ToString()
        {
            return $"{Text}: ({X1},{Y1}),({X2},{Y2}),({X3},{Y3}),({X4},{Y4})";
        }
    }
}
